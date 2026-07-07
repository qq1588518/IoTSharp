using System.Runtime.CompilerServices;
using SonnetDB.Buffers;
using SonnetDB.IO;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Format;

/// <summary>
/// <see cref="CatalogFileHeader"/> 单元测试。
/// </summary>
public sealed class CatalogFileHeaderTests
{
    // ── 尺寸 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Size_Is64Bytes()
        => Assert.Equal(FormatSizes.CatalogFileHeaderSize, Unsafe.SizeOf<CatalogFileHeader>());

    [Fact]
    public void FormatSizes_CatalogFileHeaderSize_Is64()
        => Assert.Equal(64, FormatSizes.CatalogFileHeaderSize);

    // ── default 全零 ──────────────────────────────────────────────────────────

    [Fact]
    public void Default_AllFieldsAreZero()
    {
        CatalogFileHeader h = default;
        Assert.True(h.Magic.AsReadOnlySpan().SequenceEqual(new byte[InlineBytes8.Length]));
        Assert.Equal(0, h.FormatVersion);
        Assert.Equal(0, h.HeaderSize);
        Assert.Equal(0L, h.CreatedAtUtcTicks);
        Assert.Equal(0L, h.LastModifiedUtcTicks);
        Assert.Equal(0, h.EntryCount);
        Assert.Equal(0u, h.Reserved0);
        Assert.True(h.Reserved.AsReadOnlySpan().SequenceEqual(new byte[InlineBytes24.Length]));
    }

    // ── CreateNew ────────────────────────────────────────────────────────────

    [Fact]
    public void CreateNew_SetsExpectedFields()
    {
        CatalogFileHeader h = CatalogFileHeader.CreateNew(42);
        Assert.True(h.Magic.AsReadOnlySpan().SequenceEqual(TsdbMagic.Catalog));
        Assert.Equal(TsdbMagic.FormatVersion, h.FormatVersion);
        Assert.Equal(FormatSizes.CatalogFileHeaderSize, h.HeaderSize);
        Assert.NotEqual(0L, h.CreatedAtUtcTicks);
        Assert.Equal(h.CreatedAtUtcTicks, h.LastModifiedUtcTicks);
        Assert.Equal(42, h.EntryCount);
    }

    [Fact]
    public void CreateNew_IsValid_ReturnsTrue()
        => Assert.True(CatalogFileHeader.CreateNew(0).IsValid());

    // ── IsValid ──────────────────────────────────────────────────────────────

    [Fact]
    public void IsValid_Default_ReturnsFalse()
        => Assert.False(default(CatalogFileHeader).IsValid());

    [Fact]
    public void IsValid_WrongVersion_ReturnsFalse()
    {
        var h = CatalogFileHeader.CreateNew(0);
        h.FormatVersion = 99;
        Assert.False(h.IsValid());
    }

    [Fact]
    public void IsValid_WrongMagic_ReturnsFalse()
    {
        var h = CatalogFileHeader.CreateNew(0);
        h.Magic.AsSpan()[0] = 0x00;
        Assert.False(h.IsValid());
    }

    [Fact]
    public void IsValid_WrongHeaderSize_ReturnsFalse()
    {
        var h = CatalogFileHeader.CreateNew(0);
        h.HeaderSize = 32;
        Assert.False(h.IsValid());
    }

    // ── Round-trip ───────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_WriteStruct_ReadStruct_AllFieldsEqual()
    {
        var original = CatalogFileHeader.CreateNew(7);
        original.LastModifiedUtcTicks = 99999L;
        original.Reserved0 = 0xDEADBEEF;
        original.Reserved.AsSpan().Fill(0xAB);

        Span<byte> buffer = stackalloc byte[FormatSizes.CatalogFileHeaderSize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);
        Assert.Equal(FormatSizes.CatalogFileHeaderSize, writer.Position);

        var reader = new SpanReader(buffer);
        var read = reader.ReadStruct<CatalogFileHeader>();

        Assert.True(original.Magic.AsReadOnlySpan().SequenceEqual(read.Magic.AsReadOnlySpan()));
        Assert.Equal(original.FormatVersion, read.FormatVersion);
        Assert.Equal(original.HeaderSize, read.HeaderSize);
        Assert.Equal(original.CreatedAtUtcTicks, read.CreatedAtUtcTicks);
        Assert.Equal(original.LastModifiedUtcTicks, read.LastModifiedUtcTicks);
        Assert.Equal(original.EntryCount, read.EntryCount);
        Assert.Equal(original.Reserved0, read.Reserved0);
        Assert.True(original.Reserved.AsReadOnlySpan().SequenceEqual(read.Reserved.AsReadOnlySpan()));
    }
}
