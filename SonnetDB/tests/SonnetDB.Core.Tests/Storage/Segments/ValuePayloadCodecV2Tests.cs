using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Format;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Segments;

/// <summary>
/// PR #30 — 值列 V2 编码的单元测试：
/// 覆盖 Float64 Gorilla XOR、Boolean RLE、String 字典、Int64 直存四种类型；
/// 包括纯编解码 round-trip、损坏载荷异常、SegmentWriter→Reader 端到端与 V1 一致性、
/// 同时启用 Timestamp+Value V2 双编码、向后兼容默认 V1。
/// </summary>
public sealed class ValuePayloadCodecV2Tests : IDisposable
{
    private readonly string _tempDir;

    public ValuePayloadCodecV2Tests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sndb-valv2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── Float64 Gorilla XOR ─────────────────────────────────────────────────

    [Fact]
    public void Float64_Empty_ZeroBytes()
    {
        Assert.Equal(0, ValuePayloadCodecV2.Measure(FieldType.Float64, ReadOnlyMemory<DataPoint>.Empty));
        var values = ValuePayloadCodecV2.Decode(FieldType.Float64, ReadOnlySpan<byte>.Empty, 0);
        Assert.Empty(values);
    }

    [Fact]
    public void Float64_SinglePoint_8Bytes()
    {
        var pts = MakePoints(FieldValue.FromDouble(3.14159));
        int n = ValuePayloadCodecV2.Measure(FieldType.Float64, pts);
        Assert.Equal(8, n);

        var buf = new byte[n];
        ValuePayloadCodecV2.Write(FieldType.Float64, pts, buf);
        var values = ValuePayloadCodecV2.Decode(FieldType.Float64, buf, 1);
        Assert.Equal(3.14159, values[0].AsDouble());
    }

    [Fact]
    public void Float64_AllSame_CompressesHeavily()
    {
        // 1000 个完全相同的 double：8B 锚点 + 999 位 '0' = 8 + 125 = 133 字节
        const int count = 1000;
        var pts = new DataPoint[count];
        for (int i = 0; i < count; i++)
            pts[i] = new DataPoint(i, FieldValue.FromDouble(42.0));

        int n = ValuePayloadCodecV2.Measure(FieldType.Float64, pts);
        Assert.True(n < count * 8, $"V2({n}) 应小于 V1({count * 8})");

        var buf = new byte[n];
        ValuePayloadCodecV2.Write(FieldType.Float64, pts, buf);
        var values = ValuePayloadCodecV2.Decode(FieldType.Float64, buf, count);
        for (int i = 0; i < count; i++)
            Assert.Equal(42.0, values[i].AsDouble());
    }

    [Fact]
    public void Float64_AscendingValues_RoundTripExact()
    {
        const int count = 500;
        var pts = new DataPoint[count];
        for (int i = 0; i < count; i++)
            pts[i] = new DataPoint(i, FieldValue.FromDouble(i * 1.234567));

        int n = ValuePayloadCodecV2.Measure(FieldType.Float64, pts);
        var buf = new byte[n];
        ValuePayloadCodecV2.Write(FieldType.Float64, pts, buf);

        var values = ValuePayloadCodecV2.Decode(FieldType.Float64, buf, count);
        for (int i = 0; i < count; i++)
            Assert.Equal(i * 1.234567, values[i].AsDouble());
    }

    [Fact]
    public void Float64_SpecialValues_RoundTripExact()
    {
        var pts = MakePoints(
            FieldValue.FromDouble(0.0),
            FieldValue.FromDouble(-0.0),
            FieldValue.FromDouble(double.MinValue),
            FieldValue.FromDouble(double.MaxValue),
            FieldValue.FromDouble(double.Epsilon),
            FieldValue.FromDouble(double.NaN),
            FieldValue.FromDouble(double.PositiveInfinity),
            FieldValue.FromDouble(double.NegativeInfinity));

        int n = ValuePayloadCodecV2.Measure(FieldType.Float64, pts);
        var buf = new byte[n];
        ValuePayloadCodecV2.Write(FieldType.Float64, pts, buf);
        var values = ValuePayloadCodecV2.Decode(FieldType.Float64, buf, pts.Length);

        // 通过 bit pattern 比较以正确处理 NaN / -0.0
        for (int i = 0; i < pts.Length; i++)
        {
            ulong expected = BitConverter.DoubleToUInt64Bits(pts.Span[i].Value.AsDouble());
            ulong actual = BitConverter.DoubleToUInt64Bits(values[i].AsDouble());
            Assert.Equal(expected, actual);
        }
    }

