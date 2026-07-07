using SonnetDB.Buffers;
using Xunit;

namespace SonnetDB.Core.Tests.Buffers;

/// <summary>
/// <see cref="InlineBytesHelpers"/> 泛型辅助方法测试。
/// </summary>
public sealed class InlineBytesHelpersTests
{
    // ── SequenceEqual ─────────────────────────────────────────────────────────

    [Fact]
    public void SequenceEqual_InlineBytes4_EqualContent_ReturnsTrue()
    {
        InlineBytes4 buf = default;
        byte[] data = [0x01, 0x02, 0x03, 0x04];
        data.CopyTo(buf.AsSpan());

        Assert.True(InlineBytesHelpers.SequenceEqual(in buf, data));
    }

    [Fact]
    public void SequenceEqual_InlineBytes4_DifferentContent_ReturnsFalse()
    {
        InlineBytes4 buf = default;
        byte[] data = [0x01, 0x02, 0x03, 0x04];
        data.CopyTo(buf.AsSpan());

        Assert.False(InlineBytesHelpers.SequenceEqual(in buf, [0x01, 0x02, 0x03, 0xFF]));
    }

    [Fact]
    public void SequenceEqual_InlineBytes4_WrongLength_ReturnsFalse()
    {
        InlineBytes4 buf = default;
        byte[] data = [0x01, 0x02, 0x03, 0x04];
        data.CopyTo(buf.AsSpan());

        Assert.False(InlineBytesHelpers.SequenceEqual(in buf, [0x01, 0x02, 0x03]));
    }

    [Fact]
    public void SequenceEqual_InlineBytes8_AllZero_ReturnsTrue()
    {
        InlineBytes8 buf = default;
        Assert.True(InlineBytesHelpers.SequenceEqual(in buf, new byte[InlineBytes8.Length]));
    }

    [Fact]
    public void SequenceEqual_InlineBytes16_EqualContent_ReturnsTrue()
    {
        InlineBytes16 buf = default;
        byte[] data = new byte[InlineBytes16.Length];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)i;
        data.CopyTo(buf.AsSpan());

        Assert.True(InlineBytesHelpers.SequenceEqual(in buf, data));
    }

    // ── CopyFrom ──────────────────────────────────────────────────────────────

    [Fact]
    public void CopyFrom_InlineBytes4_CorrectLength_CopiesData()
    {
        InlineBytes4 buf = default;
        byte[] src = [0xAA, 0xBB, 0xCC, 0xDD];
        InlineBytesHelpers.CopyFrom(ref buf, src);

        Assert.Equal(src, buf.AsReadOnlySpan().ToArray());
    }

    [Fact]
    public void CopyFrom_InlineBytes8_CorrectLength_CopiesData()
    {
        InlineBytes8 buf = default;
        byte[] src = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        InlineBytesHelpers.CopyFrom(ref buf, src);

        Assert.Equal(src, buf.AsReadOnlySpan().ToArray());
    }

    [Fact]
    public void CopyFrom_InlineBytes4_TooShortSource_ThrowsArgumentException()
    {
        InlineBytes4 buf = default;
        byte[] src = [0x01, 0x02, 0x03]; // 3 bytes, needs 4

        Assert.Throws<ArgumentException>(() => InlineBytesHelpers.CopyFrom(ref buf, src));
    }

    [Fact]
    public void CopyFrom_InlineBytes4_TooLongSource_ThrowsArgumentException()
    {
        InlineBytes4 buf = default;
        byte[] src = [0x01, 0x02, 0x03, 0x04, 0x05]; // 5 bytes, needs 4

        Assert.Throws<ArgumentException>(() => InlineBytesHelpers.CopyFrom(ref buf, src));
    }

    [Fact]
    public void CopyFrom_InlineBytes16_CorrectLength_CopiesData()
    {
        InlineBytes16 buf = default;
        byte[] src = new byte[InlineBytes16.Length];
        for (int i = 0; i < src.Length; i++)
            src[i] = (byte)(i * 7 + 3);
        InlineBytesHelpers.CopyFrom(ref buf, src);

        Assert.Equal(src, buf.AsReadOnlySpan().ToArray());
    }
}
