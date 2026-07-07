using SonnetDB.Buffers;
using Xunit;

namespace SonnetDB.Core.Tests.Buffers;

/// <summary>
/// <see cref="InlineBytesExtensions"/> 扩展方法测试。
/// </summary>
public sealed class InlineBytesExtensionsTests
{
    // ── AsSpan 写入后 AsReadOnlySpan 能读到相同内容 ────────────────────────────

    [Fact]
    public void AsSpan_InlineBytes4_WriteAndReadBack_ContentMatches()
    {
        InlineBytes4 buf = default;
        byte[] data = [0x01, 0x02, 0x03, 0x04];
        data.CopyTo(buf.AsSpan());

        ReadOnlySpan<byte> ros = buf.AsReadOnlySpan();
        Assert.Equal(data, ros.ToArray());
    }

    [Fact]
    public void AsSpan_InlineBytes8_WriteAndReadBack_ContentMatches()
    {
        InlineBytes8 buf = default;
        byte[] data = [0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80];
        data.CopyTo(buf.AsSpan());

        ReadOnlySpan<byte> ros = buf.AsReadOnlySpan();
        Assert.Equal(data, ros.ToArray());
    }

    [Fact]
    public void AsSpan_InlineBytes16_WriteAndReadBack_ContentMatches()
    {
        InlineBytes16 buf = default;
        byte[] data = new byte[InlineBytes16.Length];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)(i + 1);
        data.CopyTo(buf.AsSpan());

        Assert.Equal(data, buf.AsReadOnlySpan().ToArray());
    }

    [Fact]
    public void AsSpan_InlineBytes32_WriteAndReadBack_ContentMatches()
    {
        InlineBytes32 buf = default;
        byte[] data = new byte[InlineBytes32.Length];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)(i + 100);
        data.CopyTo(buf.AsSpan());

        Assert.Equal(data, buf.AsReadOnlySpan().ToArray());
    }

    [Fact]
    public void AsSpan_InlineBytes64_WriteAndReadBack_ContentMatches()
    {
        InlineBytes64 buf = default;
        byte[] data = new byte[InlineBytes64.Length];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)(255 - i);
        data.CopyTo(buf.AsSpan());

        Assert.Equal(data, buf.AsReadOnlySpan().ToArray());
    }

    // ── Span 修改会反映到原结构体（共享底层内存）──────────────────────────────

    [Fact]
    public void AsSpan_InlineBytes4_SpanMutationReflectsInStruct()
    {
        InlineBytes4 buf = default;
        Span<byte> span = buf.AsSpan();
        span[0] = 0xFF;
        span[3] = 0xAB;

        Assert.Equal(0xFF, buf[0]);
        Assert.Equal(0xAB, buf[3]);
    }

    [Fact]
    public void AsSpan_InlineBytes8_SpanMutationReflectsInStruct()
    {
        InlineBytes8 buf = default;
        Span<byte> span = buf.AsSpan();
        for (int i = 0; i < InlineBytes8.Length; i++)
            span[i] = (byte)(i * 3 + 1);

        for (int i = 0; i < InlineBytes8.Length; i++)
            Assert.Equal((byte)(i * 3 + 1), buf[i]);
    }

    // ── AsReadOnlySpan 长度正确 ───────────────────────────────────────────────

    [Fact]
    public void AsReadOnlySpan_InlineBytes4_HasCorrectLength()
    {
        InlineBytes4 buf = default;
        Assert.Equal(InlineBytes4.Length, buf.AsReadOnlySpan().Length);
    }

    [Fact]
    public void AsReadOnlySpan_InlineBytes64_HasCorrectLength()
    {
        InlineBytes64 buf = default;
        Assert.Equal(InlineBytes64.Length, buf.AsReadOnlySpan().Length);
    }
}
