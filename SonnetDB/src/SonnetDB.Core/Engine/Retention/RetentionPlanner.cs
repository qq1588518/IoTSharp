using SonnetDB.Engine;
using SonnetDB.Storage.Segments;

namespace SonnetDB.Engine.Retention;

/// <summary>
/// 从当前段集合产出一个 <see cref="RetentionPlan"/>。
/// <para>无副作用、纯函数：不修改任何状态，仅做计算。</para>
/// </summary>
public static class RetentionPlanner
{
    /// <summary>
    /// 根据当前 <paramref name="readers"/> 快照、已有墓碑及保留策略，计算 Retention 执行计划。
    /// <list type="bullet">
    ///   <item><description>整段 drop：reader.MaxTimestamp &lt; cutoff → 加入 SegmentsToDrop；</description></item>
    ///   <item><description>部分过期：MinTimestamp &lt; cutoff &lt;= MaxTimestamp → 对每个过期 Block 注入墓碑；</description></item>
    ///   <item><description>已有等价墓碑的 (SeriesId, FieldName) 跳过；</description></item>
    ///   <item><description>同 (SeriesId, FieldName) 仅产生一条墓碑（去重）；</description></item>
    ///   <item><description>总注入数超过 MaxTombstonesPerRound 时截断。</description></item>
    /// </list>
    /// </summary>
    /// <param name="readers">当前所有已打开的 <see cref="SegmentReader"/> 快照。</param>
    /// <param name="existingTombstones">现有 <see cref="TombstoneTable"/>，用于跳过已有等价墓碑。</param>
    /// <param name="policy">Retention 策略。</param>
    /// <returns>本轮 Retention 的执行计划。</returns>
    /// <exception cref="ArgumentNullException">任何参数为 null 时抛出。</exception>
    public static RetentionPlan Plan(
        IReadOnlyList<SegmentReader> readers,
        TombstoneTable existingTombstones,
        RetentionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(readers);
        ArgumentNullException.ThrowIfNull(existingTombstones);
        ArgumentNullException.ThrowIfNull(policy);

        if (!policy.Enabled || readers.Count == 0)
            return new RetentionPlan(0, [], []);

        long ttlUnits = policy.TtlInTimestampUnits ?? (long)policy.Ttl.TotalMilliseconds;
        long now = policy.NowFn();
        long cutoff = now - ttlUnits;

        var segmentsToDrop = new List<long>();
        // 用 HashSet 对 (SeriesId, FieldName) 去重
        var seen = new HashSet<(ulong SeriesId, string FieldName)>();
        var tombstonesToInject = new List<TombstoneToInject>();

        foreach (var reader in readers)
        {
            // 整段全部过期 → 加入 drop 列表
            if (reader.MaxTimestamp < cutoff)
            {
                segmentsToDrop.Add(reader.Header.SegmentId);
                continue;
            }

            // 全段未过期 → 跳过
            if (reader.MinTimestamp >= cutoff)
                continue;

            // 部分过期：MinTimestamp < cutoff <= MaxTimestamp
            // 遍历该段所有 BlockDescriptor，为过期 block 注入墓碑
            foreach (var block in reader.Blocks)
            {
                if (tombstonesToInject.Count >= policy.MaxTombstonesPerRound)
                    break;

                // 该 block 没有过期点
                if (block.MinTimestamp >= cutoff)
                    continue;

                var key = (block.SeriesId, block.FieldName);

                // 已在本轮去重过
                if (seen.Contains(key))
                    continue;

                // 检查已有等价墓碑：From ≤ block.MinTimestamp && To ≥ cutoff - 1
                bool alreadyCovered = false;
                var existing = existingTombstones.GetForSeriesField(block.SeriesId, block.FieldName);
                foreach (var tomb in existing)
                {
                    if (tomb.FromTimestamp <= block.MinTimestamp && tomb.ToTimestamp >= cutoff - 1)
                    {
                        alreadyCovered = true;
                        break;
                    }
                }

                if (alreadyCovered)
                {
                    seen.Add(key); // 标记已处理，后续同 key block 也跳过
                    continue;
                }

                seen.Add(key);
                tombstonesToInject.Add(new TombstoneToInject(
                    block.SeriesId,
                    block.FieldName,
                    long.MinValue,
                    cutoff - 1));
            }

            if (tombstonesToInject.Count >= policy.MaxTombstonesPerRound)
                break;
        }

        return new RetentionPlan(cutoff, segmentsToDrop.AsReadOnly(), tombstonesToInject.AsReadOnly());
    }
}
