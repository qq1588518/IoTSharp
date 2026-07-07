using System.Buffers.Binary;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Format;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Segments;

/// <summary>
/// PR #29 — 时间戳 Delta-of-Delta 编码（V2）的单元测试。
/// 覆盖：编解码 round-trip、向后兼容（V1 默认）、负数差分、压缩比、SegmentWriter→Reader 端到端、
/// 与 V1 解码结果一致性、损坏载荷异常。
/// </summary>
public sealed class TimestampCodecTests : IDisposable
{
    private readonly string _tempDir;

    public TimestampCodecTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sndb-tscodec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── 纯编解码 round-trip ─────────────────────────────────────────────────

    [Fact]
    public void Empty_ReturnsZeroBytes()
    {
        Assert.Equal(0, TimestampCodec.MeasureDeltaOfDelta(ReadOnlySpan<long>.Empty));
        TimestampCodec.WriteDeltaOfDelta(ReadOnlySpan<long>.Empty, Span<byte>.Empty);
        TimestampCodec.ReadDeltaOfDelta(ReadOnlySpan<byte>.Empty, Span<long>.Empty);
    }

    [Fact]
    public void SinglePoint_OnlyAnchor8Bytes()
    {
        long[] src = [123_456_789L];
        int n = TimestampCodec.MeasureDeltaOfDelta(src);
        Assert.Equal(8, n);

        var buf = new byte[n];
        TimestampCodec.WriteDeltaOfDelta(src, buf);

        var dst = new long[1];
        TimestampCodec.ReadDeltaOfDelta(buf, dst);
        Assert.Equal(123_456_789L, dst[0]);
    }

    [Fact]
    public void RegularInterval_CompressesToOneBytePerPoint()
    {
        const int count = 1000;
        var src = new long[count];
        for (int i = 0; i < count; i++) src[i] = 1000L + i * 10L;

        int n = TimestampCodec.MeasureDeltaOfDelta(src);
        // 8B 锚点 + 1B first-delta(zigzag(10)=20→1B) + (count-2)B dod=0
        Assert.Equal(8 + 1 + (count - 2), n);
        Assert.True(n < count * 8, "V2 应当显著小于 V1");

        var buf = new byte[n];
        TimestampCodec.WriteDeltaOfDelta(src, buf);

        var dst = new long[count];
        TimestampCodec.ReadDeltaOfDelta(buf, dst);
        Assert.Equal(src, dst);
    }

    [Fact]
    public void IrregularInterval_RoundTripCorrect()
    {
        long[] src = [100L, 105L, 113L, 120L, 121L, 200L, 1_000_000L, 1_000_005L];
        var buf = new byte[TimestampCodec.MeasureDeltaOfDelta(src)];
        TimestampCodec.WriteDeltaOfDelta(src, buf);
        var dst = new long[src.Length];
        TimestampCodec.ReadDeltaOfDelta(buf, dst);
        Assert.Equal(src, dst);
    }

    [Fact]
    public void NegativeDeltaOfDelta_RoundTripCorrect()
    {
        // 时间间隔 100 → 50 → 10 → 200，二阶差分含负数
        long[] src = [0L, 100L, 150L, 160L, 360L];
        var buf = new byte[TimestampCodec.MeasureDeltaOfDelta(src)];
        TimestampCodec.WriteDeltaOfDelta(src, buf);
        var dst = new long[src.Length];
        TimestampCodec.ReadDeltaOfDelta(buf, dst);
        Assert.Equal(src, dst);
    }

    [Fact]
    public void LargeAnchorTimestamp_RoundTripCorrect()
    {
        long anchor = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var src = new long[100];
        for (int i = 0; i < 100; i++) src[i] = anchor + i * 1000L;

        var buf = new byte[TimestampCodec.MeasureDeltaOfDelta(src)];
        TimestampCodec.WriteDeltaOfDelta(src, buf);

        var dst = new long[src.Length];
        TimestampCodec.ReadDeltaOfDelta(buf, dst);
        Assert.Equal(src, dst);
    }

    [Fact]
    public void Write_DestinationLengthMismatch_Throws()
    {
        long[] src = [1L, 2L, 3L];
        var tooSmall = new byte[1];
        Assert.Throws<ArgumentException>(() => TimestampCodec.WriteDeltaOfDelta(src, tooSmall));
    }

    [Fact]
    public void Read_TruncatedAnchor_Throws()
    {
        var buf = new byte[4];
        var dst = new long[1];
        Assert.Throws<InvalidDataException>(() => TimestampCodec.ReadDeltaOfDelta(buf, dst));
    }

    [Fact]
    public void Read_TruncatedVarint_Throws()
    {
        var buf = new byte[8 + 11];
        BinaryPrimitives.WriteInt64LittleEndian(buf, 0L);
        for (int i = 8; i < buf.Length; i++) buf[i] = 0x80;
        var dst = new long[2];
        Assert.Throws<InvalidDataException>(() => TimestampCodec.ReadDeltaOfDelta(buf, dst));
    }

    // ── SegmentWriter → SegmentReader 端到端 ────────────────────────────────

