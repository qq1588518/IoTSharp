using System.Runtime.CompilerServices;
using SonnetDB.IO;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Format;

/// <summary>
/// <see cref="WalRecordHeader"/> 单元测试。
/// </summary>
public sealed class WalRecordHeaderTests
{
    // ── Size ────────────────────────────────────────────────────────────────

    [Fact]
    public void Size_Is32Bytes()
        => Assert.Equal(FormatSizes.WalRecordHeaderSize, Unsafe.SizeOf<WalRecordHeader>());

    // ── Default ─────────────────────────────────────────────────────────────

    [Fact]
    public void Default_AllFieldsAreZero()
    {
        WalRecordHeader h = default;
        Assert.Equal(0u, h.Magic);
        Assert.Equal(WalRecordType.Unknown, h.RecordType);
        Assert.Equal(0, h.Flags);
        Assert.Equal(0, h.Reserved);
        Assert.Equal(0, h.PayloadLength);
        Assert.Equal(0u, h.PayloadCrc32);
        Assert.Equal(0L, h.Timestamp);
        Assert.Equal(0L, h.Lsn);
    }

    // ── MagicValue ──────────────────────────────────────────────────────────

    [Fact]
    public void MagicValue_IsCorrect()
        => Assert.Equal(0x57414C52u, WalRecordHeader.MagicValue);

    // ── CreateNew ───────────────────────────────────────────────────────────

    [Fact]
    public void CreateNew_SetsExpectedFields()
    {
        WalRecordHeader h = WalRecordHeader.CreateNew(
            recordType: WalRecordType.WritePoint,
            payloadLength: 16,
            payloadCrc32: 0xDEADBEEFu,
            timestampUtcTicks: 1_000_000L,
            lsn: 42L);

        Assert.Equal(WalRecordHeader.MagicValue, h.Magic);
        Assert.Equal(WalRecordType.WritePoint, h.RecordType);
        Assert.Equal(0, h.Flags);
        Assert.Equal(0, h.Reserved);
        Assert.Equal(16, h.PayloadLength);
        Assert.Equal(0xDEADBEEFu, h.PayloadCrc32);
        Assert.Equal(1_000_000L, h.Timestamp);
        Assert.Equal(42L, h.Lsn);
    }

    // ── IsMagicValid ────────────────────────────────────────────────────────

    [Fact]
    public void IsMagicValid_WhenMagicSet_ReturnsTrue()
    {
        WalRecordHeader h = WalRecordHeader.CreateNew(WalRecordType.WritePoint, 0, 0, 0L, 1L);
        Assert.True(h.IsMagicValid());
    }

    [Fact]
    public void IsMagicValid_WhenMagicZero_ReturnsFalse()
    {
        WalRecordHeader h = default;
        Assert.False(h.IsMagicValid());
    }

    // ── Round-trip ──────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_WriteStruct_ReadStruct_AllFieldsEqual()
    {
        WalRecordHeader original = WalRecordHeader.CreateNew(
            recordType: WalRecordType.Checkpoint,
            payloadLength: 8,
            payloadCrc32: 0x12345678u,
            timestampUtcTicks: 9_999_999L,
            lsn: 99L);

        Span<byte> buffer = stackalloc byte[FormatSizes.WalRecordHeaderSize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);
        Assert.Equal(FormatSizes.WalRecordHeaderSize, writer.Position);

        var reader = new SpanReader(buffer);
        WalRecordHeader read = reader.ReadStruct<WalRecordHeader>();

        Assert.Equal(original.Magic, read.Magic);
        Assert.Equal(original.RecordType, read.RecordType);
        Assert.Equal(original.Flags, read.Flags);
        Assert.Equal(original.Reserved, read.Reserved);
        Assert.Equal(original.PayloadLength, read.PayloadLength);
        Assert.Equal(original.PayloadCrc32, read.PayloadCrc32);
        Assert.Equal(original.Timestamp, read.Timestamp);
        Assert.Equal(original.Lsn, read.Lsn);
    }

    // ── Enum values ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(WalRecordType.Unknown, (byte)0)]
    [InlineData(WalRecordType.WritePoint, (byte)1)]
    [InlineData(WalRecordType.Checkpoint, (byte)2)]
    [InlineData(WalRecordType.CreateSeries, (byte)3)]
    [InlineData(WalRecordType.Truncate, (byte)4)]
    public void WalRecordType_ByteValues_AreCorrect(WalRecordType type, byte expected)
        => Assert.Equal(expected, (byte)type);

    [Fact]
    public void RoundTrip_AllRecordTypes_SerializeCorrectly()
    {
        Span<byte> buffer = stackalloc byte[FormatSizes.WalRecordHeaderSize];
        foreach (WalRecordType type in Enum.GetValues<WalRecordType>())
        {
            WalRecordHeader original = WalRecordHeader.CreateNew(type, 0, 0u, 1L, 1L);
            buffer.Clear();
            var writer = new SpanWriter(buffer);
            writer.WriteStruct(in original);

            var reader = new SpanReader(buffer);
            WalRecordHeader read = reader.ReadStruct<WalRecordHeader>();
            Assert.Equal(type, read.RecordType);
        }
    }

    [Fact]
    public void IsShapeValid_WithHeaderChecksum_DetectsHeaderDamage()
    {
        WalRecordHeader original = WalRecordHeader.CreateNew(
            recordType: WalRecordType.WritePoint,
            payloadLength: 16,
            payloadCrc32: 0x12345678u,
            timestampUtcTicks: 9_999_999L,
            lsn: 99L);
        original.Flags = WalRecordHeader.HeaderChecksumFlag;

        Span<byte> buffer = stackalloc byte[FormatSizes.WalRecordHeaderSize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);
        original.Reserved = WalRecordHeader.ComputeHeaderChecksum(buffer);
        writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);

        var reader = new SpanReader(buffer);
        WalRecordHeader read = reader.ReadStruct<WalRecordHeader>();
        Assert.True(read.IsShapeValid(buffer));

        buffer[8] ^= 0x01;
        reader = new SpanReader(buffer);
        read = reader.ReadStruct<WalRecordHeader>();
        Assert.False(read.IsShapeValid(buffer));
    }
}
