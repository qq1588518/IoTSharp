using System.IO.Hashing;

namespace SonnetDB.Storage.Segments;

/// <summary>
/// 字段名哈希工具：使用 XxHash32 将 UTF-8 编码的字段名折叠为 32 位整数，
/// 用于 <see cref="SonnetDB.Storage.Format.BlockIndexEntry.FieldNameHash"/> 快速过滤。
/// </summary>
internal static class FieldNameHash
{
    /// <summary>
    /// 计算 UTF-8 字节序列的 XxHash32 值，转换为 <see cref="int"/>。
    /// </summary>
    /// <param name="utf8">字段名的 UTF-8 编码字节序列。</param>
    /// <returns>XxHash32 哈希值（转 int，保留全部 32 位）。</returns>
    public static int Compute(ReadOnlySpan<byte> utf8)
        => (int)XxHash32.HashToUInt32(utf8);
}
