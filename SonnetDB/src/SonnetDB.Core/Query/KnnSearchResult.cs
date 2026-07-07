namespace SonnetDB.Query;

/// <summary>
/// KNN 搜索的单条最近邻结果。
/// </summary>
/// <param name="Timestamp">候选点的时间戳（Unix 毫秒）。</param>
/// <param name="SeriesId">候选点所属序列 ID（XxHash64 值）。</param>
/// <param name="Distance">与查询向量的距离（越小越近；具体含义取决于 <see cref="KnnMetric"/>）。</param>
internal sealed record KnnSearchResult(long Timestamp, ulong SeriesId, double Distance);
