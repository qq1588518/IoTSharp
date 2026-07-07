namespace SonnetDB.Engine.Compaction;

/// <summary>
/// 一次 Compaction 的输入计划：要合并的段集合 + 目标 tier 索引。
/// </summary>
/// <param name="TierIndex">所属 tier 的索引（0 为最小 tier）。</param>
/// <param name="SourceSegmentIds">要合并的段 ID 列表（按 SegmentId 升序）。</param>
public sealed record CompactionPlan(
    int TierIndex,
    IReadOnlyList<long> SourceSegmentIds);
