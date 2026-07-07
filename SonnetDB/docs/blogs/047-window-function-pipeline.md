## SonnetDB 窗口函数流水线：从差值计算到异常检测的完整监控链路

SonnetDB 的窗口函数不仅各自独立强大，更能组合成完整的监控分析管道。本文通过一个生产级示例，展示如何串联多个窗口函数构建"数据采集 → 速率计算 → 平滑去噪 → 异常检测 → 告警触发"的全链路。

### 场景定义

假设我们监控一组 API 网关的请求计数（单调递增计数器），目标是实时检测请求量的异常激增或骤降。

### 第一步：计算请求速率

原始数据是累计计数器，我们需要 `rate()` 将其转换为每秒请求数（RPS）：

```sql
SELECT time, host,
       rate(request_count, 1s) AS rps
FROM api_gateway
WHERE host = 'gw-01'
  AND time > NOW() - 1h;
```

`rate()` 自动处理计数器重置（负值输出 null），确保即使网关重启也不会产生虚假的负速率。

### 第二步：平滑去噪

单点的 RPS 可能因为网络抖动而剧烈波动，使用 `ewma()` 做指数平滑：

```sql
WITH rps_data AS (
  SELECT time, host,
         rate(request_count, 1s) AS rps
  FROM api_gateway
  WHERE host = 'gw-01'
    AND time > NOW() - 1h
)
SELECT time, host, rps,
       ewma(rps, 0.3) AS smooth_rps
FROM rps_data;
```

`alpha = 0.3` 在平滑度和响应灵敏度之间取得了良好的平衡。

### 第三步：异常检测

使用 `anomaly()` 函数的 'zscore' 方法检测 RPS 是否偏离历史基线：

```sql
WITH rps_data AS (
  SELECT time, host,
         rate(request_count, 1s) AS rps
  FROM api_gateway
  WHERE host = 'gw-01'
    AND time > NOW() - 1h
),
smooth_data AS (
  SELECT time, host, rps,
         ewma(rps, 0.3) AS smooth_rps
  FROM rps_data
)
SELECT time, host, rps, smooth_rps,
       anomaly(smooth_rps, 'zscore', 3.0) AS is_anomaly
FROM smooth_data;
```

当 `is_anomaly` 输出 `true` 时，表示该点的 Z-Score 超过 3.0，即偏离均值超过 3 个标准差。

### 第四步：变点检测（可选）

对于趋势突变而非单点异常的场景，可以使用 `changepoint()` 的 CUSUM 方法检测均值漂移：

```sql
SELECT time, smooth_rps,
       changepoint(smooth_rps, 'cusum', 4.0, 0.5) AS drift_detected
FROM (
  SELECT time, ewma(rate(request_count, 1s), 0.3) AS smooth_rps
  FROM api_gateway WHERE host = 'gw-01'
    AND time > NOW() - 2h
);
```

### 完整的监控查询

将上述所有步骤整合为一个完整的监控查询：

```sql
WITH raw AS (
  SELECT time, host,
         rate(request_count, 1s) AS rps
  FROM api_gateway
  WHERE host IN ('gw-01', 'gw-02', 'gw-03')
    AND time > NOW() - 1h
),
smooth AS (
  SELECT time, host,
         ewma(rps, 0.3) AS rps_smooth
  FROM raw
)
SELECT time, host, rps_smooth,
       CASE WHEN anomaly(rps_smooth, 'zscore', 3.0) THEN 'ALERT' ELSE 'OK' END AS status
FROM smooth
WHERE anomaly(rps_smooth, 'zscore', 3.0) = true;
```

该查询直接输出需要告警的行，实现了"端到端"的监控告警流水线。整个流程完全在 SonnetDB 的 SQL 引擎内完成，无需外部计算框架，大幅降低了监控系统的架构复杂度。
