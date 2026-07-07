using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SonnetDB.Buffers;

namespace SonnetDB.Storage.Format;

/// <summary>
/// SonnetDB 段文件头（固定 64 字节）。
/// <para>
/// 二进制布局（little-endian）：
/// <code>
/// Offset  Size  Field
/// 0       8     Magic              ("SDBSEGv1")
/// 8       4     FormatVersion      (当前 = SegmentFormatVersion)
/// 12      4     HeaderSize         (= 64)
/// 16      8     SegmentId          (段唯一标识，单调递增)
/// 24      8     CreatedAtUtcTicks
/// 32      4     BlockCount         (预留，写入时填 0，Flush 后更新)
/// 36      4     Reserved0         (v6: mini-footer magic "SMF1")
/// 40      16    Reserved16        (v6: IndexCount, IndexCrc32, IndexOffset)
/// 56      8     Reserved8         (v6: FileLength)
/// ─────────────────────────────────
/// Total  64
/// </code>
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SegmentHeader
{
    internal const uint FooterMiniCopyMagic = 0x31464D53u; // "SMF1" little-endian

    /// <summary>段文件 magic（"SDBSEGv1"，8 字节）。</summary>
    public InlineBytes8 Magic;

    /// <summary>段文件格式版本号（当前 = <see cref="TsdbMagic.SegmentFormatVersion"/>）。</summary>
    public int FormatVersion;

    /// <summary>本头部的字节大小（= 64）。</summary>
    public int HeaderSize;

    /// <summary>段唯一标识（单调递增 ID）。</summary>
    public long SegmentId;

    /// <summary>段文件创建时间（UTC Ticks）。</summary>
    public long CreatedAtUtcTicks;

    /// <summary>本段包含的 Block 数量（预留，Flush 后回填）。</summary>
    public int BlockCount;

    /// <summary>保留字段；v6 起写入 mini-footer magic。</summary>
    public int Reserved0;

    /// <summary>保留字节；v6 起写入 IndexCount、IndexCrc32 与 IndexOffset 摘要。</summary>
    public InlineBytes16 Reserved16;

    /// <summary>保留字节；v6 起写入 FileLength 摘要。</summary>
    public InlineBytes8 Reserved8;

    /// <summary>
    /// 创建一个新的 <see cref="SegmentHeader"/>，自动填写 magic、版本号、当前 UTC 时间。
    /// </summary>
    /// <param name="segmentId">段唯一标识符。</param>
    /// <returns>已初始化的 <see cref="SegmentHeader"/> 实例。</returns>
    public static SegmentHeader CreateNew(long segmentId)
    {
        SegmentHeader h = default;
        TsdbMagic.Segment.CopyTo(h.Magic.AsSpan());
        h.FormatVersion = TsdbMagic.SegmentFormatVersion;
        h.HeaderSize = Unsafe.SizeOf<SegmentHeader>();
        h.SegmentId = segmentId;
        h.CreatedAtUtcTicks = DateTime.UtcNow.Ticks;
        return h;
    }

    /// <summary>
    /// 校验段文件头是否有效（magic 一致且版本匹配当前写入版本）。
    /// </summary>
    /// <returns>有效返回 <c>true</c>，否则返回 <c>false</c>。</returns>
    public readonly bool IsValid() =>
        Magic.AsReadOnlySpan().SequenceEqual(TsdbMagic.Segment) &&
        FormatVersion == TsdbMagic.SegmentFormatVersion;

    /// <summary>
    /// 读取兼容性校验（magic 一致且版本属于
    /// <see cref="TsdbMagic.SupportedSegmentFormatVersions"/>）。
    /// </summary>
    /// <returns>段文件可被当前 SonnetDB 读取时返回 <c>true</c>。</returns>
    public readonly bool IsCompatibleForRead()
    {
        if (!Magic.AsReadOnlySpan().SequenceEqual(TsdbMagic.Segment))
            return false;
        ReadOnlySpan<int> supported = TsdbMagic.SupportedSegmentFormatVersions;
        for (int i = 0; i < supported.Length; i++)
        {
            if (supported[i] == FormatVersion)
                return true;
        }
        return false;
    }

    internal void WriteFooterMiniCopy(int indexCount, long indexOffset, long fileLength, uint indexCrc32)
    {
        Reserved0 = unchecked((int)FooterMiniCopyMagic);

        Span<byte> reserved16 = Reserved16.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(reserved16[..4], indexCount);
        BinaryPrimitives.WriteUInt32LittleEndian(reserved16.Slice(4, 4), indexCrc32);
        BinaryPrimitives.WriteInt64LittleEndian(reserved16.Slice(8, 8), indexOffset);
        BinaryPrimitives.WriteInt64LittleEndian(Reserved8.AsSpan(), fileLength);
    }

    internal readonly bool TryReadFooterMiniCopy(out SegmentFooterMiniCopy copy)
    {
        copy = default;
        if (FormatVersion < 6 || Reserved0 != unchecked((int)FooterMiniCopyMagic))
            return false;

        ReadOnlySpan<byte> reserved16 = Reserved16.AsReadOnlySpan();
        int indexCount = BinaryPrimitives.ReadInt32LittleEndian(reserved16[..4]);
        uint indexCrc32 = BinaryPrimitives.ReadUInt32LittleEndian(reserved16.Slice(4, 4));
        long indexOffset = BinaryPrimitives.ReadInt64LittleEndian(reserved16.Slice(8, 8));
        long fileLength = BinaryPrimitives.ReadInt64LittleEndian(Reserved8.AsReadOnlySpan());

        copy = new SegmentFooterMiniCopy(indexCount, indexOffset, fileLength, indexCrc32);
        return copy.IsShapeValid();
    }
}
