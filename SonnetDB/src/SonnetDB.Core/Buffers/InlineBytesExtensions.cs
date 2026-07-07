using System.Runtime.InteropServices;

namespace SonnetDB.Buffers;

/// <summary>
/// 为 <see cref="InlineBytes4"/>、<see cref="InlineBytes8"/>、<see cref="InlineBytes16"/>、
/// <see cref="InlineBytes24"/>、<see cref="InlineBytes32"/>、<see cref="InlineBytes64"/> 提供 Safe-only 的 Span 视图扩展方法。
/// </summary>
public static class InlineBytesExtensions
{
    // ── InlineBytes4 ──────────────────────────────────────────────────────────

    /// <summary>将 <see cref="InlineBytes4"/> 视图为可写 <see cref="Span{Byte}"/>。</summary>
    /// <param name="buffer">目标缓冲区（by ref）。</param>
    /// <returns>长度为 <see cref="InlineBytes4.Length"/> 的可写 Span。</returns>
    public static Span<byte> AsSpan(this ref InlineBytes4 buffer)
        => MemoryMarshal.CreateSpan(ref buffer[0], InlineBytes4.Length);

    /// <summary>将 <see cref="InlineBytes4"/> 视图为只读 <see cref="ReadOnlySpan{Byte}"/>。</summary>
    /// <param name="buffer">源缓冲区（by readonly ref）。</param>
    /// <returns>长度为 <see cref="InlineBytes4.Length"/> 的只读 Span。</returns>
    public static ReadOnlySpan<byte> AsReadOnlySpan(this in InlineBytes4 buffer)
        => MemoryMarshal.CreateReadOnlySpan(in buffer[0], InlineBytes4.Length);

    // ── InlineBytes8 ──────────────────────────────────────────────────────────

    /// <summary>将 <see cref="InlineBytes8"/> 视图为可写 <see cref="Span{Byte}"/>。</summary>
    /// <param name="buffer">目标缓冲区（by ref）。</param>
    /// <returns>长度为 <see cref="InlineBytes8.Length"/> 的可写 Span。</returns>
    public static Span<byte> AsSpan(this ref InlineBytes8 buffer)
        => MemoryMarshal.CreateSpan(ref buffer[0], InlineBytes8.Length);

    /// <summary>将 <see cref="InlineBytes8"/> 视图为只读 <see cref="ReadOnlySpan{Byte}"/>。</summary>
    /// <param name="buffer">源缓冲区（by readonly ref）。</param>
    /// <returns>长度为 <see cref="InlineBytes8.Length"/> 的只读 Span。</returns>
    public static ReadOnlySpan<byte> AsReadOnlySpan(this in InlineBytes8 buffer)
        => MemoryMarshal.CreateReadOnlySpan(in buffer[0], InlineBytes8.Length);

    // ── InlineBytes16 ─────────────────────────────────────────────────────────

    /// <summary>将 <see cref="InlineBytes16"/> 视图为可写 <see cref="Span{Byte}"/>。</summary>
    /// <param name="buffer">目标缓冲区（by ref）。</param>
    /// <returns>长度为 <see cref="InlineBytes16.Length"/> 的可写 Span。</returns>
    public static Span<byte> AsSpan(this ref InlineBytes16 buffer)
        => MemoryMarshal.CreateSpan(ref buffer[0], InlineBytes16.Length);

    /// <summary>将 <see cref="InlineBytes16"/> 视图为只读 <see cref="ReadOnlySpan{Byte}"/>。</summary>
    /// <param name="buffer">源缓冲区（by readonly ref）。</param>
    /// <returns>长度为 <see cref="InlineBytes16.Length"/> 的只读 Span。</returns>
    public static ReadOnlySpan<byte> AsReadOnlySpan(this in InlineBytes16 buffer)
        => MemoryMarshal.CreateReadOnlySpan(in buffer[0], InlineBytes16.Length);

    // ── InlineBytes24 ─────────────────────────────────────────────────────────

    /// <summary>将 <see cref="InlineBytes24"/> 视图为可写 <see cref="Span{Byte}"/>。</summary>
    /// <param name="buffer">目标缓冲区（by ref）。</param>
    /// <returns>长度为 <see cref="InlineBytes24.Length"/> 的可写 Span。</returns>
    public static Span<byte> AsSpan(this ref InlineBytes24 buffer)
        => MemoryMarshal.CreateSpan(ref buffer[0], InlineBytes24.Length);

    /// <summary>将 <see cref="InlineBytes24"/> 视图为只读 <see cref="ReadOnlySpan{Byte}"/>。</summary>
    /// <param name="buffer">源缓冲区（by readonly ref）。</param>
    /// <returns>长度为 <see cref="InlineBytes24.Length"/> 的只读 Span。</returns>
    public static ReadOnlySpan<byte> AsReadOnlySpan(this in InlineBytes24 buffer)
        => MemoryMarshal.CreateReadOnlySpan(in buffer[0], InlineBytes24.Length);

    // ── InlineBytes32 ─────────────────────────────────────────────────────────

    /// <summary>将 <see cref="InlineBytes32"/> 视图为可写 <see cref="Span{Byte}"/>。</summary>
    /// <param name="buffer">目标缓冲区（by ref）。</param>
    /// <returns>长度为 <see cref="InlineBytes32.Length"/> 的可写 Span。</returns>
    public static Span<byte> AsSpan(this ref InlineBytes32 buffer)
        => MemoryMarshal.CreateSpan(ref buffer[0], InlineBytes32.Length);

    /// <summary>将 <see cref="InlineBytes32"/> 视图为只读 <see cref="ReadOnlySpan{Byte}"/>。</summary>
    /// <param name="buffer">源缓冲区（by readonly ref）。</param>
    /// <returns>长度为 <see cref="InlineBytes32.Length"/> 的只读 Span。</returns>
    public static ReadOnlySpan<byte> AsReadOnlySpan(this in InlineBytes32 buffer)
        => MemoryMarshal.CreateReadOnlySpan(in buffer[0], InlineBytes32.Length);

    // ── InlineBytes64 ─────────────────────────────────────────────────────────

    /// <summary>将 <see cref="InlineBytes64"/> 视图为可写 <see cref="Span{Byte}"/>。</summary>
    /// <param name="buffer">目标缓冲区（by ref）。</param>
    /// <returns>长度为 <see cref="InlineBytes64.Length"/> 的可写 Span。</returns>
    public static Span<byte> AsSpan(this ref InlineBytes64 buffer)
        => MemoryMarshal.CreateSpan(ref buffer[0], InlineBytes64.Length);

    /// <summary>将 <see cref="InlineBytes64"/> 视图为只读 <see cref="ReadOnlySpan{Byte}"/>。</summary>
    /// <param name="buffer">源缓冲区（by readonly ref）。</param>
    /// <returns>长度为 <see cref="InlineBytes64.Length"/> 的只读 Span。</returns>
    public static ReadOnlySpan<byte> AsReadOnlySpan(this in InlineBytes64 buffer)
        => MemoryMarshal.CreateReadOnlySpan(in buffer[0], InlineBytes64.Length);
}
