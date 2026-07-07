## SonnetDB 范围查询速度揭秘：100k 行仅需 6.71 ms

范围查询是时序数据库最基础的访问模式。SonnetDB 在 100,000 行级别的范围查询中实现了仅 **6.71 ms** 的延迟。本文深入解析支撑这一性能的段跳跃（Segment Skipping）和 block metadata 快速路径技术。

### 基准场景

```sql
SELECT time, host, usage, temperature
FROM cpu
WHERE host = 'server-01'
  AND time >= 1713676800000 AND time <= 1713680400000;
```

| 指标 | 值 |
|------|-----|
| 返回行数 | 100,000 |
| Mean | **6.71 ms** |
| 内存分配 | 2.45 MB |

### 段跳跃（Segment Skipping）

SonnetDB 将数据按时间窗口划分为不可变 Segment，每个 Segment 在 flush 时记录完整的元数据（时间范围、Tag 值前后缀、统计信息等）。`MultiSegmentIndex` 利用这些元数据在查询时快速裁剪：

```
查询: time BETWEEN 1713676800000 AND 1713680400000

Segment 1: time 1713670000000-1713675000000  →  ❌ 跳过
Segment 2: time 1713675000000-1713680000000  →  ✅ 部分命中，读取
Segment 3: time 1713680000000-1713685000000  →  ✅ 部分命中，读取
Segment 4: time 1713685000000-1713690000000  →  ❌ 跳过
```

核心代码逻辑：

```csharp
// MultiSegmentIndex 的时间窗剪枝
public IReadOnlyList<SegmentReader> LookupCandidates(
    long fromMs, long toMs)
{
    var candidates = new List<SegmentReader>();
    foreach (var reader in _readers)
    {
        if (reader.MaxTimestamp >= fromMs
            && reader.MinTimestamp <= toMs)
        {
            candidates.Add(reader);
        }
    }
    return candidates;
}
```

通过 `Volatile` 发布的 `_readers` 快照支持无锁读取，与 Compaction/Flush 并发安全。在均匀数据分布下，段跳跃可过滤 80% 以上的无关段。

### Block Metadata 快速路径

每个 Segment 内部按 (SeriesId, FieldName) 划分为多个 Block。`BlockHeader` 记录了时间范围、`BlockIndexEntry` 提供精确偏移量。`SegmentReader` 在读取时先扫描 Block 索引，跳过高精度过滤后不相干的 block：

```csharp
// 查询引擎：MemTable + 多段 N 路堆合并
foreach (var reader in matchedSegments)
{
    foreach (var block in reader.Blocks)
    {
        if (block.MaxTime >= query.FromTime
            && block.MinTime <= query.ToTime)
        {
            // 仅解码命中的 block
            var points = reader.DecodeBlock(block);
            // 加入 N 路合并
        }
    }
}
```

### 与竞品对比

| 数据库 | 100k 行范围查询 |
|--------|----------------|
| **SonnetDB** | **6.71 ms** |
| SQLite | 44.5 ms |
| InfluxDB 2.7 | 410 ms |

SonnetDB 快于 SQLite（6.6x）和 InfluxDB（61x）的核心原因：
- 段级时间索引 + 无锁并发读取
- 列式存储只读需要的列，不触碰无关数据
- Block 级高精度过滤减少 I/O
- 纯 C# 零额外协议开销

对于 IoT 平台设备数据回溯、监控系统历史查询等场景，SonnetDB 可在毫秒级返回数十万行结果，显著提升交互式分析体验。
