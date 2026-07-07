namespace SonnetDB.Storage.Format;

/// <summary>
/// 时序字段的数据类型。
/// </summary>
public enum FieldType : byte
{
    /// <summary>未知类型（占位，不应出现在有效数据中）。</summary>
    Unknown = 0,

    /// <summary>64 位双精度浮点数（IEEE 754）。</summary>
    Float64 = 1,

    /// <summary>64 位有符号整数。</summary>
    Int64 = 2,

    /// <summary>布尔值（0 = false，1 = true）。</summary>
    Boolean = 3,

    /// <summary>UTF-8 编码字符串（变长，仅 WAL 内使用）。</summary>
    String = 4,

    /// <summary>定长 32 位浮点向量（IEEE 754，dim 由 schema 声明；WAL 内按 dim×4 字节小端排布）。</summary>
    Vector = 5,

    /// <summary>WGS84 地理点（纬度 lat + 经度 lon，均为 64 位双精度浮点）。</summary>
    GeoPoint = 6,
}
