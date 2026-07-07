namespace SonnetDB.Query;

/// <summary>
/// 聚合函数类型。第一版仅支持数值字段（Float64 / Int64 / Boolean）。
/// </summary>
public enum Aggregator : byte
{
    /// <summary>不聚合（用于原始点查询）。</summary>
    None = 0,

    /// <summary>计数：统计数据点总数。</summary>
    Count = 1,

    /// <summary>求和：累加所有数值。</summary>
    Sum = 2,

    /// <summary>最小值：取所有数值中的最小值。</summary>
    Min = 3,

    /// <summary>最大值：取所有数值中的最大值。</summary>
    Max = 4,

    /// <summary>平均值：所有数值之和除以数据点数量。</summary>
    Avg = 5,

    /// <summary>第一个值：按时间戳升序排列后的第一条数据点的值。</summary>
    First = 6,

    /// <summary>最后一个值：按时间戳升序排列后的最后一条数据点的值。</summary>
    Last = 7,
}
