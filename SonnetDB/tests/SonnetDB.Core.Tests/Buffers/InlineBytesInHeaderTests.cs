using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SonnetDB.Buffers;
using SonnetDB.IO;
using Xunit;

namespace SonnetDB.Core.Tests.Buffers;

/// <summary>
/// 模拟真实 header struct 场景的集成测试，验证 <see cref="InlineBytes8"/> / <see cref="InlineBytes16"/>
/// 与 <see cref="SpanWriter"/> / <see cref="SpanReader"/> 的 round-trip 兼容性。
/// </summary>
public sealed class InlineBytesInHeaderTests
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct DemoFileHeader
    {
        public InlineBytes8 Magic;
        public int Version;
        public int HeaderSize;
        public long CreatedAtUtcTicks;
        public InlineBytes16 Reserved;
    }

    // ── 结构体尺寸验证 ────────────────────────────────────────────────────────

    [Fact]
    public void DemoFileHeader_SizeOf_Equals40()
    {
        // InlineBytes8(8) + int(4) + int(4) + long(8) + InlineBytes16(16) = 40
        Assert.Equal(40, Unsafe.SizeOf<DemoFileHeader>());
    }

    // ── SpanWriter / SpanReader round-trip ────────────────────────────────────

    [Fact]
    public void DemoFileHeader_WriteStruct_ReadStruct_RoundTrip_AllFieldsEqual()
    {
        DemoFileHeader original = default;
        TsdbMagic.File.CopyTo(original.Magic.AsSpan());
        original.Version = TsdbMagic.FormatVersion;
        original.HeaderSize = Unsafe.SizeOf<DemoFileHeader>();
        original.CreatedAtUtcTicks = 638_000_000_000_000_000L;
        original.Reserved.AsSpan().Fill(0xEE);

        Span<byte> buf = stackalloc byte[Unsafe.SizeOf<DemoFileHeader>()];
        var writer = new SpanWriter(buf);
        writer.WriteStruct(in original);

        var reader = new SpanReader(buf);
        DemoFileHeader restored = reader.ReadStruct<DemoFileHeader>();

        Assert.True(
            original.Magic.AsReadOnlySpan().SequenceEqual(restored.Magic.AsReadOnlySpan()),
            "Magic field mismatch");
        Assert.Equal(original.Version, restored.Version);
        Assert.Equal(original.HeaderSize, restored.HeaderSize);
        Assert.Equal(original.CreatedAtUtcTicks, restored.CreatedAtUtcTicks);
        Assert.True(
            original.Reserved.AsReadOnlySpan().SequenceEqual(restored.Reserved.AsReadOnlySpan()),
            "Reserved field mismatch");
    }

    // ── 写入字节流前 8 字节等于 File magic ────────────────────────────────────

    [Fact]
    public void DemoFileHeader_WrittenBytes_First8Bytes_EqualFileMagic()
    {
        DemoFileHeader header = default;
        TsdbMagic.File.CopyTo(header.Magic.AsSpan());

        Span<byte> buf = stackalloc byte[Unsafe.SizeOf<DemoFileHeader>()];
        var writer = new SpanWriter(buf);
        writer.WriteStruct(in header);

        Assert.True(buf[..8].SequenceEqual(TsdbMagic.File));
    }

    // ── Magic round-trip via SpanWriter / SpanReader ──────────────────────────

    [Fact]
    public void InlineBytes8_Magic_WriteStruct_ReadStruct_RoundTrip()
    {
        InlineBytes8 original = TsdbMagic.CreateFileMagic();

        Span<byte> buf = stackalloc byte[InlineBytes8.Length];
        var writer = new SpanWriter(buf);
        writer.WriteStruct(in original);

        var reader = new SpanReader(buf);
        InlineBytes8 restored = reader.ReadStruct<InlineBytes8>();

        Assert.True(original.AsReadOnlySpan().SequenceEqual(restored.AsReadOnlySpan()));
    }
}
