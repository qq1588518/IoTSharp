## SonnetDB 聚合查询优化：跨桶融合与 MemTable 增量聚合

聚合查询是时序数据分析的核心操作。SonnetDB 通过跨桶融合（Cross-Bucket Fusion）和 MemTable 增量聚合两项创新技术，将基础聚合查询从全量扫描优化到近乎零成本的预计算读取。

### 聚合面临的挑战

```sql
SELECT time_bucket('1m', time) AS minute,
       avg(usage), max(usage), p95(usage)
FROM cpu
WHERE time > now() - 1h
GROUP BY minute;
```

对于千万级数据量，传统全量扫描方式需要数百毫秒甚至数秒。SonnetDB 通过以下优化大幅压缩这一时间。

### 跨桶融合（Cross-Bucket Fusion）

每个 Segment 在 Flush 时预计算并写入段级统计摘要 —— `Min`、`Max`、`Sum`、`Count` 等基础聚合值直接存储在 Segment Footer 中：

```csharp
// 查询引擎的分层聚合
// 1. 段级：直接从元数据读取预计算摘要
foreach (var reader in matchedSegments)
{
    var meta = reader.ReadBlockMetadata(seriesKey, fieldName);
    totalSum += meta.Sum;
    totalCount += meta.Count;
    globalMax = Math.Max(globalMax, meta.Max);
    globalMin = Math.Min(globalMin, meta.Min);
}

// 2. 跨桶合并：O(1) 融合
var avg = totalSum / totalCount;
```

```
Segment 时序波峰：  ├───┬───┬───┬───┤
预计算摘要：         sum, count, min, max, [tdigest]
                              ↓
查询引擎跨桶融合：  avg = Σsum / Σcount
                   max = max(max₁, max₂, ...)
```

对于 SUM、COUNT、AVG、MIN、MAX，查询可直接从摘要计算精确结果，**无需扫描任何原始数据点**。对于百分位数等复杂聚合，T-Digest 算法提供可合并的近似分位估计。

### MemTable 增量聚合

仍在内存中的"热数据"同样实现增量聚合。`MemTableSeries` 在接收写入的同时持续维护聚合统计：

```csharp
internal sealed class MemTableSeries
{
    private double _runningSum;
    private long _runningCount;
    private double _runningMin = double.MaxValue;
    private double _runningMax = double.MinValue;
    private readonly TDigest _tdigest = new();

    public void Append(long timestamp, FieldValue value)
    {
        var d = value.AsDouble();
        _runningSum += d;
        _runningCount++;
        if (d < _runningMin) _runningMin = d;
        if (d > _runningMax) _runningMax = d;
        _tdigest.Add(d);
    }

    public AggregateResult GetAggregate(Aggregator agg) => agg switch
    {
        Aggregator.Sum   => new(_runningSum),
        Aggregator.Count => new(_runningCount),
        Aggregator.Min   => new(_runningMin),
        Aggregator.Max   => new(_runningMax),
        Aggregator.Avg   => new(_runningSum / _runningCount),
        Aggregator.P95   => new(_tdigest.Quantile(0.95)),
        _ => ... // 回退扫描
    };
}
```

### 性能提升

| 聚合函数 | 无优化（全量扫描） | 优化后 | 提升倍数 |
|---------|------------------|--------|---------|
| AVG | 850 ms | 8 ms | **106x** |
| MIN/MAX | 780 ms | 3 ms | **260x** |
| COUNT | 650 ms | 2 ms | **325x** |
| P95（T-Digest） | 1,200 ms | 38 ms | **32x** |

### 与竞品对比

BenchmarkDotNet 实测：SonnetDB 聚合 100 万点仅需 42.3 ms，InfluxDB 需 450 ms、SQLite 需 89 ms。对于 Grafana 面板中常用的 MIN/MAX/AVG 查询，延迟从秒级降至毫秒级，使得实时仪表盘的响应速度有质的飞跃。
