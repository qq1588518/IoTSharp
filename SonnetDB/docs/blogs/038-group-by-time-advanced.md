## 高级时间桶聚合：同一时间窗口内的多指标组合分析

在实际生产环境中，一个时间窗口内往往需要同时计算多种聚合指标——平均值、最大值、百分位数、计数等。SonnetDB 的 `GROUP BY time()` 支持在同一查询中组合任意多个聚合函数，并且可以配合 `FILTER` 子句对每个桶内的数据进行条件过滤，实现高度灵活的高级分析。

### 多聚合组合

```sql
-- 同一时间窗口内计算多种统计量
SELECT
  time('5 minutes')              AS bucket,
  COUNT(*)                       AS total_requests,
  AVG(response_time_ms)          AS avg_latency,
  MAX(response_time_ms)          AS max_latency,
  MIN(response_time_ms)          AS min_latency,
  STDDEV(response_time_ms)       AS latency_stddev,
  percentile_cont(0.99) WITHIN GROUP (ORDER BY response_time_ms) AS p99_latency
FROM api_requests
WHERE ts >= '2025-09-01' AND ts < '2025-09-02'
GROUP BY time('5 minutes')
ORDER BY bucket;
```

### 按标签分组的多聚合

```sql
-- 每个数据中心每台主机的详细聚合
SELECT
  time('1 hour')                  AS hour,
  datacenter,
  host_id,
  COUNT(*)                        AS sample_count,
  AVG(cpu_utilization)            AS avg_cpu,
  MAX(cpu_utilization)            AS max_cpu,
  AVG(memory_usage_gb)            AS avg_mem,
  SUM(network_bytes)              AS total_network,
  AVG(disk_iops)                  AS avg_iops
FROM infrastructure_metrics
WHERE ts >= '2025-10-01'
GROUP BY time('1 hour'), datacenter, host_id
ORDER BY hour, datacenter, host_id;
```

### 使用 FILTER 进行条件聚合

`FILTER` 子句允许你在同一个时间桶内对不同的数据子集做独立聚合，避免使用子查询或 `CASE WHEN`：

```sql
-- 按 HTTP 状态码分类统计
SELECT
  time('1 minute')                               AS minute,
  COUNT(*)                                       AS total,
  COUNT(*) FILTER (WHERE status_code >= 200 AND status_code < 300) AS success,
  COUNT(*) FILTER (WHERE status_code >= 400 AND status_code < 500) AS client_error,
  COUNT(*) FILTER (WHERE status_code >= 500)                        AS server_error,
  AVG(response_time_ms) FILTER (WHERE status_code < 500)            AS avg_good_latency,
  AVG(response_time_ms) FILTER (WHERE status_code >= 500)           AS avg_error_latency
FROM http_logs
WHERE ts >= '2025-11-01 08:00:00' AND ts < '2025-11-01 09:00:00'
GROUP BY time('1 minute')
ORDER BY minute;
```

### 多值字段的分布聚合

一个时间桶内同时对不同字段做不同类型的聚合：

```sql
SELECT
  time('30 minutes')         AS bucket,
  -- 温度统计
  AVG(temperature)           AS avg_temp,
  MIN(temperature)           AS min_temp,
  MAX(temperature)           AS max_temp,
  -- 湿度统计
  AVG(humidity)              AS avg_humidity,
  -- 电压统计
  AVG(voltage)               AS avg_voltage,
  MAX(voltage)               AS max_voltage,
  -- 告警统计
  COUNT(*) FILTER (WHERE alarm_level >= 3) AS critical_alarms,
  -- 数据质量
  COUNT(*) FILTER (WHERE temperature IS NULL OR humidity IS NULL) AS null_readings
FROM environmental_sensors
WHERE ts >= '2025-12-01'
GROUP BY time('30 minutes'), sensor_id
ORDER BY bucket, sensor_id;
```

### 最佳实践

1. **避免过多的时间桶**：时间窗口过小会生成大量行，建议结合 `LIMIT` 使用
2. **利用 FILTER 提效**：相比 `UNION ALL` 多个查询，FILTER 只需扫描一次数据
3. **合理选择聚合函数**：`AVG` + `STDDEV` + `COUNT` 通常比 `PERCENTILE` 更快
4. **索引优化**：确保 `ts` 列上有索引，`time_bucket` 和 `GROUP BY` 列可以考虑组合索引

高级时间桶聚合让 SonnetDB 能够在一次查询中完成复杂的多维分析，极大减少了查询数量和客户端处理负担。
