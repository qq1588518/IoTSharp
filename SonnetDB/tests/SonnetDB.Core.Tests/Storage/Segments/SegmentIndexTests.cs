using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Format;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Segments;

/// <summary>
/// <see cref="SegmentIndex"/> 单元测试：验证单段内存索引的构建与时间窗剪枝。
/// </summary>
public sealed class SegmentIndexTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SegmentWriter _writer = new(new SegmentWriterOptions { FsyncOnCommit = false });

    public SegmentIndexTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string TempPath(string name = "test.SDBSEG") =>
        Path.Combine(_tempDir, name);

    // ── 5 series × 3 field 段 ────────────────────────────────────────────────

    private static MemTable Build5SeriesMemTable()
    {
        var mt = new MemTable();
        long lsn = 1;
        for (int s = 0; s < 5; s++)
        {
            ulong sid = (ulong)(s + 1) * 1000UL;
            for (int f = 0; f < 3; f++)
            {
                string field = $"field{f}";
                for (int i = 0; i < 5; i++)
                {
                    mt.Append(sid, 1000L + i * 100L, field, FieldValue.FromDouble(i * 1.0), lsn++);
                }
            }
        }
        return mt;
    }

    [Fact]
    public void GetBlocks_BySeries_Returns3Blocks()
    {
        string path = TempPath();
        var mt = Build5SeriesMemTable();
        _writer.WriteFrom(mt, 1L, path);

        using var reader = SegmentReader.Open(path);
        var index = SegmentIndex.Build(reader, 1L);

        ulong sid = 1000UL; // first series
        var blocks = index.GetBlocks(sid);

        // 3 fields → 3 blocks per series
        Assert.Equal(3, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(sid, b.SeriesId));
    }

    [Fact]
    public void GetBlocks_BySeriesAndField_Returns1Block()
    {
        string path = TempPath();
        var mt = Build5SeriesMemTable();
        _writer.WriteFrom(mt, 1L, path);

        using var reader = SegmentReader.Open(path);
        var index = SegmentIndex.Build(reader, 1L);

        ulong sid = 2000UL;
        var blocks = index.GetBlocks(sid, "field1");

        Assert.Single(blocks);
        Assert.Equal(sid, blocks[0].SeriesId);
        Assert.Equal("field1", blocks[0].FieldName);
    }

    [Fact]
    public void GetBlocks_WithTimeRange_ReturnsOnlyOverlappingBlocks()
    {
        string path = TempPath();
        var mt = Build5SeriesMemTable();
        _writer.WriteFrom(mt, 1L, path);

        using var reader = SegmentReader.Open(path);
        var index = SegmentIndex.Build(reader, 1L);

        ulong sid = 3000UL;
        // points: 1000, 1100, 1200, 1300, 1400
        // block covers [1000, 1400]
        var all = index.GetBlocks(sid, "field0", 1000L, 1400L);
        Assert.Single(all);

        // no overlap
        var none = index.GetBlocks(sid, "field0", 2000L, 3000L);
        Assert.Empty(none);
    }

    [Fact]
    public void GetBlocks_UnknownSeries_ReturnsEmpty_NoAlloc()
    {
        string path = TempPath();
        _writer.WriteFrom(new MemTable(), 1L, path);
        using var reader = SegmentReader.Open(path);
        var index = SegmentIndex.Build(reader, 1L);

        var result = index.GetBlocks(99999UL);
        Assert.Same(Array.Empty<BlockDescriptor>(), result);
    }

    [Fact]
    public void GetBlocks_UnknownSeriesAndField_ReturnsEmpty_NoAlloc()
    {
        string path = TempPath();
        _writer.WriteFrom(new MemTable(), 1L, path);
        using var reader = SegmentReader.Open(path);
        var index = SegmentIndex.Build(reader, 1L);

        var result = index.GetBlocks(99999UL, "usage");
        Assert.Same(Array.Empty<BlockDescriptor>(), result);
    }

    [Fact]
    public void OverlapsTimeRange_ReturnsFalse_WhenNoOverlap()
    {
        string path = TempPath();
        var mt = Build5SeriesMemTable();
        _writer.WriteFrom(mt, 1L, path);

        using var reader = SegmentReader.Open(path);
        var index = SegmentIndex.Build(reader, 1L);

        long min = index.MinTimestamp;
        Assert.False(index.OverlapsTimeRange(min - 100, min - 1));
    }

    [Fact]
    public void OverlapsTimeRange_ReturnsTrue_WhenFullyInside()
    {
        string path = TempPath();
        var mt = Build5SeriesMemTable();
        _writer.WriteFrom(mt, 1L, path);

        using var reader = SegmentReader.Open(path);
        var index = SegmentIndex.Build(reader, 1L);

        Assert.True(index.OverlapsTimeRange(index.MinTimestamp, index.MaxTimestamp));
    }

    // ── 单 series+field 内多个 Block 时间窗剪枝 ──────────────────────────────

    /// <summary>
    /// 构造一个含 5 个时间相邻 Block 的段（每个 Block 独占一个字段，通过多次 flush 分块）。
    /// 直接注入 5 个来自不同时间段的相同 series+field，由于 MemTable 每个 (seriesId, fieldName)
    /// 只生成一个 Block，改为用 SegmentWriter 分步写入或借助不同字段后再通过 Build 验证。
    ///
    /// 本测试通过 SegmentWriter.WriteFrom 多段叠加来模拟多 Block 场景。
    /// 实际上，单个 MemTable 对每个 (seriesId, fieldName) 组合只产生一个 Block。
    /// 此处使用多个不同时间窗的独立 MemTable 构成的"多 SegmentIndex"来验证剪枝。
    /// </summary>
    [Fact]
    public void GetBlocks_WithTimeRange_MultiBlockPruning_CorrectResult()
    {
        ulong sid = 0xABCDUL;
        const string field = "usage";

        // 5 个 Block，每个时间窗不重叠：[t0, t1], [t1+1, t2], ...
        // 通过写入 5 个独立段文件然后对 SegmentIndex 逐一验证

        var blocks = new (long min, long max)[]
        {
            (100, 200),
            (300, 400),
            (500, 600),
            (700, 800),
            (900, 1000),
        };

        // 将所有数据点写入同一 MemTable（一个 series+field → 一个大 Block），不能测多 Block 剪枝
        // 因此改为：通过 SegmentIndex 内手动填充来测试 GetBlocks 逻辑，使用公开 API Build

        // 每个时间窗写一个独立段，然后用多段的 SegmentIndex 验证
        var indices = new List<SegmentIndex>();
        for (int i = 0; i < blocks.Length; i++)
        {
            var (min, max) = blocks[i];
            var mt = new MemTable();
            long lsn = 1;
            long step = Math.Max(10L, (max - min) / 10);
            for (long ts = min; ts < max; ts += step)
                mt.Append(sid, ts, field, SegmentIndexTestHelpers.MakeDouble(ts), lsn++);
            mt.Append(sid, max, field, SegmentIndexTestHelpers.MakeDouble(max), lsn++);

            string segPath = TempPath($"seg{i}.SDBSEG");
            _writer.WriteFrom(mt, (long)(i + 1), segPath);
            using var r = SegmentReader.Open(segPath);
            indices.Add(SegmentIndex.Build(r, i + 1));
        }

        // 查询 [blocks[1].min, blocks[2].max] → 应返回 Block1 + Block2 (index 1 & 2 in 0-based)
        var multi = new MultiSegmentIndex(indices);
        var candidates = multi.LookupCandidates(sid, field, blocks[1].min, blocks[2].max);

        Assert.Equal(2, candidates.Count);
        Assert.Equal(blocks[1].min, candidates[0].MinTimestamp);
        Assert.Equal(blocks[2].max, candidates[1].MaxTimestamp);
    }
}

/// <summary>
/// 测试辅助方法。
/// </summary>
internal static class SegmentIndexTestHelpers
{
    internal static FieldValue MakeDouble(long value) => FieldValue.FromDouble((double)value);
}
