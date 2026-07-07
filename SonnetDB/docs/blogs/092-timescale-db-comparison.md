## 多数据库横向对比：SonnetDB vs SQLite vs InfluxDB vs TDengine

时序数据库选型需综合评估多维度指标。本文基于统一的 BenchmarkDotNet 框架，在同机环境下对比 SonnetDB、SQLite、InfluxDB 2.7 和 TDengine 3.3.4.3 的写入、查询、聚合和资源消耗表现。

### 测试方法

所有数据库运行于相同硬件（i9-13900HX / Windows 11 / Docker WSL2），数据集为 100 万模拟 IoT 传感器点，基准代码在 `tests/SonnetDB.Benchmarks/` 中可完全复现。

### 写入性能

| 数据库 | 100 万点耗时 | 吞吐量 | 对比 |
|--------|-------------|--------|------|
| **SonnetDB** | **545 ms** | **1.83 M pts/s** | **基准** |
| SQLite | 811 ms | 1.23 M pts/s | 1.5x 慢 |
| TDengine LP | 996 ms | 1.00 M pts/s | 1.8x 慢 |
| InfluxDB 2.7 | 5,222 ms | 0.19 M pts/s | 9.6x 慢 |
| TDengine REST | 44,137 ms | 0.02 M pts/s | 81x 慢 |

SonnetDB 的 LSM-Tree 架构和纯 C# 实现带来了极致的写入效率。SQLite 虽为嵌入式但缺乏时序优化，InfluxDB 和 TDengine REST 因协议开销显著落后。

### 查询性能

```sql
SELECT time, host, usage FROM cpu
WHERE host = 'server-01'
  AND time >= 1713676800000 AND time <= 1713680400000;
```

| 查询类型 | SonnetDB | SQLite | InfluxDB |
|---------|---------|--------|---------|
| 范围查询（100k 行） | **6.71 ms** | 44.5 ms | 410 ms |
| 聚合（AVG/COUNT） | **42.3 ms** | 89 ms | 450 ms |
| Compaction | **16.3 ms** | N/A | N/A |

SonnetDB 的 `MultiSegmentIndex` 实现跨段时间窗剪枝，在亚毫秒级定位目标 segment，再顺序扫描命中 block。SQLite 缺乏时间分区需全表扫描；InfluxDB 的 TSM 引擎在范围查询上额外开销较大。

### 聚合性能

MemTable 增量聚合 + 段级预聚合 + `AggregateResult.Merge` 三管齐下：

```csharp
// 跨段聚合合并：无需全量数据物化
foreach (var segment in matchingSegments)
{
    var partial = segment.ReadAggregated(key, aggType);
    result.Merge(partial);  // O(1) 合并累加器
}
```

### 资源消耗对比

| 指标 | SonnetDB | SQLite | InfluxDB | TDengine |
|-----|---------|--------|---------|----------|
| 空闲内存 | **18 MB** | 2 MB | 85 MB | 45 MB |
| AOT 体积 | **12 MB** | 1.5 MB | 280 MB | 95 MB |
| 许可证 | MIT | Public Domain | AGPL/商业 | AGPL |

### 功能矩阵

| 特性 | SonnetDB | SQLite | InfluxDB | TDengine |
|-----|---------|--------|---------|----------|
| 嵌入式 | 是 | 是 | 否 | 否 |
| 向量索引 + HNSW | 是 | 否 | 否 | 否 |
| AI Copilot | 是 | 否 | 否 | 否 |
| 地理空间/轨迹 | 是 | 否 | 否 | 是 |
| AOT 兼容 | 是 | 是 | 否 | 否 |

### 选型建议

- **嵌入式/边缘计算**：SonnetDB 首选，功能远超 SQLite，性能全面领先
- **服务端部署**：SonnetDB 写入快 InfluxDB 9.6x，资源仅 1/5
- **AI + 向量搜索**：SonnetDB 独家支持 `VECTOR` 类型和 HNSW 索引
- **大规模集群**：InfluxDB / TDengine 的分布式方案更成熟
