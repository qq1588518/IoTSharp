---
name: lag-delta
description: 在 SonnetDB 中计算时序差分、变化率、累计增量与环比：优先使用 difference / delta / increase / derivative / rate / irate 等内置窗口函数。当前不支持标准 SQL 的 LAG/LEAD OVER。
triggers:
  - lag
  - lead
  - 差分
  - 变化率
  - 增量
  - delta
  - 环比
  - 同比
  - 速率
  - rate
  - 前后对比
  - 相邻行
  - 上一条
  - 下一条
  - 变化量
  - 累计增量
requires_tools:
  - query_sql
  - describe_measurement
---

# 差分与变化率计算指南

SonnetDB 当前版本已经内置了一组**时序相邻点分析函数**，可以覆盖大部分 “LAG/LEAD + 算术表达式” 的需求。

## 1. 先说结论

- 当前**不支持**标准 SQL 的 `LAG(...) OVER (...)` / `LEAD(...) OVER (...)`。
- 计算“当前值减上一值”，优先用 `difference(field)` 或 `delta(field)`。
- 计算“计数器本次增加了多少”，优先用 `increase(field)`。
- 计算“单位时间变化率”，优先用 `derivative(field[, unit])`、`rate(field[, unit])`、`irate(field[, unit])`。
- 要做“环比 / 同比 / 上一桶对比”，先 `GROUP BY time(...)` 聚合，再在应用层做相邻桶比较。

## 2. 函数对照表

| 需求 | 推荐函数 | 说明 |
| --- | --- | --- |
| 相邻值之差 | `difference(field)` / `delta(field)` | 两者当前语义相同，都是 `current - previous` |
| 只保留非负增量 | `increase(field)` | 适合累计量、计数器 reset 场景 |
| 每秒变化率 | `derivative(field)` / `rate(field)` | 默认按 1 秒归一化 |
| 自定义单位变化率 | `derivative(field, 1m)` | 按指定时间单位归一化 |
| 计数器速率 | `rate(field)` / `irate(field)` | 当前都按相邻两点计算，负值会被抑制 |

## 3. 常见写法

### 3.1 一阶差分

```sql
SELECT
    time,
    usage,
    difference(usage) AS diff_usage
FROM cpu
WHERE host = 'server-01';
```

说明：

- 首行没有上一条数据，结果为 `NULL`。
- `delta(field)` 当前与 `difference(field)` 等价。

### 3.2 计数器增量

```sql
SELECT
    time,
    bytes_total,
    increase(bytes_total) AS bytes_increase
FROM net_interface
WHERE host = 'server-01'
  AND interface = 'eth0';
```

适用场景：

- 电表累计量
- 网络字节计数器
- 请求总数 / 错误总数

如果设备重启导致计数器归零，`increase(...)` 会把负差抑制为 `NULL`，比裸 `difference(...)` 更适合。

### 3.3 单位时间变化率

```sql
SELECT
    time,
    cpu_pct,
    derivative(cpu_pct, 1s) AS rate_per_sec
FROM host_cpu
WHERE host = 'server-01';
```

```sql
SELECT
    time,
    bytes_total,
    rate(bytes_total, 1s) AS bytes_per_sec
FROM net_interface
WHERE host = 'server-01'
  AND interface = 'eth0';
```

说明：

- `derivative` 会保留正负变化。
- `rate` / `irate` 当前更适合计数器语义，负速率会被抑制为 `NULL`。
- 当相邻两点时间差 `dt <= 0` 时，结果为 `NULL`。

### 3.4 累计值与增量分开看

```sql
SELECT
    time,
    bytes_total,
    increase(bytes_total) AS bytes_increase
FROM net_interface
WHERE host = 'server-01'
  AND interface = 'eth0';
```

如果你想看运行累计值，通常直接看原始累计列 `bytes_total` 即可；如果想做“增量累计趋势”，建议在应用层对 `bytes_increase` 再做累加。

## 4. 环比 / 同比怎么做

当前版本不支持 `LAG(...) OVER (...)`，所以“上一小时对比”“上一桶环比”建议分两步：

第一步，先把数据聚合成固定时间桶：

```sql
SELECT
    avg(cpu_pct) AS avg_cpu,
    max(cpu_pct) AS peak_cpu
FROM host_cpu
WHERE host = 'server-01'
  AND time >= now() - 24h
GROUP BY time(1h);
```

第二步，在应用层对返回的桶序列做相邻比较：

- `delta_cpu = current.avg_cpu - previous.avg_cpu`
- `change_pct = delta_cpu / previous.avg_cpu`

这比让 Copilot 编造 `LAG(...) OVER (...)` 更符合当前 SonnetDB 的真实能力。

## 5. 如果用户明确问 LAG / LEAD

推荐回答策略：

1. 明确说明 SonnetDB 当前不支持 `OVER (...)`。
2. 如果需求只是“与上一点比较”，改写成 `difference` / `delta` / `derivative` / `rate`。
3. 如果需求是“与上一桶比较”“前后两桶对比”“环比同比”，告诉用户先 `GROUP BY time(...)`，再在应用层比较。

可直接给用户这样的转换示例：

```sql
-- 不要这样写
SELECT time, usage, LAG(usage) OVER (ORDER BY time) AS prev_usage
FROM cpu;

-- 当前 SonnetDB 推荐这样写
SELECT time, usage, difference(usage) AS diff_usage
FROM cpu;
```

## 6. 常见陷阱

- 不要把 `difference` / `rate` 用在 TAG 列上，它们要求数值型 FIELD。
- 不要假设 `GROUP BY time(...)` 会自动返回 bucket 列；当前只返回聚合列。
- 不要把 “按 host 分组”“按 device_id 分组” 和 “按时间桶聚合” 混在一条 SQL 里；当前只支持 `GROUP BY time(...)`。
- 不要让模型生成 `LAG(...) OVER (...)`、`LEAD(...) OVER (...)`、`time_bucket(...)` 这类当前公开语法之外的写法。
