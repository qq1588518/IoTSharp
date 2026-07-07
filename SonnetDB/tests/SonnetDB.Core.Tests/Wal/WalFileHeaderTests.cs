using System.Runtime.CompilerServices;
using SonnetDB.Buffers;
using SonnetDB.IO;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Wal;

/// <summary>
/// <see cref="WalFileHeader"/> 单元测试。
/// </summary>
public sealed class WalFileHeaderTests
{
    [Fact]
    public void Size_Is64Bytes()
        => Assert.Equal(FormatSizes.WalFileHeaderSize, Unsafe.SizeOf<WalFileHeader>());

    [Fact]
    public void CreateNew_SetsCorrectFields()
    {
        long before = DateTime.UtcNow.Ticks;
        var h = WalFileHeader.CreateNew(firstLsn: 1L);
        long after = DateTime.UtcNow.Ticks;

        Assert.True(h.IsValid());
        Assert.Equal(TsdbMagic.FormatVersion, h.FormatVersion);
        Assert.Equal(FormatSizes.WalFileHeaderSize, h.HeaderSize);
        Assert.Equal(1L, h.FirstLsn);
        Assert.InRange(h.CreatedAtUtcTicks, before, after);
    }

    [Fact]
    public void Magic_MatchesTsdbMagicWal()
    {
        var h = WalFileHeader.CreateNew(1L);
        Assert.True(h.Magic.AsReadOnlySpan().SequenceEqual(TsdbMagic.Wal));
    }

    [Fact]
    public void IsValid_InvalidMagic_ReturnsFalse()
    {
        var h = WalFileHeader.CreateNew(1L);
        h.Magic.AsSpan()[0] ^= 0xFF; // corrupt magic
        Assert.False(h.IsValid());
    }

    [Fact]
    public void RoundTrip_WriteRead_AllFieldsEqual()
    {
        var original = WalFileHeader.CreateNew(42L);

        Span<byte> buffer = stackalloc byte[FormatSizes.WalFileHeaderSize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);
        Assert.Equal(FormatSizes.WalFileHeaderSize, writer.Position);

        var reader = new SpanReader(buffer);
        var read = reader.ReadStruct<WalFileHeader>();

        Assert.True(read.Magic.AsReadOnlySpan().SequenceEqual(original.Magic.AsReadOnlySpan()));
        Assert.Equal(original.FormatVersion, read.FormatVersion);
        Assert.Equal(original.HeaderSize, read.HeaderSize);
        Assert.Equal(original.CreatedAtUtcTicks, read.CreatedAtUtcTicks);
        Assert.Equal(original.FirstLsn, read.FirstLsn);
    }
}
