namespace SonnetDB.Engine.Retention;

/// <summary>一次 Retention 扫描的产物。无副作用。</summary>
/// <param name="Cutoff">timestamp &lt; Cutoff 的点视为过期。</param>
/// <param name="SegmentsToDrop">MaxTimestamp &lt; Cutoff 的整段段 ID 列表（可直接 Drop）。</param>
/// <param name="TombstonesToInject">部分过期段需注入的墓碑列表。</param>
public sealed record RetentionPlan(
    long Cutoff,
    IReadOnlyList<long> SegmentsToDrop,
    IReadOnlyList<TombstoneToInject> TombstonesToInject);

/// <summary>
/// 需要注入到 (SeriesId, FieldName) 上的单条墓碑描述。
/// </summary>
/// <param name="SeriesId">目标序列 ID。</param>
/// <param name="FieldName">目标字段名称。</param>
/// <param name="FromTimestamp">墓碑起始时间戳（通常为 <see cref="long.MinValue"/>）。</param>
/// <param name="ToTimestamp">墓碑结束时间戳（= Cutoff - 1，闭区间）。</param>
public readonly record struct TombstoneToInject(
    ulong SeriesId,
    string FieldName,
    long FromTimestamp,
    long ToTimestamp);
