using System.Runtime.CompilerServices;
using SonnetDB.IO;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Format;

/// <summary>
/// <see cref="WalLastLsnFooter"/> 单元测试。
/// </summary>
public sealed class WalLastLsnFooterTests
{
    [Fact]
    public void Size_MatchesUnsafeSizeOf()
        => Assert.Equal(FormatSizes.WalLastLsnFooterSize, Unsafe.SizeOf<WalLastLsnFooter>());

    [Fact]
    public void CreateNew_FillsFixedFields()
    {
        var footer = WalLastLsnFooter.CreateNew(lastLsn: 42L, recordsEndOffset: 4096L);

        Assert.Equal(WalLastLsnFooter.MagicValue, footer.Magic);
        Assert.Equal(WalLastLsnFooter.VersionValue, footer.Version);
        Assert.Equal(FormatSizes.WalLastLsnFooterSize, footer.FooterSize);
        Assert.Equal(42L, footer.LastLsn);
        Assert.Equal(4096L, footer.RecordsEndOffset);
        Assert.True(footer.IsShapeValid());
    }

    [Fact]
    public void AsBytes_RoundTrip_PreservesFields()
    {
        var original = WalLastLsnFooter.CreateNew(lastLsn: 123L, recordsEndOffset: 9876L);
        original.Crc32 = 0xAABBCCDDu;

        Span<byte> buffer = stackalloc byte[FormatSizes.WalLastLsnFooterSize];
        var writer = new SpanWriter(buffer);
        writer.WriteStruct(in original);

        var reader = new SpanReader(buffer);
        var read = reader.ReadStruct<WalLastLsnFooter>();

        Assert.Equal(FormatSizes.WalLastLsnFooterSize, writer.Position);
        Assert.Equal(WalLastLsnFooter.MagicValue, read.Magic);
        Assert.Equal(WalLastLsnFooter.VersionValue, read.Version);
        Assert.Equal(123L, read.LastLsn);
        Assert.Equal(9876L, read.RecordsEndOffset);
        Assert.Equal(0xAABBCCDDu, read.Crc32);
    }
}
