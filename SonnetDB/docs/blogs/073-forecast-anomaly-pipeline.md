## 预测、异常检测与变化点分析：组合工作流

单一的时序分析方法往往难以全面刻画数据的特征。预测告诉你未来的趋势，异常检测告诉你当前是否有异常值，变化点分析告诉你系统的状态是否发生了根本性的转变。将三者组合起来，可以构建出强大的数据分析工作流。本文将展示如何在 SonnetDB 中通过 SQL 将 `forecast()`、异常检测和 `changepoint()` 组合使用，实现多层次的数据洞察。

### 三层分析架构

一个完整的时序分析流水线包含三个层次：第一层是预测层，使用历史数据建立模型并对未来进行预测；第二层是异常检测层，将实际值与预测值对比，识别偏离预测范围的异常点；第三层是变化点分析层，检测数据是否发生了结构性的偏移，解释异常的原因。在 SonnetDB 中，这三层分析可以在一个 SQL 查询中串联完成。

```sql
-- 三层组合分析工作流
WITH forecast_cte AS (
    SELECT 
        forecast(value, time, 'holt-winters', 
                horizon => 24, seasonal_period => 24) AS f
    FROM server_metrics
    WHERE time >= now() - INTERVAL '7 days'
),
anomaly_cte AS (
    SELECT time, value,
        (SELECT f->>'forecast' FROM forecast_cte) AS pred_values
    FROM server_metrics
    WHERE time >= now() - INTERVAL '24 hours'
)
SELECT time, value,
    CASE WHEN ABS(value - pred) > 2 * stddev 
         THEN 'anomaly' ELSE 'normal' 
    END AS status,
    changepoint(value, time, threshold => 3.0) AS regime_shift
FROM anomaly_cte;
```

### 预测驱动的异常检测

将 `forecast()` 与异常检测结合是最实用的组合方式之一。不同于基于历史统计的静态阈值，预测驱动的异常检测能够自适应数据的趋势和季节性变化。例如，电商网站的流量在工作日和周末有明显的模式差异，静态阈值难以同时适应两种模式，而 Holt-Winters 预测模型可以自动调整预期值。

```sql
-- 预测偏差异常检测
WITH pred AS (
    SELECT 
        forecast(page_views, time, 'holt-winters',
                horizon => 6, seasonal_period => 24) AS f
    FROM web_traffic
    WHERE time >= now() - INTERVAL '30 days'
)
SELECT 
    time, 
    page_views,
    (f->>'forecast')::json->>(time::text) AS expected,
    ABS(page_views - ((f->>'forecast')::json->>(time::text))::float) AS deviation
FROM web_traffic, pred
WHERE ABS(page_views - ((f->>'forecast')::json->>(time::text))::float) 
      > 3 * ((f->>'std')::json->>(time::text))::float;
```

### 变化点辅助的告警降噪

单一的异常检测经常产生误报——一个突发的数据尖峰可能是真实异常，也可能只是传感器噪声。结合 `changepoint()` 分析可以提高告警的准确性：如果异常点同时伴随结构性变化，说明系统可能确实发生了状态转变；如果只是孤立的异常点但没有结构变化，则更可能是噪声或瞬态干扰。

### 实际案例：服务器性能退化检测

以下是一个完整的实践案例，用于检测服务器性能的退化过程。首先使用 `forecast()` 预测正常的响应时间范围，然后通过异常检测识别响应时间的偏离，最后使用 `changepoint()` 定位系统状态发生根本变化的时间点——这通常对应着代码部署、配置变更或硬件老化等事件。

```sql
-- 服务器性能退化分析
WITH baseline AS (
    SELECT time, response_time
    FROM server_metrics
    WHERE host = 'api-server-01'
        AND time >= now() - INTERVAL '7 days'
)
SELECT 
    time,
    response_time,
    changepoint(response_time, time, threshold => 5.0) AS degradation_start
FROM baseline
ORDER BY time;
```

组合分析工作流的价值在于，它能够从多个角度交叉验证分析结论，大幅提升数据洞察的准确性和可解释性。SonnetDB 让这一切在 SQL 层面即可完成。
