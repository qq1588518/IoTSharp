using System.Runtime.CompilerServices;
using SonnetDB.Buffers;
using SonnetDB.IO;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Format;

/// <summary>
/// <see cref="SegmentHeader"/> 单元测试。
/// </summary>
public sealed class SegmentHeaderTests
{
    // ── Size ────────────────────────────────────────────────────────────────

    [Fact]
    public void Size_Is64Bytes()
        => Assert.Equal(FormatSizes.SegmentHeaderSize, Unsafe.SizeOf<SegmentHeader>());

    // ── Default ─────────────────────────────────────────────────────────────

    [Fact]
    public void Default_AllFieldsAreZero()
    {
        SegmentHeader h = default;
        Assert.True(h.Magic.AsReadOnlySpan().SequenceEqual(new byte[InlineBytes8.Length]));
        Assert.Equal(0, h.FormatVersion);
        Assert.Equal(0, h.HeaderSize);
        Assert.Equal(0L, h.SegmentId);
        Assert.Equal(0L, h.CreatedAtUtcTicks);
        Assert.Equal(0, h.BlockCount);
    }

    // ── CreateNew ───────────────────────────────────────────────────────────

    [Fact]
    public void CreateNew_SetsExpectedFields()
    {
        SegmentHeader h = SegmentHeader.CreateNew(42L);
        Assert.True(h.Magic.AsReadOnlySpan().SequenceEqual(TsdbMagic.Segment));
        Assert.Equal(TsdbMagic.SegmentFormatVersion, h.FormatVersion);
        Assert.Equal(FormatSizes.SegmentHeaderSize, h.HeaderSize);
        Assert.Equal(42L, h.SegmentId);
        Assert.NotEqual(0L, h.CreatedAtUtcTicks);
    }

    [Fact]
    public void CreateNew_IsValid_ReturnsTrue()
        => Assert.True(SegmentHeader.CreateNew(1L).IsValid());

    // ── IsValid ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsValid_Default_ReturnsFalse()
        => Assert.False(default(SegmentHeader).IsValid());

    [Fact]
    public void IsValid_WrongVersion_ReturnsFalse()
    {
        SegmentHeader h = SegmentHeader.CreateNew(1L);
        h.FormatVersion = 99;
        Assert.False(h.IsValid());
    }

    [Fact]
    public void IsValid_WrongMagic_ReturnsFalse()
    {
        SegmentHeader h = SegmentHeader.CreateNew(1L);
        h.Magic.AsSpan()[0] = 0x00;
        Assert.False(h.IsValid());
    }

    // ── Magic ────────────────────────────────────────────────────────────────

    [Fact]
    public void Magic_MatchesTsdbMagicSegment()
    {
        SegmentHeader h = SegmentHeader.CreateNew(1L);
        Assert.True(h.Magic.AsReadOnlySpan().SequenceEqual(TsdbMagic.Segment));
    }

    // ── Round-trip ──────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_WriteStruct_ReadStruct_AllFieldsEqual()
    {
        SegmentHeader original = SegmentHeader.CreateNew(999L);
        original.BlockCount = 5;

        Span<byte> buffer = stackalloc byte[FormatSizes.SegmentHeaderSize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);
        Assert.Equal(FormatSizes.SegmentHeaderSize, writer.Position);

        var reader = new SpanReader(buffer);
        SegmentHeader read = reader.ReadStruct<SegmentHeader>();

        Assert.True(original.Magic.AsReadOnlySpan().SequenceEqual(read.Magic.AsReadOnlySpan()));
        Assert.Equal(original.FormatVersion, read.FormatVersion);
        Assert.Equal(original.HeaderSize, read.HeaderSize);
        Assert.Equal(original.SegmentId, read.SegmentId);
        Assert.Equal(original.CreatedAtUtcTicks, read.CreatedAtUtcTicks);
        Assert.Equal(original.BlockCount, read.BlockCount);
    }

    // ── InlineBytes ─────────────────────────────────────────────────────────

    [Fact]
    public void InlineBytes_Reserved16_WriteAndRead_RoundTrip()
    {
        SegmentHeader original = SegmentHeader.CreateNew(1L);
        original.Reserved16.AsSpan().Fill(0xCD);

        Span<byte> buffer = stackalloc byte[FormatSizes.SegmentHeaderSize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);

        var reader = new SpanReader(buffer);
        SegmentHeader read = reader.ReadStruct<SegmentHeader>();

        Assert.True(original.Reserved16.AsReadOnlySpan().SequenceEqual(read.Reserved16.AsReadOnlySpan()));
    }

    [Fact]
    public void FooterMiniCopy_WriteAndRead_RoundTrip()
    {
        SegmentHeader header = SegmentHeader.CreateNew(1L);
        header.WriteFooterMiniCopy(
            indexCount: 7,
            indexOffset: 4096L,
            fileLength: 8192L,
            indexCrc32: 0xAABBCCDDU);

        Assert.True(header.TryReadFooterMiniCopy(out var mini));
        Assert.Equal(7, mini.IndexCount);
        Assert.Equal(4096L, mini.IndexOffset);
        Assert.Equal(8192L, mini.FileLength);
        Assert.Equal(0xAABBCCDDU, mini.IndexCrc32);
    }
}
