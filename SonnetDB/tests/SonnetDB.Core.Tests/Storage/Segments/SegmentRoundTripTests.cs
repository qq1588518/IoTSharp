using System.Diagnostics;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Format;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Segments;

/// <summary>
/// SegmentWriter → SegmentReader 完整 round-trip 端到端测试。
/// 验证从 MemTable 写出的数据经 SegmentReader 解码后与原始数据完全一致。
/// </summary>
public sealed class SegmentRoundTripTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SegmentWriter _writer = new(new SegmentWriterOptions { FsyncOnCommit = false });

    public SegmentRoundTripTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string TempPath(string name = "roundtrip.SDBSEG") =>
        Path.Combine(_tempDir, name);

    /// <summary>
    /// 完整链路：5 个 series × 4 种 FieldType，5000 点，写出后逐 Block 解码与原始数据完全一致。
    /// </summary>
    [Fact]
    public void FullRoundTrip_5Series4Types_AllPointsMatch()
    {
        string path = TempPath();

        // 构造测试数据
        const int pointsPerSeries = 250; // 5 series × 4 types × 250 = 5000 points total
        var originalData = new Dictionary<(ulong SeriesId, string FieldName), List<DataPoint>>();

        var mt = new MemTable();
        long lsn = 1L;

        // Series 1: Float64
        ulong sid1 = 1001UL;
        var s1Data = new List<DataPoint>(pointsPerSeries);
        for (int i = 0; i < pointsPerSeries; i++)
        {
            long ts = 1000L + i * 10L;
            double val = i * 3.14159;
            mt.Append(sid1, ts, "f64", FieldValue.FromDouble(val), lsn++);
            s1Data.Add(new DataPoint(ts, FieldValue.FromDouble(val)));
        }
        originalData[(sid1, "f64")] = s1Data;

        // Series 2: Int64
        ulong sid2 = 2002UL;
        var s2Data = new List<DataPoint>(pointsPerSeries);
        for (int i = 0; i < pointsPerSeries; i++)
        {
            long ts = 2000L + i * 10L;
            long val = i * 1000L - 100_000L;
            mt.Append(sid2, ts, "i64", FieldValue.FromLong(val), lsn++);
            s2Data.Add(new DataPoint(ts, FieldValue.FromLong(val)));
        }
        originalData[(sid2, "i64")] = s2Data;

        // Series 3: Boolean
        ulong sid3 = 3003UL;
        var s3Data = new List<DataPoint>(pointsPerSeries);
        for (int i = 0; i < pointsPerSeries; i++)
        {
            long ts = 3000L + i * 10L;
            bool val = i % 3 == 0;
            mt.Append(sid3, ts, "bool", FieldValue.FromBool(val), lsn++);
            s3Data.Add(new DataPoint(ts, FieldValue.FromBool(val)));
        }
        originalData[(sid3, "bool")] = s3Data;

        // Series 4: String（含中文）
        ulong sid4 = 4004UL;
        var s4Data = new List<DataPoint>(pointsPerSeries);
        for (int i = 0; i < pointsPerSeries; i++)
        {
            long ts = 4000L + i * 10L;
            string val = i % 5 == 0 ? $"状态{i}" : $"state_{i}";
            mt.Append(sid4, ts, "status", FieldValue.FromString(val), lsn++);
            s4Data.Add(new DataPoint(ts, FieldValue.FromString(val)));
        }
        originalData[(sid4, "status")] = s4Data;

        // Series 5: 两个字段（Float64 + Int64）
        ulong sid5 = 5005UL;
        var s5f1 = new List<DataPoint>(pointsPerSeries);
        var s5f2 = new List<DataPoint>(pointsPerSeries);
        for (int i = 0; i < pointsPerSeries; i++)
        {
            long ts = 5000L + i * 10L;
            mt.Append(sid5, ts, "rx", FieldValue.FromDouble(i * 100.0), lsn++);
            mt.Append(sid5, ts, "tx", FieldValue.FromLong(i * 50L), lsn++);
            s5f1.Add(new DataPoint(ts, FieldValue.FromDouble(i * 100.0)));
            s5f2.Add(new DataPoint(ts, FieldValue.FromLong(i * 50L)));
        }
        originalData[(sid5, "rx")] = s5f1;
        originalData[(sid5, "tx")] = s5f2;

        // 写出段文件
        var result = _writer.WriteFrom(mt, 1L, path);
        Assert.Equal(6, result.BlockCount); // 4 fields + 2 fields from sid5

        // 打开 SegmentReader 并逐 Block 解码
        using var reader = SegmentReader.Open(path);

        Assert.Equal(6, reader.BlockCount);

        foreach (var block in reader.Blocks)
        {
            var key = (block.SeriesId, block.FieldName);
            Assert.True(originalData.ContainsKey(key),
                $"Unexpected block SeriesId={block.SeriesId} FieldName={block.FieldName}");

            var expected = originalData[key];
            var decoded = reader.DecodeBlock(block);

            Assert.Equal(expected.Count, decoded.Length);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Timestamp, decoded[i].Timestamp);
                Assert.Equal(expected[i].Value, decoded[i].Value);
            }
        }
    }

    /// <summary>
    /// 性能 sanity：5000 点段 Open + 解析全部 Block 在合理时间内完成。
    /// 此测试仅作合理性检查（非强 perf gate）。
    /// </summary>
    [Fact]
    public void Performance_5000Points_OpenAndDecodeWithin200ms()
    {
        string path = TempPath("perf.SDBSEG");
        var mt = new MemTable();
        long lsn = 1L;
        for (int i = 0; i < 5000; i++)
            mt.Append((ulong)(i % 5 + 1), i * 10L, "v", FieldValue.FromDouble(i), lsn++);

        _writer.WriteFrom(mt, 1L, path);

        var sw = Stopwatch.StartNew();
        using var reader = SegmentReader.Open(path);
        int totalPoints = 0;
        foreach (var block in reader.Blocks)
        {
            var pts = reader.DecodeBlock(block);
            totalPoints += pts.Length;
        }
        sw.Stop();

        Assert.Equal(5000, totalPoints);
        // 合理性检查：< 200ms（非 perf gate，避免 CI 偶发超时）
        Assert.True(sw.ElapsedMilliseconds < 200,
            $"Open + DecodeAll took {sw.ElapsedMilliseconds}ms, expected < 200ms");
    }

    /// <summary>
    /// 空 MemTable 的 round-trip：写出 128B 文件，Reader 正常 Open，BlockCount=0。
    /// </summary>
    [Fact]
    public void EmptyMemTable_RoundTrip_BlockCountZero()
    {
        string path = TempPath("empty.SDBSEG");
        _writer.WriteFrom(new MemTable(), 1L, path);

        using var reader = SegmentReader.Open(path);

        Assert.Equal(0, reader.BlockCount);
        Assert.Empty(reader.Blocks);
        Assert.Equal(128L, reader.FileLength);
    }

    /// <summary>
    /// 单点 round-trip：单个数据点，所有字段一致。
    /// </summary>
    [Theory]
    [InlineData(FieldType.Float64)]
    [InlineData(FieldType.Int64)]
    [InlineData(FieldType.Boolean)]
    [InlineData(FieldType.String)]
    [InlineData(FieldType.GeoPoint)]
    public void SinglePoint_AllFieldTypes_RoundTrip(FieldType fieldType)
    {
        string path = TempPath($"single_{fieldType}.SDBSEG");
        var mt = new MemTable();
        FieldValue value = fieldType switch
        {
            FieldType.Float64 => FieldValue.FromDouble(42.5),
            FieldType.Int64 => FieldValue.FromLong(42L),
            FieldType.Boolean => FieldValue.FromBool(true),
            FieldType.String => FieldValue.FromString("hello"),
            FieldType.GeoPoint => FieldValue.FromGeoPoint(39.9042, 116.4074),
            _ => throw new InvalidOperationException()
        };
        mt.Append(1UL, 1000L, "field", value, 1L);
        _writer.WriteFrom(mt, 1L, path);

        using var reader = SegmentReader.Open(path);

        Assert.Equal(1, reader.BlockCount);
        var pts = reader.DecodeBlock(reader.Blocks[0]);
        Assert.Single(pts);
        Assert.Equal(1000L, pts[0].Timestamp);
        Assert.Equal(value, pts[0].Value);
    }

    /// <summary>
    /// 多 Block DecodeRange 的时间裁剪 round-trip：从段文件中按时间范围查找并解码。
    /// </summary>
    [Fact]
    public void DecodeBlockRange_MultipleBlocks_CorrectSubsets()
    {
        string path = TempPath("range.SDBSEG");
        var mt = new MemTable();

        // 写两个序列，时间范围不重叠
        for (int i = 0; i < 100; i++)
            mt.Append(1UL, i * 10L, "v", FieldValue.FromDouble(i), i + 1L); // 0..990
        for (int i = 0; i < 100; i++)
            mt.Append(2UL, 2000L + i * 10L, "v", FieldValue.FromDouble(i), 200L + i); // 2000..2990

        _writer.WriteFrom(mt, 1L, path);

        using var reader = SegmentReader.Open(path);

        // 用 FindByTimeRange 找出与 [200, 400] 重叠的 Block（只有 series 1）
        var blocks = reader.FindByTimeRange(200L, 400L);
        Assert.Single(blocks);
        Assert.Equal(1UL, blocks[0].SeriesId);

        var pts = reader.DecodeBlockRange(blocks[0], 200L, 400L);
        Assert.Equal(21, pts.Length); // timestamps 200, 210, ..., 400 = 21 points
        Assert.Equal(200L, pts[0].Timestamp);
        Assert.Equal(400L, pts[^1].Timestamp);
    }
}
