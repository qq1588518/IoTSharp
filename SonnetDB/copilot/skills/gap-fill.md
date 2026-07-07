---
name: gap-fill
description: 检测 SonnetDB 时序数据中的时间缺口，以及用 fill / locf / interpolate 处理结果集中的空值。当前不支持 generate_series、LAG/LEAD OVER 这类完整时序骨架生成语法。
triggers:
  - gap
  - 缺口
  - 缺失
  - 空洞
  - 填充
  - fill
  - 插值
  - 稀疏
  - 不连续
  - 数据断点
  - 前值填充
  - 线性插值
  - 零值填充
  - 数据完整性
  - 缺失数据
requires_tools:
  - query_sql
  - describe_measurement
---

# 时序缺口检测与填充指南

SonnetDB 当前版本对“缺口处理”要分成两类看：

- **时间缺口**：某个预期采样时刻根本没有行。
- **字段空值**：这一行存在，但某个 field 在该时间点是 `NULL`。

这两类问题的处理方式不同。

## 1. 当前能力边界

- 当前**不支持** `generate_series`、`LAG/LEAD OVER (...)` 这类完整时间骨架生成语法。
- 因此“检测少了哪些时间点”“自动补出不存在的时间行”，通常要在应用层完成。
- 当前**支持** `fill(field, value)`、`locf(field)`、`interpolate(field)`，但它们只作用于**已有结果行中的空值**，不会凭空生成新的时间点。

## 2. 先判断是哪种缺口

### 2.1 时间缺口：缺少整行数据

例子：

- 设备本该每 30 秒上报一次
- 12:00:00 有数据，12:00:30 没有，12:01:00 又有数据

这属于“少了一行”。当前建议做法：

1. 先查出原始 `time` 序列。
2. 在应用层按预期采样周期比较相邻时间戳。
3. 发现缺口后，再决定是只告警、图表留空，还是补一条估算值。

可直接这样查原始时间轴：

```sql
SELECT time, temp_celsius
FROM sensor_climate
WHERE device_id = 'S-A01'
  AND time >= now() - 24h
ORDER BY time ASC;
```

应用层判断：

- 预期周期 = `30000ms`
- 若 `current.time - previous.time > 30000ms`，则中间存在缺口

### 2.2 字段空值：这一行存在，但某个字段为 NULL

例子：

- 某个时间点只有 `humidity_pct`
- 同一时间点缺少 `temp_celsius`

这时就可以用 SonnetDB 内置窗口函数来填充。

## 3. 三种内置填充函数

### 3.1 固定值填充 `fill(field, value)`

```sql
SELECT
    time,
    humidity_pct,
    fill(temp_celsius, -1) AS temp_filled
FROM sensor_climate
WHERE device_id = 'S-A01'
ORDER BY time ASC;
```

适用场景：

- 想显式标记“这里原本缺值”
- 计数类指标用 `0` 补值
- 下游系统约定了特殊占位值

### 3.2 前值填充 `locf(field)`

```sql
SELECT
    time,
    humidity_pct,
    locf(temp_celsius) AS temp_filled
FROM sensor_climate
WHERE device_id = 'S-A01'
ORDER BY time ASC;
```

适用场景：

- 设备状态
- 缓慢变化的温度、液位、压力
- 短时间掉点，但业务允许“沿用上一值”

注意：

- `locf` 只会沿用**前一个非空值**
- 序列开头如果就是空值，仍然会得到 `NULL`

### 3.3 线性插值 `interpolate(field)`

```sql
SELECT
    time,
    humidity_pct,
    interpolate(temp_celsius) AS temp_filled
FROM sensor_climate
WHERE device_id = 'S-A01'
ORDER BY time ASC;
```

适用场景：

- 温度
- 压力
- 液位
- 其他随时间平滑变化的连续量

注意：

- `interpolate` 需要前后都有有效值
- 如果缺口在序列开头或结尾，通常无法补全
- 对突变信号、告警状态、开关量不适合

## 4. 什么时候该用哪种策略

| 场景 | 推荐策略 |
| --- | --- |
| 告警状态、开关量 | `locf` |
| 请求数、错误数等计数型指标 | `fill(field, 0)` |
| 温度、压力、液位等连续量 | `interpolate` |
| 不确定缺值原因，想保守处理 | 保留 `NULL`，不填充 |

## 5. 真正的“补时间点”怎么做

如果你要的是“图表上每分钟都必须有一个点”，当前推荐工作流是：

1. 用 SQL 查询已有数据。
2. 应用层生成完整时间骨架。
3. 把缺失的时间点补成 `NULL`、0、前值或插值。
4. 仅在展示层使用这些补点结果，不要轻易回写覆盖原始数据。

推荐的第一步 SQL：

```sql
SELECT time, temp_celsius
FROM sensor_climate
WHERE device_id = 'S-A01'
  AND time >= now() - 1h
ORDER BY time ASC;
```

然后在应用层：

- 若做图表：补齐时间轴即可
- 若做告警：直接对缺口长度做判断即可
- 若做长期派生数据：单独写入新的 rollup / derived measurement，不要污染原始表

## 6. 数据完整性检查建议

当前版本如果要检查“最近 1 小时采样是否完整”，建议先查点数：

```sql
SELECT count(*) AS actual_count
FROM sensor_climate
WHERE device_id = 'S-A01'
  AND time >= now() - 1h;
```

应用层再用预期点数对比：

- 预期每 30 秒一条，1 小时应有 `120` 条
- 完整率 = `actual_count / 120`

如果你还想知道“缺口集中在哪一段时间”，继续查原始时间轴并在应用层比较相邻时间差。

## 7. 常见误区

- 不要把 `fill / locf / interpolate` 理解成“自动生成缺失时间点”；它们只处理已有行中的空值。
- 不要对长时间缺口做线性插值；视觉上好看，但业务含义往往是错的。
- 不要把插值结果直接覆盖原始 measurement；建议写到单独的派生 measurement，或仅用于展示。
- 不要让 Copilot 生成 `time_bucket(...)`、`generate_series(...)`、`LAG(...) OVER (...)` 这类当前版本并未公开支持的缺口处理语法。