    // ── Boolean RLE ─────────────────────────────────────────────────────────

    [Fact]
    public void Bool_AllTrue_TwoBytes()
    {
        // 100 true → 1B(初值) + 1B(varint 100)
        const int count = 100;
        var pts = new DataPoint[count];
        for (int i = 0; i < count; i++)
            pts[i] = new DataPoint(i, FieldValue.FromBool(true));

        int n = ValuePayloadCodecV2.Measure(FieldType.Boolean, pts);
        Assert.Equal(2, n);

        var buf = new byte[n];
        ValuePayloadCodecV2.Write(FieldType.Boolean, pts, buf);
        var values = ValuePayloadCodecV2.Decode(FieldType.Boolean, buf, count);
        for (int i = 0; i < count; i++)
            Assert.True(values[i].AsBool());
    }

    [Fact]
    public void Bool_Alternating_RoundTripCorrect()
    {
        const int count = 50;
        var pts = new DataPoint[count];
        for (int i = 0; i < count; i++)
            pts[i] = new DataPoint(i, FieldValue.FromBool(i % 2 == 0));

        int n = ValuePayloadCodecV2.Measure(FieldType.Boolean, pts);
        // 1 字节初值 + 50 个 varint=1（每个 1 字节）= 51 字节
        Assert.Equal(51, n);

        var buf = new byte[n];
        ValuePayloadCodecV2.Write(FieldType.Boolean, pts, buf);
        var values = ValuePayloadCodecV2.Decode(FieldType.Boolean, buf, count);
        for (int i = 0; i < count; i++)
            Assert.Equal(i % 2 == 0, values[i].AsBool());
    }

    [Fact]
    public void Bool_MixedRuns_RoundTripCorrect()
    {
        // [F×3, T×5, F×1, T×200]
        var bools = new List<bool>();
        bools.AddRange(Enumerable.Repeat(false, 3));
        bools.AddRange(Enumerable.Repeat(true, 5));
        bools.AddRange(Enumerable.Repeat(false, 1));
        bools.AddRange(Enumerable.Repeat(true, 200));

        var pts = bools.Select((b, i) => new DataPoint(i, FieldValue.FromBool(b))).ToArray();

        int n = ValuePayloadCodecV2.Measure(FieldType.Boolean, pts);
        Assert.True(n < bools.Count, $"RLE({n}) 应小于 V1({bools.Count})");

        var buf = new byte[n];
        ValuePayloadCodecV2.Write(FieldType.Boolean, pts, buf);
        var values = ValuePayloadCodecV2.Decode(FieldType.Boolean, buf, bools.Count);
        for (int i = 0; i < bools.Count; i++)
            Assert.Equal(bools[i], values[i].AsBool());
    }

    [Fact]
    public void Bool_CorruptRunOverflow_Throws()
    {
        // 初值 1，varint=200，但 count=10
        var buf = new byte[] { 1, (byte)200, 1 };
        Assert.Throws<InvalidDataException>(() => ValuePayloadCodecV2.Decode(FieldType.Boolean, buf, 10));
    }

    // ── String dictionary ───────────────────────────────────────────────────

    [Fact]
    public void String_AllSame_CompressesHeavily()
    {
        const int count = 100;
        var pts = new DataPoint[count];
        for (int i = 0; i < count; i++)
            pts[i] = new DataPoint(i, FieldValue.FromString("hello"));

        int n = ValuePayloadCodecV2.Measure(FieldType.String, pts);
        // 字典：1B(size=1) + 1B(len=5) + 5B + 100×1B(idx=0) = 107 字节
        Assert.Equal(107, n);

        var buf = new byte[n];
        ValuePayloadCodecV2.Write(FieldType.String, pts, buf);
        var values = ValuePayloadCodecV2.Decode(FieldType.String, buf, count);
        for (int i = 0; i < count; i++)
            Assert.Equal("hello", values[i].AsString());
    }

