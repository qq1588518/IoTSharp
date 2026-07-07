using System.Runtime.CompilerServices;
using SonnetDB.Buffers;
using SonnetDB.IO;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Format;

/// <summary>
/// <see cref="BlockHeader"/> 单元测试。
/// </summary>
public sealed class BlockHeaderTests
{
    // ── Size ────────────────────────────────────────────────────────────────

    [Fact]
    public void Size_Is72Bytes()
        => Assert.Equal(FormatSizes.BlockHeaderSize, Unsafe.SizeOf<BlockHeader>());

    // ── Default ─────────────────────────────────────────────────────────────

    [Fact]
    public void Default_AllFieldsAreZero()
    {
        BlockHeader h = default;
        Assert.Equal(0UL, h.SeriesId);
        Assert.Equal(0L, h.MinTimestamp);
        Assert.Equal(0L, h.MaxTimestamp);
        Assert.Equal(0, h.Count);
        Assert.Equal(0, h.TimestampPayloadLength);
        Assert.Equal(0, h.ValuePayloadLength);
        Assert.Equal(0, h.FieldNameUtf8Length);
        Assert.Equal(BlockEncoding.None, h.Encoding);
        Assert.Equal(FieldType.Unknown, h.FieldType);
    }

    // ── CreateNew ───────────────────────────────────────────────────────────

    [Fact]
    public void CreateNew_SetsExpectedFields()
    {
        BlockHeader h = BlockHeader.CreateNew(
            seriesId: 123UL,
            min: 1000L,
            max: 2000L,
            count: 10,
            fieldType: FieldType.Float64,
            fieldNameLen: 5,
            tsLen: 80,
            valLen: 80);

        Assert.Equal(123UL, h.SeriesId);
        Assert.Equal(1000L, h.MinTimestamp);
        Assert.Equal(2000L, h.MaxTimestamp);
        Assert.Equal(10, h.Count);
        Assert.Equal(FieldType.Float64, h.FieldType);
        Assert.Equal(5, h.FieldNameUtf8Length);
        Assert.Equal(80, h.TimestampPayloadLength);
        Assert.Equal(80, h.ValuePayloadLength);
        Assert.Equal(BlockEncoding.None, h.Encoding);
    }

    // ── Round-trip ──────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_WriteStruct_ReadStruct_AllFieldsEqual()
    {
        BlockHeader original = BlockHeader.CreateNew(
            seriesId: 0xDEADBEEFUL,
            min: -100L,
            max: 100L,
            count: 50,
            fieldType: FieldType.Int64,
            fieldNameLen: 8,
            tsLen: 400,
            valLen: 400);
        original.Encoding = BlockEncoding.DeltaTimestamp;

        Span<byte> buffer = stackalloc byte[FormatSizes.BlockHeaderSize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);
        Assert.Equal(FormatSizes.BlockHeaderSize, writer.Position);

        var reader = new SpanReader(buffer);
        BlockHeader read = reader.ReadStruct<BlockHeader>();

        Assert.Equal(original.SeriesId, read.SeriesId);
        Assert.Equal(original.MinTimestamp, read.MinTimestamp);
        Assert.Equal(original.MaxTimestamp, read.MaxTimestamp);
        Assert.Equal(original.Count, read.Count);
        Assert.Equal(original.TimestampPayloadLength, read.TimestampPayloadLength);
        Assert.Equal(original.ValuePayloadLength, read.ValuePayloadLength);
        Assert.Equal(original.FieldNameUtf8Length, read.FieldNameUtf8Length);
        Assert.Equal(original.Encoding, read.Encoding);
        Assert.Equal(original.FieldType, read.FieldType);
    }

    [Fact]
    public void AggregateMetadata_WriteAndRead_RoundTrip()
    {
        BlockHeader original = BlockHeader.CreateNew(1UL, 0L, 0L, 3, FieldType.Float64, 0, 0, 0);
        original.AggregateFlags = BlockHeader.HasSumCount | BlockHeader.HasMinMax;
        original.AggregateSum = 12.5;
        // v2：AggregateMin/Max 直接是 8 字节 double，无损覆盖 Float64 / Int64 全部范围。
        original.AggregateMin = 1.234567890123456;
        original.AggregateMax = double.MaxValue;

        Span<byte> buffer = stackalloc byte[FormatSizes.BlockHeaderSize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);

        var reader = new SpanReader(buffer);
        BlockHeader read = reader.ReadStruct<BlockHeader>();

        Assert.Equal(original.AggregateFlags, read.AggregateFlags);
        Assert.Equal(original.AggregateSum, read.AggregateSum);
        Assert.Equal(original.AggregateMin, read.AggregateMin);
        Assert.Equal(original.AggregateMax, read.AggregateMax);
        Assert.True((read.AggregateFlags & BlockHeader.HasSumCount) != 0);
        Assert.True((read.AggregateFlags & BlockHeader.HasMinMax) != 0);
    }

    [Fact]
    public void AggregateMin_PreservesInt64FullRange()
    {
        // 验证 v2 升级后 Int64 极值不再因窄类型截断丢失。
        BlockHeader original = BlockHeader.CreateNew(1UL, 0L, 0L, 1, FieldType.Int64, 0, 0, 0);
        original.AggregateFlags = BlockHeader.HasSumCount | BlockHeader.HasMinMax;
        original.AggregateMin = (double)long.MinValue;
        original.AggregateMax = (double)long.MaxValue;

        Span<byte> buffer = stackalloc byte[FormatSizes.BlockHeaderSize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);

        var reader = new SpanReader(buffer);
        BlockHeader read = reader.ReadStruct<BlockHeader>();
        Assert.Equal((double)long.MinValue, read.AggregateMin);
        Assert.Equal((double)long.MaxValue, read.AggregateMax);
    }

    [Fact]
    public void AggregateFlags_LegacyValueOne_MapsToSumCountOnly()
    {
        // 兼容性：v1 写入侧曾把 flags 直接置 1（含义为“sum/min/max 都有”，但 min/max 实为有损）。
        // v2 段文件不会再出现这种 flags（写入侧总是同时置 HasMinMax），但常量映射仍保持向后定义清晰。
        const short legacyFlags = 1;
        Assert.Equal(BlockHeader.HasSumCount, legacyFlags);
        Assert.Equal(0, legacyFlags & BlockHeader.HasMinMax);
    }
}
