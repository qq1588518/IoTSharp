using System.Runtime.CompilerServices;
using SonnetDB.Buffers;
using Xunit;

namespace SonnetDB.Core.Tests.Buffers;

/// <summary>
/// <see cref="InlineBytes4"/>、<see cref="InlineBytes8"/>、<see cref="InlineBytes16"/>、
/// <see cref="InlineBytes32"/>、<see cref="InlineBytes64"/> 基础行为测试。
/// </summary>
public sealed class InlineBytesTests
{
    // ── SizeOf 与 Length 一致 ─────────────────────────────────────────────────

    [Fact]
    public void SizeOf_InlineBytes4_EqualsLengthConstant()
        => Assert.Equal(InlineBytes4.Length, Unsafe.SizeOf<InlineBytes4>());

    [Fact]
    public void SizeOf_InlineBytes8_EqualsLengthConstant()
        => Assert.Equal(InlineBytes8.Length, Unsafe.SizeOf<InlineBytes8>());

    [Fact]
    public void SizeOf_InlineBytes16_EqualsLengthConstant()
        => Assert.Equal(InlineBytes16.Length, Unsafe.SizeOf<InlineBytes16>());

    [Fact]
    public void SizeOf_InlineBytes32_EqualsLengthConstant()
        => Assert.Equal(InlineBytes32.Length, Unsafe.SizeOf<InlineBytes32>());

    [Fact]
    public void SizeOf_InlineBytes64_EqualsLengthConstant()
        => Assert.Equal(InlineBytes64.Length, Unsafe.SizeOf<InlineBytes64>());

    // ── AsSpan 长度 ───────────────────────────────────────────────────────────

    [Fact]
    public void AsSpan_InlineBytes4_HasCorrectLength()
    {
        InlineBytes4 buf = default;
        Assert.Equal(InlineBytes4.Length, buf.AsSpan().Length);
    }

    [Fact]
    public void AsSpan_InlineBytes8_HasCorrectLength()
    {
        InlineBytes8 buf = default;
        Assert.Equal(InlineBytes8.Length, buf.AsSpan().Length);
    }

    [Fact]
    public void AsSpan_InlineBytes16_HasCorrectLength()
    {
        InlineBytes16 buf = default;
        Assert.Equal(InlineBytes16.Length, buf.AsSpan().Length);
    }

    [Fact]
    public void AsSpan_InlineBytes32_HasCorrectLength()
    {
        InlineBytes32 buf = default;
        Assert.Equal(InlineBytes32.Length, buf.AsSpan().Length);
    }

    [Fact]
    public void AsSpan_InlineBytes64_HasCorrectLength()
    {
        InlineBytes64 buf = default;
        Assert.Equal(InlineBytes64.Length, buf.AsSpan().Length);
    }

    // ── default 全零 ──────────────────────────────────────────────────────────

    [Fact]
    public void Default_InlineBytes4_IsAllZero()
    {
        InlineBytes4 buf = default;
        Assert.All(buf.AsSpan().ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Default_InlineBytes8_IsAllZero()
    {
        InlineBytes8 buf = default;
        Assert.All(buf.AsSpan().ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Default_InlineBytes16_IsAllZero()
    {
        InlineBytes16 buf = default;
        Assert.All(buf.AsSpan().ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Default_InlineBytes32_IsAllZero()
    {
        InlineBytes32 buf = default;
        Assert.All(buf.AsSpan().ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Default_InlineBytes64_IsAllZero()
    {
        InlineBytes64 buf = default;
        Assert.All(buf.AsSpan().ToArray(), b => Assert.Equal(0, b));
    }

    // ── 索引器写入/读取 ───────────────────────────────────────────────────────

    [Fact]
    public void Indexer_InlineBytes4_WriteRead_RoundTrip()
    {
        InlineBytes4 buf = default;
        buf[0] = 0xAA;
        buf[1] = 0xBB;
        buf[2] = 0xCC;
        buf[3] = 0xDD;

        Assert.Equal(0xAA, buf[0]);
        Assert.Equal(0xBB, buf[1]);
        Assert.Equal(0xCC, buf[2]);
        Assert.Equal(0xDD, buf[3]);
    }

    [Fact]
    public void Indexer_InlineBytes8_WriteRead_RoundTrip()
    {
        InlineBytes8 buf = default;
        for (int i = 0; i < InlineBytes8.Length; i++)
            buf[i] = (byte)(i + 1);

        for (int i = 0; i < InlineBytes8.Length; i++)
            Assert.Equal((byte)(i + 1), buf[i]);
    }

    // ── Span 写入/读取 ────────────────────────────────────────────────────────

    [Fact]
    public void Span_InlineBytes16_WriteRead_RoundTrip()
    {
        InlineBytes16 buf = default;
        Span<byte> span = buf.AsSpan();
        for (int i = 0; i < span.Length; i++)
            span[i] = (byte)(i * 2);

        for (int i = 0; i < InlineBytes16.Length; i++)
            Assert.Equal((byte)(i * 2), buf[i]);
    }
}
