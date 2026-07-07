namespace SonnetDB.Query;

/// <summary>
/// 聚合查询的单桶结果。
/// </summary>
/// <param name="BucketStart">桶起点时间戳（毫秒，TimeBucket.Floor 对齐后的值）。</param>
/// <param name="BucketEndExclusive">桶终点时间戳（毫秒，不含）。</param>
/// <param name="Count">桶内数据点数量。</param>
/// <param name="Value">聚合后的值；当聚合函数为 Count 时，Value == (double)Count。</param>
public sealed record AggregateBucket(
    long BucketStart,
    long BucketEndExclusive,
    long Count,
    double Value);
