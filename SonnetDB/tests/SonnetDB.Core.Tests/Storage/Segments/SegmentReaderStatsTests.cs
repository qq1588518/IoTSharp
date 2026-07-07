using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Format;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Segments;

/// <summary>
/// PR #31 — <see cref="SegmentReader.GetStats"/> 与 <see cref="SegmentStats"/> 的单元测试：
/// 覆盖空段、纯 V1、纯 V2 时间戳、纯 V2 值、双 V2、按 FieldType 分组、
/// 平均字节/点辅助属性、以及 V2 相比 V1 的字节缩减验证。
/// </summary>
public sealed class SegmentReaderStatsTests : IDisposable
{
    private readonly string _tempDir;

    public SegmentReaderStatsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sndb-stats-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Stats_DefaultV1_ReportsAllRawAndCorrectByteCounts()
    {
        var mt = BuildMemTable(seriesCount: 2, pointsPerSeries: 50, fieldType: FieldType.Float64);
        string path = Path.Combine(_tempDir, "v1.SDBSEG");
        new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false })
            .WriteFrom(mt, 1L, path);

        using var reader = SegmentReader.Open(path);
        var stats = reader.GetStats();

        Assert.Equal(2, stats.BlockCount);
        Assert.Equal(100, stats.TotalPointCount);

        // 全部 V1：raw=2, delta=0
        Assert.Equal(2, stats.RawTimestampBlocks);
        Assert.Equal(0, stats.DeltaTimestampBlocks);
        Assert.Equal(2, stats.RawValueBlocks);
        Assert.Equal(0, stats.DeltaValueBlocks);

        // V1 时间戳 = 8B/点 × 100 = 800
        Assert.Equal(800L, stats.TotalTimestampPayloadBytes);
        // V1 Float64 值 = 8B/点 × 100 = 800
        Assert.Equal(800L, stats.TotalValuePayloadBytes);
        Assert.Equal(8d, stats.AverageTimestampBytesPerPoint);
        Assert.Equal(8d, stats.AverageValueBytesPerPoint);

        // ByFieldType 仅含 Float64
        Assert.Single(stats.ByFieldType);
        var f64 = stats.ByFieldType[FieldType.Float64];
        Assert.Equal(2, f64.BlockCount);
        Assert.Equal(100, f64.PointCount);
        Assert.Equal(800L, f64.ValuePayloadBytes);
        Assert.Equal(0, f64.DeltaValueBlocks);
    }

    [Fact]
    public void Stats_DeltaTimestampOnly_CompressesTimestampBytes()
    {
        var mt = BuildMemTable(seriesCount: 1, pointsPerSeries: 200, fieldType: FieldType.Float64);

        string pathV1 = Path.Combine(_tempDir, "v1ts.SDBSEG");
        string pathV2 = Path.Combine(_tempDir, "v2ts.SDBSEG");
        new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false }).WriteFrom(mt, 1L, pathV1);
        new SegmentWriter(new SegmentWriterOptions
        {
            FsyncOnCommit = false,
            TimestampEncoding = BlockEncoding.DeltaTimestamp,
        }).WriteFrom(mt, 2L, pathV2);

        using var rV1 = SegmentReader.Open(pathV1);
        using var rV2 = SegmentReader.Open(pathV2);
        var s1 = rV1.GetStats();
        var s2 = rV2.GetStats();

        Assert.Equal(1, s2.DeltaTimestampBlocks);
        Assert.Equal(0, s2.RawTimestampBlocks);
        Assert.Equal(1, s2.RawValueBlocks);
        Assert.Equal(0, s2.DeltaValueBlocks);

        Assert.True(s2.TotalTimestampPayloadBytes < s1.TotalTimestampPayloadBytes,
            $"V2 ts({s2.TotalTimestampPayloadBytes}) 应小于 V1 ts({s1.TotalTimestampPayloadBytes})");
        // 值未启用 V2，应保持一致
        Assert.Equal(s1.TotalValuePayloadBytes, s2.TotalValuePayloadBytes);
    }

    [Fact]
    public void Stats_DeltaValueOnly_CompressesValueBytes()
    {
        // 全相同字符串：字典编码极高压缩
        var mt = BuildMemTable(seriesCount: 1, pointsPerSeries: 300, fieldType: FieldType.String);

        string pathV1 = Path.Combine(_tempDir, "v1val.SDBSEG");
        string pathV2 = Path.Combine(_tempDir, "v2val.SDBSEG");
        new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false }).WriteFrom(mt, 1L, pathV1);
        new SegmentWriter(new SegmentWriterOptions
        {
            FsyncOnCommit = false,
            ValueEncoding = BlockEncoding.DeltaValue,
        }).WriteFrom(mt, 2L, pathV2);

        using var rV1 = SegmentReader.Open(pathV1);
        using var rV2 = SegmentReader.Open(pathV2);
        var s1 = rV1.GetStats();
        var s2 = rV2.GetStats();

        Assert.Equal(1, s2.DeltaValueBlocks);
        Assert.Equal(0, s2.RawValueBlocks);
        Assert.Equal(1, s2.RawTimestampBlocks);
        Assert.Equal(0, s2.DeltaTimestampBlocks);

        Assert.True(s2.TotalValuePayloadBytes < s1.TotalValuePayloadBytes,
            $"V2 val({s2.TotalValuePayloadBytes}) 应小于 V1 val({s1.TotalValuePayloadBytes})");
        Assert.Equal(s1.TotalTimestampPayloadBytes, s2.TotalTimestampPayloadBytes);

        var byString = s2.ByFieldType[FieldType.String];
        Assert.Equal(1, byString.DeltaValueBlocks);
        Assert.Equal(s2.TotalValuePayloadBytes, byString.ValuePayloadBytes);
    }

    [Fact]
    public void Stats_BothEncodings_BothCountersIncremented()
    {
        var mt = BuildMemTable(seriesCount: 3, pointsPerSeries: 100, fieldType: FieldType.Float64);
        string path = Path.Combine(_tempDir, "vall.SDBSEG");
        new SegmentWriter(new SegmentWriterOptions
        {
            FsyncOnCommit = false,
            TimestampEncoding = BlockEncoding.DeltaTimestamp,
            ValueEncoding = BlockEncoding.DeltaValue,
        }).WriteFrom(mt, 1L, path);

        using var reader = SegmentReader.Open(path);
        var stats = reader.GetStats();

        Assert.Equal(3, stats.BlockCount);
        Assert.Equal(300, stats.TotalPointCount);
        Assert.Equal(3, stats.DeltaTimestampBlocks);
        Assert.Equal(3, stats.DeltaValueBlocks);
        Assert.Equal(0, stats.RawTimestampBlocks);
        Assert.Equal(0, stats.RawValueBlocks);
        Assert.True(stats.AverageTimestampBytesPerPoint < 8d);
        Assert.True(stats.AverageValueBytesPerPoint < 8d);
    }

    [Fact]
    public void Stats_MultipleFieldTypes_GroupedByFieldType()
    {
        // 同一段内三种 FieldType（Float64 / Boolean / String）各 1 个 series
        var mt = new MemTable();
        long lsn = 1L;
        for (int i = 0; i < 50; i++)
            mt.Append(1001UL, 10_000L + i, "v", FieldValue.FromDouble(i * 0.5), lsn++);
        for (int i = 0; i < 30; i++)
            mt.Append(1002UL, 10_000L + i, "v", FieldValue.FromBool(i % 2 == 0), lsn++);
        for (int i = 0; i < 20; i++)
            mt.Append(1003UL, 10_000L + i, "v", FieldValue.FromString($"label-{i % 3}"), lsn++);

        string path = Path.Combine(_tempDir, "mixed.SDBSEG");
        new SegmentWriter(new SegmentWriterOptions
        {
            FsyncOnCommit = false,
            ValueEncoding = BlockEncoding.DeltaValue,
        }).WriteFrom(mt, 1L, path);

        using var reader = SegmentReader.Open(path);
        var stats = reader.GetStats();

        Assert.Equal(3, stats.BlockCount);
        Assert.Equal(100, stats.TotalPointCount);
        Assert.Equal(3, stats.DeltaValueBlocks);

        Assert.Equal(3, stats.ByFieldType.Count);
        Assert.Equal(1, stats.ByFieldType[FieldType.Float64].BlockCount);
        Assert.Equal(50, stats.ByFieldType[FieldType.Float64].PointCount);
        Assert.Equal(1, stats.ByFieldType[FieldType.Boolean].BlockCount);
        Assert.Equal(30, stats.ByFieldType[FieldType.Boolean].PointCount);
        Assert.Equal(1, stats.ByFieldType[FieldType.String].BlockCount);
        Assert.Equal(20, stats.ByFieldType[FieldType.String].PointCount);

        // 每个 FieldType 都启用了 V2 值编码
        foreach (var (_, s) in stats.ByFieldType)
            Assert.Equal(1, s.DeltaValueBlocks);
    }

    [Fact]
    public void Stats_AverageProperties_HandleZeroPointsGracefully()
    {
        // 直接构造空 stats 验证除零防护
        var empty = new SegmentStats();
        Assert.Equal(0d, empty.AverageTimestampBytesPerPoint);
        Assert.Equal(0d, empty.AverageValueBytesPerPoint);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static MemTable BuildMemTable(int seriesCount, int pointsPerSeries, FieldType fieldType)
    {
        var mt = new MemTable();
        long lsn = 1L;
        for (int s = 0; s < seriesCount; s++)
        {
            ulong sid = (ulong)(7000 + s);
            for (int i = 0; i < pointsPerSeries; i++)
            {
                long ts = 10_000L + i * 1000L;
                FieldValue v = fieldType switch
                {
                    FieldType.Float64 => FieldValue.FromDouble(i * 1.5),
                    FieldType.Int64 => FieldValue.FromLong(i),
                    FieldType.Boolean => FieldValue.FromBool(i % 4 < 2),
                    FieldType.String => FieldValue.FromString("constant"),
                    _ => throw new ArgumentOutOfRangeException(nameof(fieldType)),
                };
                mt.Append(sid, ts, "v", v, lsn++);
            }
        }
        return mt;
    }
}
