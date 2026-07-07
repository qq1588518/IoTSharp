using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Format;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Segments;

/// <summary>
/// <see cref="BlockDecoder"/> 单元测试：验证 4 种 FieldType 的 round-trip 解码与范围裁剪。
/// </summary>
public sealed class BlockDecoderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SegmentWriter _writer = new(new SegmentWriterOptions { FsyncOnCommit = false });

    public BlockDecoderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string TempPath(string name = "decoder_test.SDBSEG") =>
        Path.Combine(_tempDir, name);

    // ── Float64 round-trip ────────────────────────────────────────────────────

    [Fact]
    public void Float64_RoundTrip_AllPointsCorrect()
    {
        const int count = 100;
        var (reader, descriptor) = BuildSingleBlockReader(
            1UL, "f64", count,
            i => FieldValue.FromDouble(i * 1.23456789));

        using (reader)
        {
            var points = reader.DecodeBlock(descriptor);
            Assert.Equal(count, points.Length);
            for (int i = 0; i < count; i++)
                Assert.Equal(i * 1.23456789, points[i].Value.AsDouble(), precision: 10);
        }
    }

    [Fact]
    public void Float64_SpecialValues_RoundTrip()
    {
        string path = TempPath("float64_special.SDBSEG");
        var mt = new MemTable();
        mt.Append(1UL, 1L, "v", FieldValue.FromDouble(double.NaN), 1L);
        mt.Append(1UL, 2L, "v", FieldValue.FromDouble(double.PositiveInfinity), 2L);
        mt.Append(1UL, 3L, "v", FieldValue.FromDouble(double.NegativeInfinity), 3L);
        mt.Append(1UL, 4L, "v", FieldValue.FromDouble(double.MinValue), 4L);
        mt.Append(1UL, 5L, "v", FieldValue.FromDouble(double.MaxValue), 5L);
        _writer.WriteFrom(mt, 1L, path);

        using var reader = SegmentReader.Open(path);
        var pts = reader.DecodeBlock(reader.Blocks[0]);

        Assert.Equal(5, pts.Length);
        Assert.True(double.IsNaN(pts[0].Value.AsDouble()));
        Assert.Equal(double.PositiveInfinity, pts[1].Value.AsDouble());
        Assert.Equal(double.NegativeInfinity, pts[2].Value.AsDouble());
        Assert.Equal(double.MinValue, pts[3].Value.AsDouble());
        Assert.Equal(double.MaxValue, pts[4].Value.AsDouble());
    }

    // ── Int64 round-trip ──────────────────────────────────────────────────────

    [Fact]
    public void Int64_RoundTrip_AllPointsCorrect()
    {
        const int count = 100;
        var (reader, descriptor) = BuildSingleBlockReader(
            2UL, "i64", count,
            i => FieldValue.FromLong(i * 1000L - 50000L));

        using (reader)
        {
            var points = reader.DecodeBlock(descriptor);
            Assert.Equal(count, points.Length);
            for (int i = 0; i < count; i++)
                Assert.Equal(i * 1000L - 50000L, points[i].Value.AsLong());
        }
    }

    [Fact]
    public void Int64_BoundaryValues_RoundTrip()
    {
        string path = TempPath("int64_boundary.SDBSEG");
        var mt = new MemTable();
        mt.Append(1UL, 1L, "v", FieldValue.FromLong(long.MinValue), 1L);
        mt.Append(1UL, 2L, "v", FieldValue.FromLong(0L), 2L);
        mt.Append(1UL, 3L, "v", FieldValue.FromLong(long.MaxValue), 3L);
        _writer.WriteFrom(mt, 1L, path);

        using var reader = SegmentReader.Open(path);
        var pts = reader.DecodeBlock(reader.Blocks[0]);

        Assert.Equal(long.MinValue, pts[0].Value.AsLong());
        Assert.Equal(0L, pts[1].Value.AsLong());
        Assert.Equal(long.MaxValue, pts[2].Value.AsLong());
    }

    // ── Boolean round-trip ────────────────────────────────────────────────────

    [Fact]
    public void Boolean_RoundTrip_AllPointsCorrect()
    {
        const int count = 50;
        var (reader, descriptor) = BuildSingleBlockReader(
            3UL, "bool", count,
            i => FieldValue.FromBool(i % 2 == 0));

        using (reader)
        {
            var points = reader.DecodeBlock(descriptor);
            Assert.Equal(count, points.Length);
            for (int i = 0; i < count; i++)
                Assert.Equal(i % 2 == 0, points[i].Value.AsBool());
        }
    }

    // ── String round-trip ────────────────────────────────────────────────────

    [Fact]
    public void String_RoundTrip_AllPointsCorrect()
    {
        const int count = 20;
        var (reader, descriptor) = BuildSingleBlockReader(
            4UL, "s", count,
            i => FieldValue.FromString($"value_{i}_测试"));

        using (reader)
        {
            var points = reader.DecodeBlock(descriptor);
            Assert.Equal(count, points.Length);
            for (int i = 0; i < count; i++)
                Assert.Equal($"value_{i}_测试", points[i].Value.AsString());
        }
    }

    [Fact]
    public void String_EmptyString_RoundTrip()
    {
        string path = TempPath("empty_string.SDBSEG");
        var mt = new MemTable();
        mt.Append(1UL, 1L, "v", FieldValue.FromString(""), 1L);
        mt.Append(1UL, 2L, "v", FieldValue.FromString("hello"), 2L);
        _writer.WriteFrom(mt, 1L, path);

        using var reader = SegmentReader.Open(path);
        var pts = reader.DecodeBlock(reader.Blocks[0]);

        Assert.Equal("", pts[0].Value.AsString());
        Assert.Equal("hello", pts[1].Value.AsString());
    }

    // ── DecodeRange 边界 ──────────────────────────────────────────────────────

    [Fact]
    public void DecodeRange_FromGreaterThanMax_ReturnsEmpty()
    {
        const int count = 10;
        var (reader, descriptor) = BuildSingleBlockReader(
            1UL, "v", count,
            i => FieldValue.FromDouble(i));

        using (reader)
        {
            // Block MaxTimestamp = (count - 1) * 10 = 90
            var points = reader.DecodeBlockRange(descriptor, 1000L, 2000L);
            Assert.Empty(points);
        }
    }

    [Fact]
    public void DecodeRange_ToLessThanMin_ReturnsEmpty()
    {
        const int count = 10;
        var (reader, descriptor) = BuildSingleBlockReader(
            1UL, "v", count,
            i => FieldValue.FromDouble(i));

        using (reader)
        {
            // Block MinTimestamp = 0
            var points = reader.DecodeBlockRange(descriptor, -100L, -1L);
            Assert.Empty(points);
        }
    }

    [Fact]
    public void DecodeRange_FromAndToCoversAll_ReturnsAllPoints()
    {
        const int count = 10;
        var (reader, descriptor) = BuildSingleBlockReader(
            1UL, "v", count,
            i => FieldValue.FromDouble(i));

        using (reader)
        {
            long minTs = 0L;
            long maxTs = (count - 1) * 10L;
            var points = reader.DecodeBlockRange(descriptor, minTs, maxTs);
            Assert.Equal(count, points.Length);
        }
    }

    [Fact]
    public void DecodeRange_SinglePointRange_ReturnsOnePoint()
    {
        const int count = 10;
        var (reader, descriptor) = BuildSingleBlockReader(
            1UL, "v", count,
            i => FieldValue.FromDouble(i * 2.0));

        using (reader)
        {
            // Timestamps are 0, 10, 20, ..., 90 - get only ts=30
            var points = reader.DecodeBlockRange(descriptor, 30L, 30L);
            Assert.Single(points);
            Assert.Equal(30L, points[0].Timestamp);
            Assert.Equal(6.0, points[0].Value.AsDouble(), precision: 10); // index 3, value 3*2.0=6.0
        }
    }

    [Fact]
    public void DecodeRange_String_CorrectSubset()
    {
        string path = TempPath("str_range.SDBSEG");
        var mt = new MemTable();
        for (int i = 0; i < 10; i++)
            mt.Append(1UL, i * 10L, "s", FieldValue.FromString($"s{i}"), i + 1L);
        _writer.WriteFrom(mt, 1L, path);

        using var reader = SegmentReader.Open(path);
        // ts [20, 40] → indices 2, 3, 4
        var pts = reader.DecodeBlockRange(reader.Blocks[0], 20L, 40L);

        Assert.Equal(3, pts.Length);
        Assert.Equal("s2", pts[0].Value.AsString());
        Assert.Equal("s3", pts[1].Value.AsString());
        Assert.Equal("s4", pts[2].Value.AsString());
    }

    // ── 辅助方法 ──────────────────────────────────────────────────────────────

    /// <summary>写入一个单块段并打开 Reader，返回 reader 与 descriptor。调用方负责释放。</summary>
    private (SegmentReader reader, BlockDescriptor descriptor) BuildSingleBlockReader(
        ulong seriesId,
        string fieldName,
        int count,
        Func<int, FieldValue> valueFactory)
    {
        string path = TempPath($"single_{seriesId}_{fieldName}.SDBSEG");
        var mt = new MemTable();
        for (int i = 0; i < count; i++)
            mt.Append(seriesId, i * 10L, fieldName, valueFactory(i), i + 1L);
        _writer.WriteFrom(mt, 1L, path);

        var reader = SegmentReader.Open(path);
        return (reader, reader.Blocks[0]);
    }
}