    [Fact]
    public void String_DistinctValues_RoundTripCorrect()
    {
        var strs = new[] { "alpha", "beta", "gamma", "alpha", "delta", "beta", "中文", "alpha" };
        var pts = strs.Select((s, i) => new DataPoint(i, FieldValue.FromString(s))).ToArray();

        int n = ValuePayloadCodecV2.Measure(FieldType.String, pts);
        var buf = new byte[n];
        ValuePayloadCodecV2.Write(FieldType.String, pts, buf);
        var values = ValuePayloadCodecV2.Decode(FieldType.String, buf, strs.Length);
        for (int i = 0; i < strs.Length; i++)
            Assert.Equal(strs[i], values[i].AsString());
    }

    [Fact]
    public void String_EmptyString_RoundTripCorrect()
    {
        var pts = MakePoints(FieldValue.FromString(""), FieldValue.FromString(""), FieldValue.FromString("x"));
        int n = ValuePayloadCodecV2.Measure(FieldType.String, pts);
        var buf = new byte[n];
        ValuePayloadCodecV2.Write(FieldType.String, pts, buf);
        var values = ValuePayloadCodecV2.Decode(FieldType.String, buf, 3);
        Assert.Equal("", values[0].AsString());
        Assert.Equal("", values[1].AsString());
        Assert.Equal("x", values[2].AsString());
    }

    [Fact]
    public void String_CorruptDictIndex_Throws()
    {
        // dictSize=1, "a"，但 idx=5
        var buf = new byte[] { 1, 1, (byte)'a', 5 };
        Assert.Throws<InvalidDataException>(() => ValuePayloadCodecV2.Decode(FieldType.String, buf, 1));
    }

    // ── Int64 (V2 = raw passthrough) ────────────────────────────────────────

    [Fact]
    public void Int64_V2EqualsV1Format()
    {
        var pts = MakePoints(
            FieldValue.FromLong(0L),
            FieldValue.FromLong(long.MaxValue),
            FieldValue.FromLong(long.MinValue),
            FieldValue.FromLong(-42L));

        int v2Size = ValuePayloadCodecV2.Measure(FieldType.Int64, pts);
        Assert.Equal(pts.Length * 8, v2Size);

        var buf = new byte[v2Size];
        ValuePayloadCodecV2.Write(FieldType.Int64, pts, buf);
        var values = ValuePayloadCodecV2.Decode(FieldType.Int64, buf, pts.Length);
        for (int i = 0; i < pts.Length; i++)
            Assert.Equal(pts.Span[i].Value.AsLong(), values[i].AsLong());
    }

    // ── SegmentWriter → SegmentReader 端到端 ────────────────────────────────

