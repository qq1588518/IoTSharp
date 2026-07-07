using SonnetDB.Storage.Segments;

namespace SonnetDB.Engine.Compaction;

/// <summary>
/// 从当前段集合产出 0..N 个 <see cref="CompactionPlan"/>，使用 Size-Tiered 策略。
/// <para>无副作用：不修改任何状态，仅做计算。</para>
/// <para>
/// Tier 划分公式：
/// <c>tierIndex = max(0, floor(log_TierSizeRatio(fileLength / FirstTierMaxBytes)) + 1)</c>；
/// 文件小于 <c>FirstTierMaxBytes</c> 时归入 tier 0。
/// </para>
/// </summary>
public static class CompactionPlanner
{
    /// <summary>
    /// 根据当前 <paramref name="readers"/> 快照及 <paramref name="policy"/>，
    /// 产出需要执行的 <see cref="CompactionPlan"/> 列表。
    /// </summary>
    /// <param name="readers">当前所有已打开的 <see cref="SegmentReader"/> 快照。</param>
    /// <param name="policy">Compaction 触发策略。</param>
    /// <returns>按 tier 分组生成的 CompactionPlan 列表（可能为空）。</returns>
    /// <exception cref="ArgumentNullException">任何参数为 null 时抛出。</exception>
    public static IReadOnlyList<CompactionPlan> Plan(
        IReadOnlyList<SegmentReader> readers,
        CompactionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(readers);
        ArgumentNullException.ThrowIfNull(policy);

        if (!policy.Enabled || readers.Count == 0)
            return [];

        // 按 SegmentId 升序处理
        var sorted = new List<SegmentReader>(readers);
        sorted.Sort(static (a, b) => a.Header.SegmentId.CompareTo(b.Header.SegmentId));

        // 分 tier：tierIndex → List<SegmentId>
        var tiers = new Dictionary<int, List<long>>();
        foreach (var reader in sorted)
        {
            int tier = ComputeTier(reader.FileLength, policy);
            if (!tiers.TryGetValue(tier, out var list))
            {
                list = [];
                tiers[tier] = list;
            }
            list.Add(reader.Header.SegmentId);
        }

        // 每个 tier 内，每凑齐 MinTierSize 个产出一个 CompactionPlan
        var plans = new List<CompactionPlan>();
        foreach (var (tierIndex, segIds) in tiers)
        {
            int i = 0;
            while (i + policy.MinTierSize <= segIds.Count)
            {
                var batch = segIds.GetRange(i, policy.MinTierSize);
                plans.Add(new CompactionPlan(tierIndex, batch.AsReadOnly()));
                i += policy.MinTierSize;
            }
        }

        return plans.AsReadOnly();
    }

    /// <summary>
    /// 计算给定文件大小所在的 tier 索引。
    /// </summary>
    private static int ComputeTier(long fileLength, CompactionPolicy policy)
    {
        if (fileLength <= 0 || fileLength < policy.FirstTierMaxBytes)
            return 0;

        // tierIndex = floor(log_TierSizeRatio(fileLength / FirstTierMaxBytes)) + 1
        double ratio = (double)fileLength / policy.FirstTierMaxBytes;
        double log = Math.Log(ratio) / Math.Log(policy.TierSizeRatio);
        int tier = (int)Math.Floor(log) + 1;
        return Math.Max(0, tier);
    }
}
