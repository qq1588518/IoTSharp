## 使用 histogram() 进行等宽分桶分布分析

数据分布分析是时间序列工作负载中的关键环节。当你想了解响应时间的分布是否呈长尾、CPU 使用率集中在哪个区间、或者延迟的 99 分位落在哪里时，`histogram(field, n)` 函数可以直接在数据库内完成等宽分桶，避免将原始数据全量拉到客户端。

### 等宽直方图的工作原理

`histogram(field, buckets)` 将字段的值域 `[min, max]` 均匀划分为 `buckets` 个等宽区间，然后统计每个区间内有多少条记录。返回结果包含桶的下界（bucket_start）、上界（bucket_end）以及计数（count）。

### 基本用法

```sql
-- 将响应时间分为 10 个等宽桶
SELECT
  histogram(response_time_ms, 10) AS rt_histogram
FROM api_requests
WHERE ts >= '2025-04-01' AND ts < '2025-04-02';
```

返回结果样例如下（实际为嵌套表结构）：

| bucket_start | bucket_end | count |
|---|---|---|
| 0.0 | 150.0 | 45231 |
| 150.0 | 300.0 | 18204 |
| 300.0 | 450.0 | 6732 |
| ... | ... | ... |
| 1350.0 | 1500.0 | 43 |

### 与时间分桶结合

```sql
-- 按小时查看延迟分布
SELECT
  time_bucket('1 hour', ts) AS hour,
  histogram(latency_ms, 5)  AS latency_hist
FROM service_logs
WHERE ts >= NOW() - INTERVAL '24 hours'
GROUP BY hour
ORDER BY hour;
```

### 计算每个桶的百分比

```sql
-- 计算每个桶的占比
WITH hist AS (
  SELECT
    histogram(response_time_ms, 20) AS h
  FROM api_requests
  WHERE ts >= '2025-05-01' AND ts < '2025-06-01'
),
unnested AS (
  SELECT
    (h->>'bucket_start')::double AS bucket_start,
    (h->>'bucket_end')::double   AS bucket_end,
    (h->>'count')::bigint        AS cnt
  FROM hist, LATERAL UNNEST(hist.h) AS h
)
SELECT
  bucket_start,
  bucket_end,
  cnt,
  ROUND(100.0 * cnt / SUM(cnt) OVER (), 2) AS pct
FROM unnested
ORDER BY bucket_start;
```

### 自定义分桶数量

桶的数量直接影响分析的精细度：

```sql
-- 粗略概览：3 个桶
SELECT histogram(temperature, 3) FROM sensor_data
WHERE ts >= '2025-07-01' AND ts < '2025-08-01';

-- 精细分析：50 个桶  
SELECT histogram(temperature, 50) FROM sensor_data
WHERE ts >= '2025-07-01' AND ts < '2025-08-01';
```

### 适用场景

- 延迟/响应时间分布分析
- 传感器数值分布监控（温度、湿度、电压）
- 计费金额区间统计
- 数据质量检查（发现异常聚集）

`histogram()` 让分布分析变得简单直接，无需外部工具即可在 SQL 中完成完整的数据分布洞察。