    [Fact]
    public void SegmentWriter_DefaultEncoding_NoFlagsSet()
    {
        var mt = BuildMemTable(seriesCount: 1, pointsPerSeries: 5, fieldType: FieldType.Float64);
        string path = Path.Combine(_tempDir, "default.SDBSEG");

        new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false })
            .WriteFrom(mt, 1L, path);

        using var reader = SegmentReader.Open(path);
        Assert.Equal(BlockEncoding.None, reader.Blocks[0].ValueEncoding);
        Assert.Equal(BlockEncoding.None, reader.Blocks[0].TimestampEncoding);
    }

    [Fact]
    public void SegmentWriter_DeltaValue_Float64_RoundTripMatchesV1()
    {
        var mt = BuildMemTable(seriesCount: 2, pointsPerSeries: 200, fieldType: FieldType.Float64);

        string pathV1 = Path.Combine(_tempDir, "v1f.SDBSEG");
        string pathV2 = Path.Combine(_tempDir, "v2f.SDBSEG");

        new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false }).WriteFrom(mt, 1L, pathV1);
        new SegmentWriter(new SegmentWriterOptions
        {
            FsyncOnCommit = false,
            ValueEncoding = BlockEncoding.DeltaValue,
        }).WriteFrom(mt, 2L, pathV2);

        Assert.True(new FileInfo(pathV2).Length < new FileInfo(pathV1).Length);

        AssertReadersMatch(pathV1, pathV2, expectedValueEncoding: BlockEncoding.DeltaValue);
    }

    [Fact]
    public void SegmentWriter_DeltaValue_Boolean_RoundTripMatchesV1()
    {
        var mt = BuildMemTable(seriesCount: 1, pointsPerSeries: 500, fieldType: FieldType.Boolean);

        string pathV1 = Path.Combine(_tempDir, "v1b.SDBSEG");
        string pathV2 = Path.Combine(_tempDir, "v2b.SDBSEG");

        new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false }).WriteFrom(mt, 1L, pathV1);
        new SegmentWriter(new SegmentWriterOptions
        {
            FsyncOnCommit = false,
            ValueEncoding = BlockEncoding.DeltaValue,
        }).WriteFrom(mt, 2L, pathV2);

        AssertReadersMatch(pathV1, pathV2, expectedValueEncoding: BlockEncoding.DeltaValue);
    }

    [Fact]
    public void SegmentWriter_DeltaValue_String_RoundTripMatchesV1()
    {
        var mt = BuildMemTable(seriesCount: 1, pointsPerSeries: 300, fieldType: FieldType.String);

        string pathV1 = Path.Combine(_tempDir, "v1s.SDBSEG");
        string pathV2 = Path.Combine(_tempDir, "v2s.SDBSEG");

        new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false }).WriteFrom(mt, 1L, pathV1);
        new SegmentWriter(new SegmentWriterOptions
        {
            FsyncOnCommit = false,
            ValueEncoding = BlockEncoding.DeltaValue,
        }).WriteFrom(mt, 2L, pathV2);

        // 300 个点，但仅 5 个不同字符串：V2 应显著小于 V1
        Assert.True(new FileInfo(pathV2).Length < new FileInfo(pathV1).Length);

        AssertReadersMatch(pathV1, pathV2, expectedValueEncoding: BlockEncoding.DeltaValue);
    }

    [Fact]
    public void SegmentWriter_BothEncodings_RoundTripMatchesV1AndDecodeRange()
    {
        var mt = BuildMemTable(seriesCount: 2, pointsPerSeries: 100, fieldType: FieldType.Float64);

        string pathV1 = Path.Combine(_tempDir, "v1all.SDBSEG");
        string pathV2 = Path.Combine(_tempDir, "v2all.SDBSEG");

        new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false }).WriteFrom(mt, 1L, pathV1);
        new SegmentWriter(new SegmentWriterOptions
        {
            FsyncOnCommit = false,
            TimestampEncoding = BlockEncoding.DeltaTimestamp,
            ValueEncoding = BlockEncoding.DeltaValue,
        }).WriteFrom(mt, 2L, pathV2);

        using var rV1 = SegmentReader.Open(pathV1);
        using var rV2 = SegmentReader.Open(pathV2);

        Assert.Equal(rV1.Blocks.Count, rV2.Blocks.Count);
        for (int i = 0; i < rV1.Blocks.Count; i++)
        {
            Assert.Equal(BlockEncoding.DeltaTimestamp, rV2.Blocks[i].TimestampEncoding);
            Assert.Equal(BlockEncoding.DeltaValue, rV2.Blocks[i].ValueEncoding);

            var p1 = rV1.DecodeBlock(rV1.Blocks[i]);
            var p2 = rV2.DecodeBlock(rV2.Blocks[i]);
            Assert.Equal(p1.Length, p2.Length);
            for (int j = 0; j < p1.Length; j++)
            {
                Assert.Equal(p1[j].Timestamp, p2[j].Timestamp);
                Assert.Equal(p1[j].Value.AsDouble(), p2[j].Value.AsDouble());
            }

            // 范围解码也一致
            long mid = (rV1.Blocks[i].MinTimestamp + rV1.Blocks[i].MaxTimestamp) / 2;
            var r1 = rV1.DecodeBlockRange(rV1.Blocks[i], rV1.Blocks[i].MinTimestamp, mid);
            var r2 = rV2.DecodeBlockRange(rV2.Blocks[i], rV2.Blocks[i].MinTimestamp, mid);
            Assert.Equal(r1.Length, r2.Length);
            for (int j = 0; j < r1.Length; j++)
            {
                Assert.Equal(r1[j].Timestamp, r2[j].Timestamp);
                Assert.Equal(r1[j].Value.AsDouble(), r2[j].Value.AsDouble());
            }
        }
    }

    [Theory]
    [InlineData(FieldType.Float64)]
    [InlineData(FieldType.Int64)]
    [InlineData(FieldType.Boolean)]
    [InlineData(FieldType.String)]
    public void SegmentWriter_BothEncodings_DecodeRangeMatchesFullSubset(FieldType fieldType)
    {
        var mt = BuildMemTable(seriesCount: 1, pointsPerSeries: 80, fieldType);
        string path = Path.Combine(_tempDir, $"v2_range_{fieldType}.SDBSEG");

        new SegmentWriter(new SegmentWriterOptions
        {
            FsyncOnCommit = false,
            TimestampEncoding = BlockEncoding.DeltaTimestamp,
            ValueEncoding = BlockEncoding.DeltaValue,
        }).WriteFrom(mt, 10L, path);

        using var reader = SegmentReader.Open(path);
        var block = reader.Blocks[0];
        var all = reader.DecodeBlock(block);

        const int startIndex = 11;
        const int endIndex = 37;
        var range = reader.DecodeBlockRange(block, all[startIndex].Timestamp, all[endIndex].Timestamp);

        Assert.Equal(endIndex - startIndex + 1, range.Length);
        for (int i = 0; i < range.Length; i++)
        {
            var expected = all[startIndex + i];
            Assert.Equal(expected.Timestamp, range[i].Timestamp);
            Assert.Equal(expected.Value, range[i].Value);
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static ReadOnlyMemory<DataPoint> MakePoints(params FieldValue[] values)
    {
        var arr = new DataPoint[values.Length];
        for (int i = 0; i < values.Length; i++)
            arr[i] = new DataPoint(i, values[i]);
        return arr;
    }

    private static MemTable BuildMemTable(int seriesCount, int pointsPerSeries, FieldType fieldType)
    {
        var mt = new MemTable();
        long lsn = 1L;
        for (int s = 0; s < seriesCount; s++)
        {
            ulong sid = (ulong)(3000 + s);
            for (int i = 0; i < pointsPerSeries; i++)
            {
                long ts = 10_000L + i * 100L;
                FieldValue v = fieldType switch
                {
                    FieldType.Float64 => FieldValue.FromDouble(i * 1.5),
                    FieldType.Int64 => FieldValue.FromLong(i),
                    FieldType.Boolean => FieldValue.FromBool(i % 4 < 2),
                    FieldType.String => FieldValue.FromString($"label-{i % 5}"),
                    _ => throw new ArgumentOutOfRangeException(nameof(fieldType)),
                };
                mt.Append(sid, ts, "v", v, lsn++);
            }
        }
        return mt;
    }

    private static void AssertReadersMatch(string pathV1, string pathV2, BlockEncoding expectedValueEncoding)
    {
        using var rV1 = SegmentReader.Open(pathV1);
        using var rV2 = SegmentReader.Open(pathV2);

        Assert.Equal(rV1.Blocks.Count, rV2.Blocks.Count);
        for (int i = 0; i < rV1.Blocks.Count; i++)
        {
            Assert.Equal(BlockEncoding.None, rV1.Blocks[i].ValueEncoding);
            Assert.Equal(expectedValueEncoding, rV2.Blocks[i].ValueEncoding);

            var p1 = rV1.DecodeBlock(rV1.Blocks[i]);
            var p2 = rV2.DecodeBlock(rV2.Blocks[i]);
            Assert.Equal(p1.Length, p2.Length);
            for (int j = 0; j < p1.Length; j++)
            {
                Assert.Equal(p1[j].Timestamp, p2[j].Timestamp);
                Assert.Equal(p1[j].Value, p2[j].Value);
            }
        }
    }
}
