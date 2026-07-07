using SonnetDB.Model;

namespace SonnetDB.Query;

/// <summary>
/// 原始数据点查询：返回 [Range.FromInclusive, Range.ToInclusive] 内指定 (series, field) 的 DataPoint 流。
/// </summary>
/// <param name="SeriesId">目标序列的唯一标识符（XxHash64 值）。</param>
/// <param name="FieldName">目标字段名称。</param>
/// <param name="Range">查询时间范围（闭区间）。</param>
/// <param name="Limit">最多返回的数据点数量；null 表示不限制。</param>
public sealed record PointQuery(
    ulong SeriesId,
    string FieldName,
    TimeRange Range,
    int? Limit = null,
    GeoPointFilter? GeoFilter = null);

/// <summary>
/// GEOPOINT 字段查询的空间剪枝条件。
/// </summary>
/// <param name="HashMin">查询外包框的最小 32-bit geohash 前缀。</param>
/// <param name="HashMax">查询外包框的最大 32-bit geohash 前缀。</param>
public sealed record GeoPointFilter(uint HashMin, uint HashMax);
