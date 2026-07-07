## 计数器重置感知的增长计算：increase() 函数

在时间序列监控领域，计数器（counter）是最常见的指标类型之一——总请求数、累计字节数、CPU 时间片等。但计数器有一个典型问题：当系统重启或计数器溢出时，数值会重置归零，导致简单的差值计算出现大幅负值。SonnetDB 的 `increase()` 函数专门解决这个问题，它只取正的变化量，忽略重置带来的负跳变。

### 为什么需要 increase()？

考虑一组连续上报的计数器值：`100 -> 200 -> 300 -> 50 -> 150 -> 250`。这里 `300 -> 50` 是一个重置事件（50 是重置后的新值）。普通 `difference()` 会得到 `-250`，而 `increase()` 正确计算出 `50`（重置后的新起点），整个序列的计算结果为 `(200-100)+(300-200)+(50-0)+(150-50)+(250-150) = 350`。

### 基本用法

```sql
-- 计算每 5 分钟的实际请求增长量
SELECT
  time('5 minutes') AS bucket,
  increase(total_requests) AS request_increase
FROM server_stats
WHERE ts >= '2025-01-01' AND ts < '2025-01-02'
GROUP BY time('5 minutes')
ORDER BY bucket;
```

### 在窗口函数中使用

`increase()` 可以作为窗口函数逐行计算：

```sql
SELECT
  ts,
  total_bytes_sent,
  increase(total_bytes_sent) OVER (ORDER BY ts) AS bytes_increase
FROM network_stats
WHERE ts >= '2025-02-01' AND ts < '2025-02-02'
ORDER BY ts;
```

### 与 difference() 的直观对比

```sql
-- 对比普通差值与计数器感知的增长
SELECT
  ts,
  total_requests,
  difference(total_requests) OVER (ORDER BY ts) AS raw_diff,
  increase(total_requests)   OVER (ORDER BY ts) AS counter_increase
FROM web_server_metrics
WHERE ts >= '2025-03-01' AND ts < '2025-03-02'
ORDER BY ts;
```

当发生重置时，`raw_diff` 会显示负值，而 `counter_increase` 会取 `max(0, diff)` 逻辑，只计算正向增量。

### 聚合模式：按时间段汇总增长

```sql
-- 每小时的总增长量
SELECT
  time('1 hour')               AS hour,
  SUM(
    increase(total_requests) OVER (ORDER BY ts)
  )                            AS hourly_request_growth,
  SUM(
    increase(total_errors) OVER (ORDER BY ts)
  )                            AS hourly_error_growth
FROM web_server_metrics
WHERE ts >= '2025-04-01'
GROUP BY time('1 hour')
ORDER BY hour;
```

### 实际应用：速率计算

```sql
-- 计算每秒请求速率（RPS）
WITH counter_data AS (
  SELECT
    ts,
    increase(total_requests) OVER (ORDER BY ts) AS req_increase
  FROM server_stats
  WHERE ts >= NOW() - INTERVAL '1 hour'
)
SELECT
  ts,
  req_increase / 1.0 AS rps  -- 假设每秒一条记录
FROM counter_data
ORDER BY ts;
```

### increase() 的核心优势

| 特性 | difference() | increase() |
|---|:---:|:---:|
| 重置感知 | 否 | 是 |
| 负值输出 | 允许 | 不允许（最小为 0） |
| 公式 | `value_n - value_{n-1}` | `max(0, value_n - value_{n-1})` |
| 适用指标 | 任意数值 | 单调递增计数器 |

### 适用场景

- 微服务请求量监控（应对 Pod 重启）
- 网络流量计费统计
- 数据库 WAL 日志增长量计算
- 消息队列积压变化追踪

`increase()` 是监控场景中不可或缺的函数，它让计数器指标的增量计算变得准确可靠，无需在应用层额外处理重置逻辑。
