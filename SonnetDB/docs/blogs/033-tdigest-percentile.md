## 深入 T-Digest：分位数聚合与 percentile

在时序数据分析中，仅仅知道平均值远远不够。分位数（Percentile）能够揭示数据的分布特征，帮助我们理解"绝大多数数据落在什么范围内"。SonnetDB 使用 T-Digest 算法实现高效的分位数计算，支持 p50、p90、p95、p99 等常用分位数。

### 为什么需要 T-Digest

传统分位数计算需要将所有数据排序后取特定位置的值，这对于海量时序数据来说计算代价极高。T-Digest 是一种近似算法，它通过将数据点聚合成质心（Centroid）来压缩数据，在保证较高精度的同时大幅降低内存占用和计算时间。

T-Digest 的核心特点是：在数据分布密集的区域使用较高的精度，在数据稀疏的区域使用较低的精度。这种自适应特性使得 T-Digest 特别适合处理时序数据中常见的偏斜分布。

### 基本用法

SonnetDB 提供 `percentile(field, n)` 函数来计算第 n 百分位数，以及 `p50`、`p90`、`p95`、`p99` 等便捷别名：

```sql
SELECT
    percentile(usage, 50)  AS p50,    -- 第 50 百分位数（中位数）
    percentile(usage, 90)  AS p90,    -- 第 90 百分位数
    percentile(usage, 95)  AS p95,    -- 第 95 百分位数
    percentile(usage, 99)  AS p99,    -- 第 99 百分位数
    p50(usage)             AS p50_alias,  -- 便捷别名
    p90(usage)             AS p90_alias,
    p95(usage)             AS p95_alias,
    p99(usage)             AS p99_alias
FROM cpu
WHERE host = 'server-01';
```

### 解读分位数结果

假设 CPU 使用率的查询结果如下：

- p50 = 0.55：50% 的时间内使用率不超过 55%
- p90 = 0.85：90% 的时间内使用率不超过 85%
- p99 = 0.93：99% 的时间内使用率不超过 93%

这意味着系统在大部分时间（90%）负载良好，但有 1% 的时间使用率超过 93%，这些极端值可能就是需要关注的性能瓶颈。

### 调试 T-Digest 内部状态

如果你对 T-Digest 的内部结构感兴趣，可以使用 `tdigest_agg` 函数查看聚合状态的 JSON 表示：

```sql
SELECT tdigest_agg(usage) AS tdigest_state
FROM cpu
WHERE host = 'server-01';
```

返回的 JSON 包含以下信息：
- compression：压缩参数，控制精度与内存的平衡
- centroids：质心列表（均值与权重），T-Digest 的核心数据结构
- count：参与聚合的数据点总数

通过分析质心列表，你可以了解 T-Digest 如何在数据空间中分布精度资源。

### 与 GROUP BY time 配合

分位数聚合与时间桶配合，可以分析不同时间段的数据分布变化：

```sql
SELECT
    p50(usage) AS p50_usage,
    p95(usage) AS p95_usage,
    p99(usage) AS p99_usage
FROM cpu
WHERE host = 'server-01'
GROUP BY time(1h);
```

这在 SLA 监控中尤为有用——你可以追踪每个小时的 p99 延迟或使用率，确保服务满足性能目标。T-Digest 算法的低内存特性使得即使在大规模时间桶聚合中也能保持高效的性能表现。
