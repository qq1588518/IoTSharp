## 实时异常检测监控：窗口函数与告警流水线

在工业运维和业务监控场景中，异常检测不仅仅是离线分析的任务，更重要的是能够实时地发现和处理异常。SonnetDB 结合窗口函数和流式数据处理能力，为构建实时的异常检测告警流水线提供了完整的 SQL 级解决方案。本文将介绍如何利用 SonnetDB 的窗口函数实现滚动窗口上的异常检测，并搭建端到端的监控告警系统。

### 基于滑动窗口的实时异常检测

时间序列数据天然适合使用滑动窗口进行分析。SonnetDB 支持多种窗口函数，允许在每个窗口内独立计算统计量并判断异常。以下示例展示了如何使用 1 小时的滑动窗口，在每收到一条新数据时重新计算 Z-Score 并判断是否异常。

```sql
-- 实时滑动窗口异常检测
SELECT 
    time,
    temperature,
    AVG(temperature) OVER (
        ORDER BY time 
        RANGE BETWEEN INTERVAL '1 hour' PRECEDING AND CURRENT ROW
    ) AS window_avg,
    STDDEV(temperature) OVER (
        ORDER BY time 
        RANGE BETWEEN INTERVAL '1 hour' PRECEDING AND CURRENT ROW
    ) AS window_std,
    (temperature - AVG(temperature) OVER (
        ORDER BY time 
        RANGE BETWEEN INTERVAL '1 hour' PRECEDING AND CURRENT ROW
    )) / NULLIF(STDDEV(temperature) OVER (
        ORDER BY time 
        RANGE BETWEEN INTERVAL '1 hour' PRECEDING AND CURRENT ROW
    ), 0) AS z_score
FROM sensor_data
WHERE device_id = 'pump-03'
QUALIFY ABS(z_score) > 3;
```

### 构建告警流水线

实时异常检测的最终目的是触发告警。在 SonnetDB 中，可以通过持续查询（continuous query）或定时任务定期检测异常，并将结果写入告警表。当异常被检测到时，可以将相关信息格式化并推送到通知系统。

```sql
-- 创建告警事件表
CREATE MEASUREMENT alert_events (
    device_id TAG,
    alert_type TAG,
    severity FIELD INT,
    value FIELD FLOAT,
    threshold FIELD FLOAT,
    message FIELD STRING
);

-- 写入检测到的异常告警
INSERT INTO alert_events (time, device_id, alert_type, severity, value, threshold, message)
SELECT 
    time,
    'pump-03' AS device_id,
    'temperature_anomaly' AS alert_type,
    CASE WHEN ABS(z_score) > 5 THEN 3 ELSE 2 END AS severity,
    temperature AS value,
    3.0 AS threshold,
    FORMAT('温度异常: %s, Z-Score: %s', temperature, z_score) AS message
FROM (
    SELECT time, temperature,
        (temperature - AVG(temperature) OVER w) / NULLIF(STDDEV(temperature) OVER w, 0) AS z_score
    FROM sensor_data
    WHERE device_id = 'pump-03'
    WINDOW w AS (ORDER BY time RANGE BETWEEN INTERVAL '1 hour' PRECEDING AND CURRENT ROW)
) WHERE ABS(z_score) > 3;
```

### 分级告警与降噪

在实际生产中，单一的异常检测往往会产生过多噪音。SonnetDB 支持分级告警策略：轻度异常（如 |Z| > 3）仅记录日志，中度异常（|Z| > 4）发送通知，严重异常（|Z| > 5）触发紧急告警。同时，可以结合持续异常检测机制——只有当连续三个以上检测窗口都判定为异常时才触发告警，从而有效减少误报。

### 告警效果评估

告警流水线的质量需要持续评估。SonnetDB 可以将检测结果与实际故障记录进行对比，计算检测率（Recall）和误报率（Precision），帮助运维团队不断优化阈值参数。

```sql
-- 评估告警准确率
SELECT 
    alert_type,
    COUNT(*) AS total_alerts,
    SUM(CASE WHEN confirmed = true THEN 1 ELSE 0 END) AS true_positives,
    SUM(CASE WHEN confirmed = false THEN 1 ELSE 0 END) AS false_positives,
    AVG(CASE WHEN confirmed = true THEN 1.0 ELSE 0.0 END) AS precision
FROM alert_events
WHERE time >= now() - INTERVAL '30 days'
GROUP BY alert_type;
```

通过 SonnetDB 的窗口函数和流水线能力，运维团队可以在不引入额外流处理框架的情况下，构建出功能完善的实时异常检测系统。