    [Fact]
    public void SegmentWriter_DefaultEncoding_StaysV1()
    {
        var mt = BuildMemTable(seriesCount: 1, pointsPerSeries: 50);
        string path = Path.Combine(_tempDir, "default.SDBSEG");

        new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false })
            .WriteFrom(mt, 1L, path);

        using var reader = SegmentReader.Open(path);
        Assert.Equal(BlockEncoding.None, reader.Blocks[0].TimestampEncoding);
        Assert.Equal(BlockEncoding.None, reader.Blocks[0].ValueEncoding);
    }

    [Fact]
    public void SegmentWriter_DeltaTimestamp_RoundTripMatchesV1()
    {
        var mt = BuildMemTable(seriesCount: 3, pointsPerSeries: 200);

        string pathV1 = Path.Combine(_tempDir, "v1.SDBSEG");
        string pathV2 = Path.Combine(_tempDir, "v2.SDBSEG");

        new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false })
            .WriteFrom(mt, 1L, pathV1);
        new SegmentWriter(new SegmentWriterOptions
        {
            FsyncOnCommit = false,
            TimestampEncoding = BlockEncoding.DeltaTimestamp,
        }).WriteFrom(mt, 2L, pathV2);

        long sizeV1 = new FileInfo(pathV1).Length;
        long sizeV2 = new FileInfo(pathV2).Length;
        Assert.True(sizeV2 < sizeV1, $"期待 V2({sizeV2}) < V1({sizeV1})");

        using var readerV1 = SegmentReader.Open(pathV1);
        using var readerV2 = SegmentReader.Open(pathV2);

        Assert.Equal(readerV1.Blocks.Count, readerV2.Blocks.Count);

        for (int i = 0; i < readerV1.Blocks.Count; i++)
        {
            Assert.Equal(BlockEncoding.None, readerV1.Blocks[i].TimestampEncoding);
            Assert.Equal(BlockEncoding.DeltaTimestamp, readerV2.Blocks[i].TimestampEncoding);

            var pointsV1 = readerV1.DecodeBlock(readerV1.Blocks[i]);
            var pointsV2 = readerV2.DecodeBlock(readerV2.Blocks[i]);
            Assert.Equal(pointsV1.Length, pointsV2.Length);
            for (int j = 0; j < pointsV1.Length; j++)
            {
                Assert.Equal(pointsV1[j].Timestamp, pointsV2[j].Timestamp);
                Assert.Equal(pointsV1[j].Value, pointsV2[j].Value);
            }
        }
    }

    [Fact]
    public void SegmentWriter_DeltaTimestamp_DecodeRangeMatchesV1()
    {
        var mt = BuildMemTable(seriesCount: 2, pointsPerSeries: 100);

        string pathV1 = Path.Combine(_tempDir, "v1r.SDBSEG");
        string pathV2 = Path.Combine(_tempDir, "v2r.SDBSEG");

        new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false })
            .WriteFrom(mt, 1L, pathV1);
        new SegmentWriter(new SegmentWriterOptions
        {
            FsyncOnCommit = false,
            TimestampEncoding = BlockEncoding.DeltaTimestamp,
        }).WriteFrom(mt, 2L, pathV2);

        using var rV1 = SegmentReader.Open(pathV1);
        using var rV2 = SegmentReader.Open(pathV2);

        var b1 = rV1.Blocks[0];
        var b2 = rV2.Blocks[0];
        long mid = (b1.MinTimestamp + b1.MaxTimestamp) / 2;

        var rangeV1 = rV1.DecodeBlockRange(b1, b1.MinTimestamp, mid);
        var rangeV2 = rV2.DecodeBlockRange(b2, b2.MinTimestamp, mid);

        Assert.Equal(rangeV1.Length, rangeV2.Length);
        for (int i = 0; i < rangeV1.Length; i++)
        {
            Assert.Equal(rangeV1[i].Timestamp, rangeV2[i].Timestamp);
            Assert.Equal(rangeV1[i].Value, rangeV2[i].Value);
        }
    }

    [Fact]
    public void SegmentReader_PreservesEncodingFlagInDescriptor()
    {
        var mt = BuildMemTable(seriesCount: 1, pointsPerSeries: 10);
        string path = Path.Combine(_tempDir, "flag.SDBSEG");

        new SegmentWriter(new SegmentWriterOptions
        {
            FsyncOnCommit = false,
            TimestampEncoding = BlockEncoding.DeltaTimestamp,
        }).WriteFrom(mt, 1L, path);

        using var reader = SegmentReader.Open(path);
        Assert.Single(reader.Blocks);
        Assert.Equal(BlockEncoding.DeltaTimestamp, reader.Blocks[0].TimestampEncoding);
        Assert.Equal(BlockEncoding.None, reader.Blocks[0].ValueEncoding);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static MemTable BuildMemTable(int seriesCount, int pointsPerSeries)
    {
        var mt = new MemTable();
        long lsn = 1L;
        for (int s = 0; s < seriesCount; s++)
        {
            ulong sid = (ulong)(2000 + s);
            for (int i = 0; i < pointsPerSeries; i++)
            {
                long ts = 10_000L + i * 100L;
                mt.Append(sid, ts, "v", FieldValue.FromDouble(i * 1.5), lsn++);
            }
        }
        return mt;
    }
}
