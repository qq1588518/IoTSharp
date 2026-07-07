using System.Runtime.InteropServices;
using SonnetDB.Buffers;

namespace SonnetDB.Storage.Format;

/// <summary>
/// 段文件尾部索引中的一个索引条目（固定 48 字节）。
/// <para>
/// 索引条目数组位于 <see cref="SegmentFooter.IndexOffset"/> 偏移处，
/// 共 <see cref="SegmentFooter.IndexCount"/> 个条目，每个条目 48 字节。
/// </para>
/// <para>
/// 二进制布局（little-endian）：
/// <code>
/// Offset  Size  Field
/// 0       8     SeriesId        (序列唯一 ID)
/// 8       8     MinTimestamp    (本 Block 最小时间戳)
/// 16      8     MaxTimestamp    (本 Block 最大时间戳)
/// 24      8     FileOffset      (Block 在段文件中的字节偏移)
/// 32      4     BlockLength     (Block 总字节数，含头部与所有载荷)
/// 36      4     FieldNameHash   (字段名哈希，用于快速过滤)
/// 40      8     Reserved
/// ─────────────────────────────────
/// Total  48
/// </code>
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BlockIndexEntry
{
    /// <summary>序列唯一 ID（与 BlockHeader.SeriesId 一致）。</summary>
    public ulong SeriesId;

    /// <summary>本 Block 最小时间戳（毫秒 UTC）。</summary>
    public long MinTimestamp;

    /// <summary>本 Block 最大时间戳（毫秒 UTC）。</summary>
    public long MaxTimestamp;

    /// <summary>Block 在段文件中的字节偏移（从文件起始位置算起）。</summary>
    public long FileOffset;

    /// <summary>Block 总字节数（含 BlockHeader + FieldNameUtf8 + 时间戳载荷 + 值载荷）。</summary>
    public int BlockLength;

    /// <summary>字段名哈希（用于快速跳过不匹配的 Block，第一版填 0）。</summary>
    public int FieldNameHash;

    /// <summary>保留字节（全 0）。</summary>
    public InlineBytes8 Reserved;
}
