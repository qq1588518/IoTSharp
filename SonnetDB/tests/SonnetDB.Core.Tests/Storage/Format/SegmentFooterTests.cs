using System.Runtime.CompilerServices;
using SonnetDB.Buffers;
using SonnetDB.IO;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Format;

/// <summary>
/// <see cref="SegmentFooter"/> 单元测试。
/// </summary>
public sealed class SegmentFooterTests
{
    // ── Size ────────────────────────────────────────────────────────────────

    [Fact]
    public void Size_Is64Bytes()
        => Assert.Equal(FormatSizes.SegmentFooterSize, Unsafe.SizeOf<SegmentFooter>());

    // ── Default ─────────────────────────────────────────────────────────────

    [Fact]
    public void Default_AllFieldsAreZero()
    {
        SegmentFooter f = default;
        Assert.True(f.Magic.AsReadOnlySpan().SequenceEqual(new byte[InlineBytes8.Length]));
        Assert.Equal(0, f.FormatVersion);
        Assert.Equal(0, f.IndexCount);
        Assert.Equal(0L, f.IndexOffset);
        Assert.Equal(0L, f.FileLength);
        Assert.Equal(0U, f.Crc32);
    }

    // ── CreateNew ───────────────────────────────────────────────────────────

    [Fact]
    public void CreateNew_SetsExpectedFields()
    {
        SegmentFooter f = SegmentFooter.CreateNew(indexCount: 3, indexOffset: 1024L, fileLength: 1200L);
        Assert.True(f.Magic.AsReadOnlySpan().SequenceEqual(TsdbMagic.Segment));
        Assert.Equal(TsdbMagic.SegmentFormatVersion, f.FormatVersion);
        Assert.Equal(3, f.IndexCount);
        Assert.Equal(1024L, f.IndexOffset);
        Assert.Equal(1200L, f.FileLength);
    }

    [Fact]
    public void CreateNew_IsValid_ReturnsTrue()
        => Assert.True(SegmentFooter.CreateNew(0, 0L, 0L).IsValid());

    // ── IsValid ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsValid_Default_ReturnsFalse()
        => Assert.False(default(SegmentFooter).IsValid());

    [Fact]
    public void IsValid_WrongVersion_ReturnsFalse()
    {
        SegmentFooter f = SegmentFooter.CreateNew(0, 0L, 0L);
        f.FormatVersion = 99;
        Assert.False(f.IsValid());
    }

    [Fact]
    public void IsValid_WrongMagic_ReturnsFalse()
    {
        SegmentFooter f = SegmentFooter.CreateNew(0, 0L, 0L);
        f.Magic.AsSpan()[0] = 0x00;
        Assert.False(f.IsValid());
    }

    // ── Magic ────────────────────────────────────────────────────────────────

    [Fact]
    public void Magic_MatchesTsdbMagicSegment()
    {
        SegmentFooter f = SegmentFooter.CreateNew(0, 0L, 0L);
        Assert.True(f.Magic.AsReadOnlySpan().SequenceEqual(TsdbMagic.Segment));
    }

    // ── Round-trip ──────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_WriteStruct_ReadStruct_AllFieldsEqual()
    {
        SegmentFooter original = SegmentFooter.CreateNew(indexCount: 7, indexOffset: 8192L, fileLength: 8528L);
        original.Crc32 = 0xABCD1234U;

        Span<byte> buffer = stackalloc byte[FormatSizes.SegmentFooterSize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);
        Assert.Equal(FormatSizes.SegmentFooterSize, writer.Position);

        var reader = new SpanReader(buffer);
        SegmentFooter read = reader.ReadStruct<SegmentFooter>();

        Assert.True(original.Magic.AsReadOnlySpan().SequenceEqual(read.Magic.AsReadOnlySpan()));
        Assert.Equal(original.FormatVersion, read.FormatVersion);
        Assert.Equal(original.IndexCount, read.IndexCount);
        Assert.Equal(original.IndexOffset, read.IndexOffset);
        Assert.Equal(original.FileLength, read.FileLength);
        Assert.Equal(original.Crc32, read.Crc32);
    }

    // ── InlineBytes ─────────────────────────────────────────────────────────

    [Fact]
    public void InlineBytes_Reserved16_WriteAndRead_RoundTrip()
    {
        SegmentFooter original = SegmentFooter.CreateNew(0, 0L, 0L);
        original.Reserved16.AsSpan().Fill(0x55);

        Span<byte> buffer = stackalloc byte[FormatSizes.SegmentFooterSize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);

        var reader = new SpanReader(buffer);
        SegmentFooter read = reader.ReadStruct<SegmentFooter>();

        Assert.True(original.Reserved16.AsReadOnlySpan().SequenceEqual(read.Reserved16.AsReadOnlySpan()));
    }
}
