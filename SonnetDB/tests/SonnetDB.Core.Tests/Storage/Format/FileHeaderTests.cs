using System.Runtime.CompilerServices;
using SonnetDB.Buffers;
using SonnetDB.IO;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Format;

/// <summary>
/// <see cref="FileHeader"/> 单元测试。
/// </summary>
public sealed class FileHeaderTests
{
    // ── Size ────────────────────────────────────────────────────────────────

    [Fact]
    public void Size_Is64Bytes()
        => Assert.Equal(FormatSizes.FileHeaderSize, Unsafe.SizeOf<FileHeader>());

    // ── Default ─────────────────────────────────────────────────────────────

    [Fact]
    public void Default_AllFieldsAreZero()
    {
        FileHeader h = default;
        Assert.True(h.Magic.AsReadOnlySpan().SequenceEqual(new byte[InlineBytes8.Length]));
        Assert.Equal(0, h.FormatVersion);
        Assert.Equal(0, h.HeaderSize);
        Assert.Equal(0L, h.CreatedAtUtcTicks);
        Assert.Equal(0L, h.LastModifiedUtcTicks);
        Assert.Equal(0L, h.PageSize);
        Assert.True(h.InstanceId.AsReadOnlySpan().SequenceEqual(new byte[InlineBytes16.Length]));
        Assert.True(h.Reserved.AsReadOnlySpan().SequenceEqual(new byte[InlineBytes8.Length]));
    }

    // ── CreateNew ───────────────────────────────────────────────────────────

    [Fact]
    public void CreateNew_SetsExpectedFields()
    {
        FileHeader h = FileHeader.CreateNew();
        Assert.True(h.Magic.AsReadOnlySpan().SequenceEqual(TsdbMagic.File));
        Assert.Equal(TsdbMagic.FormatVersion, h.FormatVersion);
        Assert.Equal(FormatSizes.FileHeaderSize, h.HeaderSize);
        Assert.NotEqual(0L, h.CreatedAtUtcTicks);
        Assert.Equal(h.CreatedAtUtcTicks, h.LastModifiedUtcTicks);
        Assert.Equal(0L, h.PageSize);
    }

    [Fact]
    public void CreateNew_IsValid_ReturnsTrue()
        => Assert.True(FileHeader.CreateNew().IsValid());

    // ── IsValid ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsValid_Default_ReturnsFalse()
        => Assert.False(default(FileHeader).IsValid());

    [Fact]
    public void IsValid_WrongVersion_ReturnsFalse()
    {
        FileHeader h = FileHeader.CreateNew();
        h.FormatVersion = 99;
        Assert.False(h.IsValid());
    }

    [Fact]
    public void IsValid_WrongMagic_ReturnsFalse()
    {
        FileHeader h = FileHeader.CreateNew();
        h.Magic.AsSpan()[0] = 0x00;
        Assert.False(h.IsValid());
    }

    // ── Magic ────────────────────────────────────────────────────────────────

    [Fact]
    public void Magic_MatchesTsdbMagicFile()
    {
        FileHeader h = FileHeader.CreateNew();
        Assert.True(h.Magic.AsReadOnlySpan().SequenceEqual(TsdbMagic.File));
    }

    // ── Round-trip ──────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_WriteStruct_ReadStruct_AllFieldsEqual()
    {
        FileHeader original = FileHeader.CreateNew();
        original.LastModifiedUtcTicks = 12345L;
        original.PageSize = 4096L;
        original.InstanceId.AsSpan().Fill(0xAB);

        Span<byte> buffer = stackalloc byte[FormatSizes.FileHeaderSize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);
        Assert.Equal(FormatSizes.FileHeaderSize, writer.Position);

        var reader = new SpanReader(buffer);
        FileHeader read = reader.ReadStruct<FileHeader>();

        Assert.True(original.Magic.AsReadOnlySpan().SequenceEqual(read.Magic.AsReadOnlySpan()));
        Assert.Equal(original.FormatVersion, read.FormatVersion);
        Assert.Equal(original.HeaderSize, read.HeaderSize);
        Assert.Equal(original.CreatedAtUtcTicks, read.CreatedAtUtcTicks);
        Assert.Equal(original.LastModifiedUtcTicks, read.LastModifiedUtcTicks);
        Assert.Equal(original.PageSize, read.PageSize);
        Assert.True(original.InstanceId.AsReadOnlySpan().SequenceEqual(read.InstanceId.AsReadOnlySpan()));
        Assert.True(original.Reserved.AsReadOnlySpan().SequenceEqual(read.Reserved.AsReadOnlySpan()));
    }

    // ── InlineBytes ─────────────────────────────────────────────────────────

    [Fact]
    public void InlineBytes_Reserved_WriteAndRead_RoundTrip()
    {
        FileHeader original = FileHeader.CreateNew();
        original.Reserved.AsSpan().Fill(0xFF);

        Span<byte> buffer = stackalloc byte[FormatSizes.FileHeaderSize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);

        var reader = new SpanReader(buffer);
        FileHeader read = reader.ReadStruct<FileHeader>();

        Assert.True(original.Reserved.AsReadOnlySpan().SequenceEqual(read.Reserved.AsReadOnlySpan()));
    }
}
