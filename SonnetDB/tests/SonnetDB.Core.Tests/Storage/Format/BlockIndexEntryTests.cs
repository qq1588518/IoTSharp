using System.Runtime.CompilerServices;
using SonnetDB.Buffers;
using SonnetDB.IO;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Format;

/// <summary>
/// <see cref="BlockIndexEntry"/> 单元测试。
/// </summary>
public sealed class BlockIndexEntryTests
{
    // ── Size ────────────────────────────────────────────────────────────────

    [Fact]
    public void Size_Is48Bytes()
        => Assert.Equal(FormatSizes.BlockIndexEntrySize, Unsafe.SizeOf<BlockIndexEntry>());

    // ── Default ─────────────────────────────────────────────────────────────

    [Fact]
    public void Default_AllFieldsAreZero()
    {
        BlockIndexEntry e = default;
        Assert.Equal(0UL, e.SeriesId);
        Assert.Equal(0L, e.MinTimestamp);
        Assert.Equal(0L, e.MaxTimestamp);
        Assert.Equal(0L, e.FileOffset);
        Assert.Equal(0, e.BlockLength);
        Assert.Equal(0, e.FieldNameHash);
        Assert.True(e.Reserved.AsReadOnlySpan().SequenceEqual(new byte[InlineBytes8.Length]));
    }

    // ── Round-trip ──────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_WriteStruct_ReadStruct_AllFieldsEqual()
    {
        BlockIndexEntry original = new BlockIndexEntry
        {
            SeriesId = 0xABCDEF01UL,
            MinTimestamp = 500L,
            MaxTimestamp = 1500L,
            FileOffset = 4096L,
            BlockLength = 2048,
            FieldNameHash = unchecked((int)0xDEADBEEF),
        };
        original.Reserved.AsSpan().Fill(0x11);

        Span<byte> buffer = stackalloc byte[FormatSizes.BlockIndexEntrySize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);
        Assert.Equal(FormatSizes.BlockIndexEntrySize, writer.Position);

        var reader = new SpanReader(buffer);
        BlockIndexEntry read = reader.ReadStruct<BlockIndexEntry>();

        Assert.Equal(original.SeriesId, read.SeriesId);
        Assert.Equal(original.MinTimestamp, read.MinTimestamp);
        Assert.Equal(original.MaxTimestamp, read.MaxTimestamp);
        Assert.Equal(original.FileOffset, read.FileOffset);
        Assert.Equal(original.BlockLength, read.BlockLength);
        Assert.Equal(original.FieldNameHash, read.FieldNameHash);
        Assert.True(original.Reserved.AsReadOnlySpan().SequenceEqual(read.Reserved.AsReadOnlySpan()));
    }

    // ── InlineBytes ─────────────────────────────────────────────────────────

    [Fact]
    public void InlineBytes_Reserved_WriteAndRead_RoundTrip()
    {
        BlockIndexEntry original = default;
        original.Reserved.AsSpan().Fill(0x99);

        Span<byte> buffer = stackalloc byte[FormatSizes.BlockIndexEntrySize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);

        var reader = new SpanReader(buffer);
        BlockIndexEntry read = reader.ReadStruct<BlockIndexEntry>();

        Assert.True(original.Reserved.AsReadOnlySpan().SequenceEqual(read.Reserved.AsReadOnlySpan()));
    }
}
