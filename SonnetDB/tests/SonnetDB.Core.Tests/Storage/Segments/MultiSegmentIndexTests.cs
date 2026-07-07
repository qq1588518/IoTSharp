using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Segments;

/// <summary>
/// <see cref="MultiSegmentIndex"/> 单元测试：验证跨段联合索引快照的构建与查询。
/// </summary>
public sealed class MultiSegmentIndexTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SegmentWriter _writer = new(new SegmentWriterOptions { FsyncOnCommit = false });

    public MultiSegmentIndexTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string TempPath(string name) => Path.Combine(_tempDir, name);

    private SegmentIndex BuildSegment(long segId, ulong seriesId, string field, long minTs, long maxTs)
    {
        var mt = new MemTable();
        long lsn = 1;
        long step = Math.Max(1L, (maxTs - minTs) / 5);
        for (long ts = minTs; ts < maxTs; ts += step)
            mt.Append(seriesId, ts, field, FieldValue.FromDouble((double)ts), lsn++);
        mt.Append(seriesId, maxTs, field, FieldValue.FromDouble((double)maxTs), lsn++);

        string path = TempPath($"seg{segId:X16}.SDBSEG");
        _writer.WriteFrom(mt, segId, path);
        using var reader = SegmentReader.Open(path);
        return SegmentIndex.Build(reader, segId);
    }

    // ── Empty 单例 ──────────────────────────────────────────────────────────

    [Fact]
    public void Empty_HasZeroSegments()
    {
        var empty = MultiSegmentIndex.Empty;
        Assert.Equal(0, empty.SegmentCount);
    }

    [Fact]
    public void Empty_MinIsMaxValue_MaxIsMinValue()
    {
        var empty = MultiSegmentIndex.Empty;
        Assert.Equal(long.MaxValue, empty.MinTimestamp);
        Assert.Equal(long.MinValue, empty.MaxTimestamp);
    }

    [Fact]
    public void Empty_LookupCandidates_ReturnsEmpty()
    {
        var empty = MultiSegmentIndex.Empty;
        Assert.Empty(empty.LookupCandidates(1UL, "f", 0L, long.MaxValue));
        Assert.Empty(empty.LookupCandidates(1UL, 0L, long.MaxValue));
    }

    // ── 0 段 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ZeroSegments_LookupCandidates_ReturnsEmpty()
    {
        var multi = new MultiSegmentIndex(Array.Empty<SegmentIndex>());
        Assert.Empty(multi.LookupCandidates(42UL, "value", 0L, long.MaxValue));
    }

    // ── 3 段：同 series 不同时间窗 ─────────────────────────────────────────

    [Fact]
    public void ThreeSegments_LookupCandidates_ReturnsInSegmentIdOrder()
    {
        ulong sid = 0xBEEFUL;
        const string field = "temp";

        // Segment 1: [1000, 2000], Segment 2: [2001, 3000], Segment 3: [3001, 4000]
        var idx1 = BuildSegment(1L, sid, field, 1000L, 2000L);
        var idx2 = BuildSegment(2L, sid, field, 2001L, 3000L);
        var idx3 = BuildSegment(3L, sid, field, 3001L, 4000L);

        var multi = new MultiSegmentIndex(new[] { idx1, idx2, idx3 });

        // Query all time range
        var all = multi.LookupCandidates(sid, field, 1000L, 4000L);
        Assert.Equal(3, all.Count);
        Assert.Equal(1L, all[0].SegmentId);
        Assert.Equal(2L, all[1].SegmentId);
        Assert.Equal(3L, all[2].SegmentId);
    }

    [Fact]
    public void ThreeSegments_LookupCandidates_WithTimeFilter_ReturnsOnlyIntersecting()
    {
        ulong sid = 0xCAFEUL;
        const string field = "cpu";

        var idx1 = BuildSegment(1L, sid, field, 1000L, 2000L);
        var idx2 = BuildSegment(2L, sid, field, 3000L, 4000L);
        var idx3 = BuildSegment(3L, sid, field, 5000L, 6000L);

        var multi = new MultiSegmentIndex(new[] { idx1, idx2, idx3 });

        // Query only covers segment 2 range
        var result = multi.LookupCandidates(sid, field, 3000L, 4000L);
        var single = Assert.Single(result);
        Assert.Equal(2L, single.SegmentId);
    }

    [Fact]
    public void ThreeSegments_LookupCandidates_NoMatch_ReturnsEmpty()
    {
        ulong sid = 0xDEADUL;
        const string field = "disk";

        var idx1 = BuildSegment(1L, sid, field, 1000L, 2000L);
        var idx2 = BuildSegment(2L, sid, field, 3000L, 4000L);
        var idx3 = BuildSegment(3L, sid, field, 5000L, 6000L);

        var multi = new MultiSegmentIndex(new[] { idx1, idx2, idx3 });

        // Query range does not overlap any segment
        var result = multi.LookupCandidates(sid, field, 7000L, 8000L);
        Assert.Empty(result);
    }

    [Fact]
    public void LookupCandidates_MultiField_ReturnsAllFieldsInTimeRange()
    {
        ulong sid = 0x1234UL;
        var idx1 = BuildSegment(1L, sid, "cpu", 1000L, 2000L);
        var idx2 = BuildSegment(2L, sid, "mem", 1000L, 2000L);

        var multi = new MultiSegmentIndex(new[] { idx1, idx2 });

        // Query by series only
        var result = multi.LookupCandidates(sid, 1000L, 2000L);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void MultiSegmentIndex_MinMax_AcrossSegments()
    {
        ulong sid = 0xFFFFUL;
        var idx1 = BuildSegment(1L, sid, "f", 100L, 200L);
        var idx2 = BuildSegment(2L, sid, "f", 300L, 400L);

        var multi = new MultiSegmentIndex(new[] { idx1, idx2 });

        Assert.Equal(100L, multi.MinTimestamp);
        Assert.Equal(400L, multi.MaxTimestamp);
    }
}
