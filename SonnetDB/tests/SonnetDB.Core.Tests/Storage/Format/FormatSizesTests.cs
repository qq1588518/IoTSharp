using System.Runtime.CompilerServices;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Format;

/// <summary>
/// <see cref="FormatSizes"/> 常量与 <c>Unsafe.SizeOf&lt;T&gt;()</c> 一致性测试。
/// 这是格式稳定性的硬约束：任何结构体布局变更都会导致此处断言失败。
/// </summary>
public sealed class FormatSizesTests
{
    [Fact]
    public void FileHeaderSize_MatchesUnsafeSizeOf()
        => Assert.Equal(FormatSizes.FileHeaderSize, Unsafe.SizeOf<FileHeader>());

    [Fact]
    public void SegmentHeaderSize_MatchesUnsafeSizeOf()
        => Assert.Equal(FormatSizes.SegmentHeaderSize, Unsafe.SizeOf<SegmentHeader>());

    [Fact]
    public void BlockHeaderSize_MatchesUnsafeSizeOf()
        => Assert.Equal(FormatSizes.BlockHeaderSize, Unsafe.SizeOf<BlockHeader>());

    [Fact]
    public void BlockIndexEntrySize_MatchesUnsafeSizeOf()
        => Assert.Equal(FormatSizes.BlockIndexEntrySize, Unsafe.SizeOf<BlockIndexEntry>());

    [Fact]
    public void SegmentFooterSize_MatchesUnsafeSizeOf()
        => Assert.Equal(FormatSizes.SegmentFooterSize, Unsafe.SizeOf<SegmentFooter>());

    [Fact]
    public void WalRecordHeaderSize_MatchesUnsafeSizeOf()
        => Assert.Equal(FormatSizes.WalRecordHeaderSize, Unsafe.SizeOf<WalRecordHeader>());

    [Fact]
    public void WalFileHeaderSize_MatchesUnsafeSizeOf()
        => Assert.Equal(FormatSizes.WalFileHeaderSize, Unsafe.SizeOf<WalFileHeader>());

    [Fact]
    public void WalLastLsnFooterSize_MatchesUnsafeSizeOf()
        => Assert.Equal(FormatSizes.WalLastLsnFooterSize, Unsafe.SizeOf<WalLastLsnFooter>());

    [Fact]
    public void AllSizes_AreCorrect()
    {
        Assert.Equal(64, FormatSizes.FileHeaderSize);
        Assert.Equal(64, FormatSizes.SegmentHeaderSize);
        Assert.Equal(80, FormatSizes.BlockHeaderSize);
        Assert.Equal(48, FormatSizes.BlockIndexEntrySize);
        Assert.Equal(64, FormatSizes.SegmentFooterSize);
        Assert.Equal(32, FormatSizes.WalRecordHeaderSize);
        Assert.Equal(64, FormatSizes.WalFileHeaderSize);
        Assert.Equal(32, FormatSizes.WalLastLsnFooterSize);
    }
}
