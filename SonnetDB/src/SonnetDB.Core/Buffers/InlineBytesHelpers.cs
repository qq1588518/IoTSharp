using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SonnetDB.Buffers;

/// <summary>
/// 为任意 <c>unmanaged</c> 内联缓冲区提供常用辅助操作（全部 Safe-only）。
/// </summary>
public static class InlineBytesHelpers
{
    /// <summary>
    /// 比较内联缓冲区与给定字节序列是否完全相等。
    /// </summary>
    /// <typeparam name="TBuffer">unmanaged 结构体类型（通常为 InlineBytesN）。</typeparam>
    /// <param name="buffer">被比较的缓冲区（by readonly ref）。</param>
    /// <param name="other">目标字节序列。</param>
    /// <returns>字节内容完全相等时返回 <c>true</c>；否则返回 <c>false</c>。</returns>
    public static bool SequenceEqual<TBuffer>(in TBuffer buffer, ReadOnlySpan<byte> other)
        where TBuffer : unmanaged
    {
        ReadOnlySpan<byte> view = MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(in buffer, 1));
        return view.SequenceEqual(other);
    }

    /// <summary>
    /// 将给定字节序列复制到内联缓冲区，源长度必须严格等于缓冲区大小。
    /// </summary>
    /// <typeparam name="TBuffer">unmanaged 结构体类型（通常为 InlineBytesN）。</typeparam>
    /// <param name="buffer">目标缓冲区（by ref）。</param>
    /// <param name="source">要复制的字节序列。</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="source"/> 的长度与 <typeparamref name="TBuffer"/> 的大小不一致时抛出。
    /// </exception>
    public static void CopyFrom<TBuffer>(ref TBuffer buffer, ReadOnlySpan<byte> source)
        where TBuffer : unmanaged
    {
        int size = Unsafe.SizeOf<TBuffer>();
        if (source.Length != size)
            throw new ArgumentException($"Source length must be exactly {size} bytes.", nameof(source));

        Span<byte> dst = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref buffer, 1));
        source.CopyTo(dst);
    }
}
