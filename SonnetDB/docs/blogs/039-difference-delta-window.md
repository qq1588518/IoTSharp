## 行间变化检测：difference() 与 delta() 窗口函数

在时间序列分析中，数据点之间的变化往往比绝对值本身更有意义。传感器读数的跳变、股票价格的涨跌、计数器数值的增减——SonnetDB 提供了 `difference()` 和 `delta()` 两个窗口函数来捕捉这种行与行之间的变化。

### difference() — 当前值减去前一个值

`difference(value)` 返回当前行与前一行的差值，即 `diff = value_current - value_previous`。第一行的差值为 NULL。

```sql
-- 监控温度传感器每 10 秒的变化量
SELECT
  ts,
  temperature,
  difference(temperature) AS temp_change
FROM sensor_readings
WHERE sensor_id = 'sensor-001'
  AND ts >= '2025-01-01' AND ts < '2025-01-02'
ORDER BY ts;
```

```sql
-- 发现异常跳变（超过阈值的变化）
SELECT
  ts,
  temperature,
  difference(temperature) AS temp_change
FROM sensor_readings
WHERE sensor_id = 'sensor-001'
  AND ABS(difference(temperature)) > 10
  AND ts >= '2025-01-01' AND ts < '2025-01-02'
ORDER BY ts;
```

### delta() — 考虑数据类型的有符号差值

`delta(value)` 与 `difference()` 类似，但语义上更强调"增量"，通常用于监控持续增长或周期性变化的指标：

```sql
SELECT
  ts,
  page_views,
  delta(page_views) AS view_delta
FROM web_traffic
WHERE ts >= '2025-02-01' AND ts < '2025-02-02'
ORDER BY ts;
```

### 在窗口分区中使用

将两个函数与 `OVER` 子句结合，可以在不同维度上独立计算变化：

```sql
-- 每台机器各自计算 CPU 变化
SELECT
  ts,
  host_id,
  cpu_usage,
  difference(cpu_usage) OVER (PARTITION BY host_id ORDER BY ts) AS cpu_change
FROM system_metrics
WHERE ts >= '2025-03-01' AND ts < '2025-03-02'
ORDER BY host_id, ts;
```

### difference() 与 delta() 的区别

| 特性 | difference() | delta() |
|---|:---:|:---:|
| 语义 | 减法的直接结果 | 带符号的变化量 |
| 第一行结果 | NULL | NULL |
| 负值 | 允许 | 允许（表示下降） |
| 典型用途 | 任意值变化检测 | 监控增量变化 |

### 实际应用：变化率与趋势分析

```sql
-- 计算变化率（百分比变化）
WITH changes AS (
  SELECT
    ts,
    revenue,
    difference(revenue) AS revenue_change
  FROM daily_revenue
  WHERE ts >= '2025-04-01'
)
SELECT
  ts,
  revenue,
  revenue_change,
  ROUND(
    100.0 * revenue_change / NULLIF(
      LAG(revenue) OVER (ORDER BY ts), 0
    ), 2
  ) AS change_pct
FROM changes
ORDER BY ts;
```

### 适用场景

- 传感器异常跳变检测（设备故障预警）
- 金融数据逐笔变动分析
- 流量突增/突降监控
- 数据一致性验证（检查计数器是否单调递增）

`difference()` 和 `delta()` 是 SonnetDB 窗口函数体系中的基础工具，适合对时间序列做行级别的细粒度变化分析。
