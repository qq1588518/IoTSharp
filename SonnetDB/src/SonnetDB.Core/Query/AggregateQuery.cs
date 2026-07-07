namespace SonnetDB.Query;

/// <summary>
/// 聚合查询：在时间桶内对单个字段做聚合。
/// <para><see cref="BucketSizeMs"/> &lt;= 0 表示全局聚合（单桶，覆盖整个 Range）。</para>
/// </summary>
/// <param name="SeriesId">目标序列的唯一标识符（XxHash64 值）。</param>
/// <param name="FieldName">目标字段名称。</param>
/// <param name="Range">查询时间范围（闭区间）。</param>
/// <param name="Aggregator">聚合函数类型。</param>
/// <param name="BucketSizeMs">桶的大小（毫秒）；&lt;= 0 表示全局单桶聚合。</param>
public sealed record AggregateQuery(
    ulong SeriesId,
    string FieldName,
    TimeRange Range,
    Aggregator Aggregator,
    long BucketSizeMs = 0);
