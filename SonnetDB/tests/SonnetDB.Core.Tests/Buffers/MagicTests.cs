using SonnetDB.Buffers;
using Xunit;

namespace SonnetDB.Core.Tests.Buffers;

/// <summary>
/// <see cref="TsdbMagic"/> 常量与工厂方法测试。
/// </summary>
public sealed class MagicTests
{
    // ── 工厂方法字节序列正确 ──────────────────────────────────────────────────

    [Fact]
    public void CreateFileMagic_BytesEqual_FileMagicSpan()
    {
        InlineBytes8 magic = TsdbMagic.CreateFileMagic();
        Assert.True(magic.AsReadOnlySpan().SequenceEqual(TsdbMagic.File));
    }

    [Fact]
    public void CreateSegmentMagic_BytesEqual_SegmentMagicSpan()
    {
        InlineBytes8 magic = TsdbMagic.CreateSegmentMagic();
        Assert.True(magic.AsReadOnlySpan().SequenceEqual(TsdbMagic.Segment));
    }

    [Fact]
    public void CreateWalMagic_BytesEqual_WalMagicSpan()
    {
        InlineBytes8 magic = TsdbMagic.CreateWalMagic();
        Assert.True(magic.AsReadOnlySpan().SequenceEqual(TsdbMagic.Wal));
    }

    // ── 三个 magic 互不相等 ───────────────────────────────────────────────────

    [Fact]
    public void FileMagic_NotEqual_SegmentMagic()
    {
        InlineBytes8 file = TsdbMagic.CreateFileMagic();
        InlineBytes8 segment = TsdbMagic.CreateSegmentMagic();
        Assert.False(file.AsReadOnlySpan().SequenceEqual(segment.AsReadOnlySpan()));
    }

    [Fact]
    public void FileMagic_NotEqual_WalMagic()
    {
        InlineBytes8 file = TsdbMagic.CreateFileMagic();
        InlineBytes8 wal = TsdbMagic.CreateWalMagic();
        Assert.False(file.AsReadOnlySpan().SequenceEqual(wal.AsReadOnlySpan()));
    }

    [Fact]
    public void SegmentMagic_NotEqual_WalMagic()
    {
        InlineBytes8 segment = TsdbMagic.CreateSegmentMagic();
        InlineBytes8 wal = TsdbMagic.CreateWalMagic();
        Assert.False(segment.AsReadOnlySpan().SequenceEqual(wal.AsReadOnlySpan()));
    }

    // ── 字节内容精确验证 ──────────────────────────────────────────────────────

    [Fact]
    public void CreateFileMagic_FirstBytes_MatchAscii_SONNETDB()
    {
        InlineBytes8 magic = TsdbMagic.CreateFileMagic();
        ReadOnlySpan<byte> span = magic.AsReadOnlySpan();
        Assert.Equal((byte)'S', span[0]);
        Assert.Equal((byte)'O', span[1]);
        Assert.Equal((byte)'N', span[2]);
        Assert.Equal((byte)'N', span[3]);
        Assert.Equal((byte)'E', span[4]);
        Assert.Equal((byte)'T', span[5]);
        Assert.Equal((byte)'D', span[6]);
        Assert.Equal((byte)'B', span[7]);
    }

    [Fact]
    public void FormatVersion_IsOne()
        => Assert.Equal(1, TsdbMagic.FormatVersion);
}
