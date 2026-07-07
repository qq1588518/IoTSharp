using System.Buffers;
using System.IO.Hashing;
using System.Text;

namespace SonnetDB.Model;

/// <summary>
/// 序列 ID 计算工具：将 <see cref="SeriesKey"/> 的规范化字符串通过
/// <c>XxHash64</c> 折叠为 <c>ulong</c>，作为引擎内 MemTable / Block / Index 的主键。
/// </summary>
public static class SeriesId
{
    /// <summary>
    /// 使用 <c>stackalloc</c> 的 UTF-8 字节数上限阈值（512B）。
    /// 典型的序列键远小于此值；超出时改从 <see cref="ArrayPool{T}"/> 租用缓冲区，
    /// 避免大字符串占用过多栈空间（默认栈大小约 1 MB）。
    /// </summary>
    private const int _stackAllocThreshold = 512;

    /// <summary>
    /// 计算 <paramref name="key"/> 的 <c>XxHash64</c> 哈希值。
    /// </summary>
    /// <param name="key">规范化序列键。</param>
    /// <returns>对应的 64 位无符号整数 ID。</returns>
    /// <remarks>
    /// 内部将 <see cref="SeriesKey.Canonical"/> 编码为 UTF-8 字节，
    /// 然后调用 <see cref="XxHash64.HashToUInt64(ReadOnlySpan{byte})"/>。
    /// 小于等于 512 字节时使用 <c>stackalloc</c>，
    /// 否则从 <see cref="ArrayPool{T}"/> 租用缓冲区，全程无托管堆分配。
    /// </remarks>
    public static ulong Compute(SeriesKey key) => ComputeFromCanonical(key.Canonical);

    /// <summary>
    /// 直接从规范化字符串计算 <c>XxHash64</c> 哈希值。
    /// </summary>
    /// <param name="canonical">序列键的规范化字符串。</param>
    /// <returns>对应的 64 位无符号整数 ID。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="canonical"/> 为 null。</exception>
    public static ulong ComputeFromCanonical(string canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);

        int maxBytes = Encoding.UTF8.GetMaxByteCount(canonical.Length);

        if (maxBytes <= _stackAllocThreshold)
        {
            Span<byte> stackBuffer = stackalloc byte[maxBytes];
            int written = Encoding.UTF8.GetBytes(canonical, stackBuffer);
            return XxHash64.HashToUInt64(stackBuffer[..written]);
        }

        byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(maxBytes);
        try
        {
            int written = Encoding.UTF8.GetBytes(canonical, rentedBuffer);
            return XxHash64.HashToUInt64(rentedBuffer.AsSpan(0, written));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }
}
