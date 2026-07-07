## 使用 HyperLogLog 进行基数估计：distinct_count() 函数详解

在大规模时间序列数据分析中，精确计算唯一值的数量（基数）是一个非常常见的需求——比如统计某段时间内有多少独立用户访问了系统、有多少不同的设备上报了数据、或者有多少个唯一的 IP 地址产生了日志。传统方案使用 `COUNT(DISTINCT expr)` 在数据量巨大时，内存和计算开销都非常高。SonnetDB 提供的 `distinct_count()` 函数基于 HyperLogLog（HLL）算法，以可控的精度损失换取极致的性能提升。

### HyperLogLog 原理简述

HyperLogLog 是一种概率性基数估计算法，它通过哈希函数将元素映射为比特串，利用"最长前导零"的统计规律来估算基数。SonnetDB 中默认实现了精度参数 p=14 的 HLL，标准误差约为 1.625%，内存占用仅为 12KB 左右——无论你有 100 万还是 10 亿个唯一值。

### 基本用法

```sql
-- 统计每天的唯一用户数
SELECT
  time_bucket('1 day', ts) AS day,
  distinct_count(user_id) AS unique_users
FROM sensor_events
WHERE ts >= '2025-01-01' AND ts < '2025-02-01'
GROUP BY day
ORDER BY day;
```

```sql
-- 统计每个传感器标签的唯一设备数
SELECT
  sensor_tag,
  distinct_count(device_id) AS unique_devices
FROM telemetry
WHERE ts >= NOW() - INTERVAL '7 days'
GROUP BY sensor_tag
ORDER BY unique_devices DESC;
```

### 与精确 COUNT DISTINCT 的精度对比

```sql
-- 同时对比精确值与 HLL 估计值
SELECT
  time_bucket('1 hour', ts) AS hour,
  COUNT(DISTINCT session_id) AS exact_count,
  distinct_count(session_id)  AS hll_estimate,
  ABS(COUNT(DISTINCT session_id) - distinct_count(session_id))
    / NULLIF(COUNT(DISTINCT session_id)::float, 0) * 100 AS error_pct
FROM web_sessions
WHERE ts >= '2025-03-01' AND ts < '2025-03-02'
GROUP BY hour
ORDER BY hour;
```

### 合并多个 HLL 草图

HyperLogLog 的一大优势是草图（sketch）可以合并。在 SonnetDB 中，你可以对子查询中的 HLL 结果做二次聚合：

```sql
-- 先按小时计算，再合并为天的结果
SELECT
  time_bucket('1 day', hour) AS day,
  distinct_count(hll_sketch) AS daily_unique_users
FROM (
  SELECT
    time_bucket('1 hour', ts) AS hour,
    distinct_count(user_id)   AS hll_sketch
  FROM app_events
  WHERE ts >= '2025-06-01' AND ts < '2025-07-01'
  GROUP BY hour
) sub
GROUP BY day
ORDER BY day;
```

### 适用场景

- 大基数去重计数（百万级以上）时性能提升显著
- 对流式数据做实时唯一值统计
- 允许 1%-3% 误差的仪表盘与报表场景

`distinct_count()` 在 SonnetDB 中是 HyperLogLog 的一等公民实现，推荐在分析型查询中优先使用。
