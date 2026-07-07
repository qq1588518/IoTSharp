## 按时间段分组：GROUP BY time() 时间桶聚合

在时间序列查询中，最常见的需求之一就是将连续的时间点按固定宽度的时间窗口进行分组聚合。SonnetDB 的 `GROUP BY time(duration)` 语法提供了简洁高效的时间分桶能力，支持多种时间单位，让你可以轻松实现从毫秒到年的时间维度聚合。

### 基本语法

```sql
SELECT
  time(duration) AS bucket,
  AGGREGATE_FUNC(value)
FROM table
GROUP BY time(duration);
```

`duration` 由数值和时间单位组成，支持以下单位：

- `ms` 或 `millisecond` —— 毫秒
- `s` 或 `second` —— 秒
- `m` 或 `minute` —— 分钟
- `h` 或 `hour` —— 小时
- `d` 或 `day` —— 天
- `w` 或 `week` —— 周
- `M` 或 `month` —— 月
- `y` 或 `year` —— 年

### 使用示例

```sql
-- 每 5 分钟的平均 CPU 使用率
SELECT
  time('5 minutes') AS bucket,
  AVG(cpu_usage)    AS avg_cpu
FROM system_metrics
WHERE ts >= '2025-08-01 00:00:00' AND ts < '2025-08-02 00:00:00'
GROUP BY time('5 minutes')
ORDER BY bucket;
```

```sql
-- 每 30 秒的最大请求数
SELECT
  time('30 seconds')  AS bucket,
  MAX(request_count)  AS peak_rps
FROM api_stats
WHERE ts >= NOW() - INTERVAL '1 hour'
GROUP BY time('30 seconds')
ORDER BY bucket;
```

```sql
-- 按周的累计写入字节数
SELECT
  time('1 week')      AS week_start,
  SUM(written_bytes)  AS total_written
FROM disk_io_logs
WHERE ts >= '2025-01-01'
GROUP BY time('1 week')
ORDER BY week_start;
```

### 与其他 GROUP BY 字段组合

```sql
-- 每台机器每小时的聚合
SELECT
  time('1 hour')       AS hour,
  machine_id,
  AVG(temperature)     AS avg_temp,
  MAX(temperature)     AS max_temp,
  COUNT(*)             AS readings
FROM sensor_data
WHERE ts >= '2025-06-01'
GROUP BY time('1 hour'), machine_id
ORDER BY hour, machine_id;
```

### time() 与 time_bucket() 的关系

`time(duration)` 是 SonnetDB 的 SQL 语法糖，底层映射为 `time_bucket(duration, ts)` 函数。两者在实际查询中可互换：

```sql
-- 以下两种写法等价
SELECT time('1 hour') AS h, AVG(val) FROM t GROUP BY time('1 hour');

SELECT time_bucket('1 hour', ts) AS h, AVG(val) FROM t GROUP BY h;
```

### 性能提示

- 选择合适的时间窗口：窗口越小，返回的行数越多，查询开销越大
- 建议结合 `WHERE` 条件限制时间范围，避免全表扫描
- 对于长时间范围的查询，较大的时间窗口（如小时或天）性能更优

`GROUP BY time()` 让时间序列聚合变得直观且强大，是日常时序分析的核心工具。
