using SonnetDB.Storage.Format;

namespace SonnetDB.Storage.Segments;

/// <summary>
/// 对外只读地描述段文件内一个 Block 的元数据与物理位置。
/// 不承诺解析 payload；实际 payload 数据通过 <see cref="SegmentReader.ReadBlock"/> 按需获取。
/// </summary>
public readonly struct BlockDescriptor
{
    /// <summary>在段文件内的序号（[0, BlockCount)）。</summary>
    public int Index { get; init; }

    /// <summary>所属序列的唯一 ID（XxHash64 值）。</summary>
    public ulong SeriesId { get; init; }

    /// <summary>本 Block 内最小时间戳（毫秒 UTC）。</summary>
    public long MinTimestamp { get; init; }

    /// <summary>本 Block 内最大时间戳（毫秒 UTC）。</summary>
    public long MaxTimestamp { get; init; }

    /// <summary>本 Block 包含的数据点数量。</summary>
    public int Count { get; init; }

    /// <summary>字段数据类型。</summary>
    public FieldType FieldType { get; init; }

    /// <summary>时间戳载荷的编码方式。</summary>
    public BlockEncoding TimestampEncoding { get; init; }

    /// <summary>值载荷的编码方式。</summary>
    public BlockEncoding ValueEncoding { get; init; }

    /// <summary>已解码的 UTF-8 字段名称。</summary>
    public string FieldName { get; init; }

    /// <summary>BlockHeader 在文件中的字节偏移（文件起始 = 0）。</summary>
    public long FileOffset { get; init; }

    /// <summary>Block 总字节数（BlockHeader + FieldNameUtf8 + TsPayload + ValPayload）。</summary>
    public int BlockLength { get; init; }

    /// <summary>BlockHeader 中记录的 CRC32 校验值。</summary>
    public uint Crc32 { get; init; }

    /// <summary>是否持久化了可信的 Sum/Count 元数据（对应 BlockHeader.HasSumCount 标记）。</summary>
    public bool HasAggregateSumCount { get; init; }

    /// <summary>是否持久化了无损的 Min/Max 元数据（对应 BlockHeader.HasMinMax 标记）。</summary>
    public bool HasAggregateMinMax { get; init; }

    /// <summary>是否持久化了任意聚合元数据（兼容旧字段名，等价于 <see cref="HasAggregateSumCount"/> 或 <see cref="HasAggregateMinMax"/> 之一为真）。</summary>
    public bool HasAggregateMetadata => HasAggregateSumCount || HasAggregateMinMax;

    /// <summary>数值聚合的 Sum（仅当 <see cref="HasAggregateSumCount"/> 为真时有效）。</summary>
    public double AggregateSum { get; init; }

    /// <summary>数值聚合的 Min（仅当 <see cref="HasAggregateMinMax"/> 为真时有效）。</summary>
    public double AggregateMin { get; init; }

    /// <summary>数值聚合的 Max（仅当 <see cref="HasAggregateMinMax"/> 为真时有效）。</summary>
    public double AggregateMax { get; init; }

    /// <summary>是否持久化了 GEOPOINT geohash 范围元数据。</summary>
    public bool HasGeoHashRange => GeoHashMin != 0 || GeoHashMax != 0;

    /// <summary>GEOPOINT 块内最小 32-bit geohash 前缀。</summary>
    public uint GeoHashMin { get; init; }

    /// <summary>GEOPOINT 块内最大 32-bit geohash 前缀。</summary>
    public uint GeoHashMax { get; init; }

    /// <summary>字段名 UTF-8 编码的字节数（跟在 BlockHeader 之后）。</summary>
    internal int FieldNameUtf8Length { get; init; }

    /// <summary>BlockHeader 实际字节数；v2-v4 为 72，v5 起为 80。</summary>
    internal int HeaderSize { get; init; }

    /// <summary>时间戳载荷字节数（跟在 FieldNameUtf8 之后）。</summary>
    internal int TimestampPayloadLength { get; init; }

    /// <summary>值载荷字节数（跟在 TimestampPayload 之后）。</summary>
    internal int ValuePayloadLength { get; init; }
}
