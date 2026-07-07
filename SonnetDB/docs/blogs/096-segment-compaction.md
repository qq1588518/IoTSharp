## SonnetDB 段合并机制：基于大小分层的 Compaction 策略

随着数据的持续写入，SonnetDB 会不断产生新的数据段文件。如果不加管理，过多的段文件会导致查询性能下降和存储空间的碎片化。SonnetDB 采用**基于大小分层（Size-Tiered）的 Compaction 策略**，通过精心设计的规划器（Planner）和合并器（Merger）来自动优化段文件布局。

### 为什么需要 Compaction

SonnetDB 的段文件（Segment）是数据持久化的基本单元。当 MemTable 达到阈值后，数据会被刷写为一个新的段文件。随着时间的推移，系统中会出现大量大小不一、时间范围重叠的小段：

```text
刷写前: MemTable (内存)
刷写后: seg_001.data (128MB), seg_002.data (64MB),
        seg_003.data (256MB), seg_004.data (32MB),
        seg_005.data (8MB),  ...
```

小段过多会带来三个问题：一是查询时需要打开更多文件，增加 I/O 开销；二是文件系统的元数据管理负担加重；三是时间范围重叠的段会降低段跳跃过滤的效率。

### Size-Tiered 规划器（Planner）

SonnetDB 的 Compaction Planner 定期扫描所有段文件，基于大小分层策略识别需要合并的段。核心算法如下：

```csharp
public class SizeTieredPlanner
{
    private readonly int _minTierSize = 64 * 1024 * 1024;  // 64MB 基准
    private readonly int _maxTierCount = 10;                 // 每层最多段数

    public CompactionPlan Plan(List<SegmentInfo> segments)
    {
        // 按大小分级
        var tiers = new List<List<SegmentInfo>>();

        foreach (var segment in segments.OrderBy(s => s.Size))
        {
            var tier = (int)Math.Floor(Math.Log2(segment.Size / _minTierSize));
            if (tier < 0) tier = 0;

            if (tiers.Count <= tier)
                tiers.Add(new List<SegmentInfo>());
            tiers[tier].Add(segment);
        }

        // 选择超出阈值的层级进行合并
        var toCompact = new List<SegmentInfo>();
        foreach (var tier in tiers)
        {
            if (tier.Count >= _maxTierCount)
            {
                toCompact.AddRange(tier);
            }
        }

        return new CompactionPlan
        {
            SegmentsToCompact = toCompact,
            EstimatedOutputSize = toCompact.Sum(s => s.Size)
        };
    }
}
```

规划器将段按照大小分为多个层级（Tier），每个层级的大小基准是 `2^n * 64MB`。当一个层级中的段数量超过阈值（默认 10 个）时，规划器触发 Compaction，将该层级的所有段合并为一个更大的段。

```
Tier 0 (64MB):    [s1] [s2] [s3] [s4] [s5] [s6] [s7] [s8] [s9] [s10] [s11]
                  └────────────────── 11 个段，触发合并 ──────────────────┘
Tier 1 (128MB):   [t1] [t2] [t3] [t4] [t5] ... ← 之后这些段进入更高层级
```

### 合并器（Merger）

当规划器确定了合并计划后，合并器负责执行实际的段合并操作。合并过程采用归并排序（Merge Sort）方式，将多个有序段合并为一个新的有序段：

```csharp
public class SegmentMerger
{
    public async Task<SegmentInfo> MergeAsync(
        List<SegmentInfo> segments, CancellationToken ct)
    {
        // 1. 创建新的段写入器
        var writer = new SegmentWriter(_outputPath);

        // 2. 多路归并排序
        var heap = new PriorityQueue<SegmentCursor, long>();
        foreach (var seg in segments)
        {
            var cursor = seg.GetSortedCursor();
            if (cursor.MoveNext())
                heap.Enqueue(cursor, cursor.Current.Timestamp);
        }

        while (heap.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var cursor = heap.Dequeue();
            await writer.WriteAsync(cursor.Current);

            if (cursor.MoveNext())
                heap.Enqueue(cursor, cursor.Current.Timestamp);
        }

        // 3. 完成写入，构建新段的元数据
        var newSegment = await writer.CompleteAsync();

        // 4. 删除旧的段文件
        foreach (var seg in segments)
            File.Delete(seg.FilePath);

        return newSegment;
    }
}
```

合并过程以事务方式执行：新段完全写入后，再原子性地替换旧段。如果在合并过程中发生崩溃，旧段文件仍然完好无损，保证了数据的安全性。

### Compaction 调优参数

SonnetDB 的 Compaction 行为可以通过以下参数进行调优：

```csharp
// appsettings.json 中的 Compaction 配置
{
  "SonnetDB": {
    "Compaction": {
      "Enabled": true,
      "IntervalSeconds": 3600,
      "MinTierSizeMB": 64,
      "MaxTierCount": 10,
      "MaxConcurrentCompactions": 2,
      "TargetSegmentSizeMB": 512
    }
  }
}
```

合理配置 Compaction 参数可以在写入放大（Write Amplification）和查询性能之间找到平衡。对于写入密集场景，适当增大 `MinTierSize` 减少合并频率；对于查询密集场景，适当减小 `TargetSegmentSize` 让段跳跃更精确。
