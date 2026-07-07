using System.Runtime.InteropServices;

namespace SonnetDB.Storage.Format;

/// <summary>
/// WAL 文件尾部的可选 LastLsn 元数据（固定 32 字节）。
/// <para>
/// 新 WAL 会在文件尾写入该 footer，用于让 <c>WalWriter.Open</c> 快速获知最后一条
/// 记录的 LSN 和记录区结尾偏移；旧 WAL 没有该 footer 时仍可按记录扫描路径打开。
/// </para>
/// <para>
/// 二进制布局（little-endian）：
/// <code>
/// Offset  Size  Field
/// 0       4     Magic             (0x57414C46，= "WALF" big-endian)
/// 4       2     Version           (当前 = 1)
/// 6       2     FooterSize        (= 32)
/// 8       8     LastLsn           (最后一条合法记录的 LSN；空 WAL 为 FirstLsn - 1)
/// 16      8     RecordsEndOffset  (文件头 + 全部记录之后的偏移)
/// 24      4     Reserved          (填 0)
/// 28      4     Crc32             (覆盖 offset 0..27)
/// ─────────────────────────────────
/// Total  32
/// </code>
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WalLastLsnFooter
{
    /// <summary>WAL LastLsn footer magic（固定值 0x57414C46）。</summary>
    public uint Magic;

    /// <summary>footer 格式版本号（当前 = <see cref="VersionValue"/>）。</summary>
    public ushort Version;

    /// <summary>本 footer 的字节大小（= <see cref="FormatSizes.WalLastLsnFooterSize"/>）。</summary>
    public ushort FooterSize;

    /// <summary>最后一条合法 WAL 记录的 LSN；空 WAL 为 FirstLsn - 1。</summary>
    public long LastLsn;

    /// <summary>WAL 记录区结尾偏移，即文件头和所有记录之后的位置。</summary>
    public long RecordsEndOffset;

    /// <summary>保留字段（填 0）。</summary>
    public uint Reserved;

    /// <summary>footer CRC32 校验值，覆盖本结构 offset 0..27。</summary>
    public uint Crc32;

    /// <summary>WAL LastLsn footer magic 常量（0x57414C46）。</summary>
    public const uint MagicValue = 0x57414C46u;

    /// <summary>WAL LastLsn footer 格式版本常量。</summary>
    public const ushort VersionValue = 1;

    /// <summary>参与 CRC32 计算的前缀长度（不包含 <see cref="Crc32"/> 字段）。</summary>
    public const int CrcCoveredLength = 28;

    /// <summary>
    /// 创建一个新的 <see cref="WalLastLsnFooter"/>，CRC 字段由调用方在序列化后回填。
    /// </summary>
    /// <param name="lastLsn">最后一条合法 WAL 记录的 LSN。</param>
    /// <param name="recordsEndOffset">WAL 记录区结尾偏移。</param>
    /// <returns>已初始化且 CRC 为 0 的 <see cref="WalLastLsnFooter"/> 实例。</returns>
    public static WalLastLsnFooter CreateNew(long lastLsn, long recordsEndOffset)
    {
        WalLastLsnFooter footer = default;
        footer.Magic = MagicValue;
        footer.Version = VersionValue;
        footer.FooterSize = FormatSizes.WalLastLsnFooterSize;
        footer.LastLsn = lastLsn;
        footer.RecordsEndOffset = recordsEndOffset;
        return footer;
    }

    /// <summary>
    /// 校验 footer 的固定字段是否匹配当前格式。
    /// </summary>
    /// <returns>固定字段有效返回 <c>true</c>，否则返回 <c>false</c>。</returns>
    public readonly bool IsShapeValid() =>
        Magic == MagicValue &&
        Version == VersionValue &&
        FooterSize == FormatSizes.WalLastLsnFooterSize;
}
