using System.IO.Hashing;
using System.Runtime.InteropServices;

namespace SonnetDB.Storage.Format;

/// <summary>
/// SonnetDB WAL 日志中每条记录的头部（固定 32 字节）。
/// <para>
/// WAL 文件布局：每条记录 = WalRecordHeader + Payload（PayloadLength 字节）。
/// </para>
/// <para>
/// 二进制布局（little-endian）：
/// <code>
/// Offset  Size  Field
/// 0       4     Magic           (0x57414C52，= "WALR" big-endian)
/// 4       1     RecordType      (<see cref="WalRecordType"/>)
/// 5       1     Flags           (bit 0 表示 Reserved 存放 header checksum)
/// 6       2     Reserved        (旧记录填 0；新记录存放 header checksum)
/// 8       4     PayloadLength   (载荷字节数，不含本头部)
/// 12      4     PayloadCrc32    (载荷 CRC32 校验值)
/// 16      8     Timestamp       (UTC Ticks，写入时刻)
/// 24      8     Lsn             (单调递增序列号)
/// ─────────────────────────────────
/// Total  32
/// </code>
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WalRecordHeader
{
    internal const byte HeaderChecksumFlag = 0x01;
    internal const byte KnownFlags = HeaderChecksumFlag;
    internal const int HeaderChecksumOffset = 6;
    internal const int HeaderChecksumLength = sizeof(ushort);
    internal const int HeaderSize = 32;

    /// <summary>WAL 记录 magic（固定值 0x57414C52）。</summary>
    public uint Magic;

    /// <summary>WAL 记录类型（见 <see cref="WalRecordType"/>）。</summary>
    public WalRecordType RecordType;

    /// <summary>标志位；bit 0 表示 <see cref="Reserved"/> 存放 header checksum。</summary>
    public byte Flags;

    /// <summary>旧记录保留字段填 0；新记录在 bit 0 启用时存放 header checksum。</summary>
    public ushort Reserved;

    /// <summary>载荷字节数（不含本头部自身）。</summary>
    public int PayloadLength;

    /// <summary>载荷 CRC32 校验值。</summary>
    public uint PayloadCrc32;

    /// <summary>记录写入时刻（UTC Ticks）。</summary>
    public long Timestamp;

    /// <summary>日志序列号（单调递增，从 1 开始）。</summary>
    public long Lsn;

    /// <summary>WAL 记录 magic 常量（0x57414C52）。</summary>
    public const uint MagicValue = 0x57414C52u;

    /// <summary>
    /// 创建一个新的 <see cref="WalRecordHeader"/>，填写关键字段。
    /// </summary>
    /// <param name="recordType">记录类型。</param>
    /// <param name="payloadLength">载荷字节数。</param>
    /// <param name="payloadCrc32">载荷 CRC32 校验值。</param>
    /// <param name="timestampUtcTicks">写入时刻（UTC Ticks）。</param>
    /// <param name="lsn">日志序列号。</param>
    /// <returns>已初始化的 <see cref="WalRecordHeader"/> 实例。</returns>
    public static WalRecordHeader CreateNew(
        WalRecordType recordType,
        int payloadLength,
        uint payloadCrc32,
        long timestampUtcTicks,
        long lsn)
    {
        WalRecordHeader h = default;
        h.Magic = MagicValue;
        h.RecordType = recordType;
        h.PayloadLength = payloadLength;
        h.PayloadCrc32 = payloadCrc32;
        h.Timestamp = timestampUtcTicks;
        h.Lsn = lsn;
        return h;
    }

    /// <summary>
    /// 检查 magic 是否合法。
    /// </summary>
    /// <returns>magic 等于 <see cref="MagicValue"/> 时返回 <c>true</c>。</returns>
    public readonly bool IsMagicValid() => Magic == MagicValue;

    internal readonly bool HasHeaderChecksum => (Flags & HeaderChecksumFlag) != 0;

    internal readonly bool HasUnsupportedFlags => (Flags & ~KnownFlags) != 0;

    internal readonly bool HasLegacyReservedShape => Flags == 0 && Reserved == 0;

    internal static ushort ComputeHeaderChecksum(ReadOnlySpan<byte> headerBytes)
    {
        if (headerBytes.Length < HeaderSize)
            throw new ArgumentException("WAL record header buffer is too small.", nameof(headerBytes));

        Span<byte> checksumBytes = stackalloc byte[HeaderSize];
        headerBytes[..HeaderSize].CopyTo(checksumBytes);
        checksumBytes.Slice(HeaderChecksumOffset, HeaderChecksumLength).Clear();

        uint crc32 = Crc32.HashToUInt32(checksumBytes);
        return (ushort)(crc32 ^ (crc32 >> 16));
    }

    internal readonly bool IsShapeValid(ReadOnlySpan<byte> headerBytes)
    {
        if (headerBytes.Length < HeaderSize)
            return false;

        if (!IsMagicValid())
            return false;

        if (HasUnsupportedFlags)
            return false;

        if (!HasHeaderChecksum)
            return HasLegacyReservedShape;

        return ComputeHeaderChecksum(headerBytes) == Reserved;
    }
}
