## 基础聚合函数：count/sum/min/max/avg/first/last

聚合函数是时序数据分析的核心工具。SonnetDB 提供了七种基础聚合函数，覆盖了最常用的统计分析需求。无论你是计算平均值、汇总总量，还是追踪极值，这些函数都能帮助你快速从海量时序数据中提取有价值的信息。

### 七种聚合函数详解

**count(field)**：统计非空值的个数。常用于了解数据密度和采样频率。`count(*)` 会统计所有行，包括某些列为 NULL 的行。

**sum(field)**：计算所有值的总和。适用于累加型指标，如总流量、总能耗。

**min(field) / max(field)**：返回字段的最小值和最大值。适用于监控场景中的峰值和谷值检测。

**avg(field)**：计算算术平均值。最常用的中心趋势指标，反映数据的"平均水平"。

**first(field) / last(field)**：返回时间序列中的第一个值和最后一个值。在时序数据中按时间排序后取首尾值，适用于追踪初始状态和最新状态。

### 使用示例

```sql
SELECT
    count(usage)  AS cnt,       -- 非空记录数
    sum(usage)    AS total,     -- 使用率总和
    min(usage)    AS min_usage, -- 最低使用率
    max(usage)    AS max_usage, -- 最高使用率
    avg(usage)    AS avg_usage, -- 平均使用率
    first(usage)  AS first_usage, -- 初始使用率
    last(usage)   AS last_usage   -- 最终使用率
FROM cpu
WHERE host = 'server-01';
```

### 各函数的适用场景

**监控告警场景**：使用 `max()` 检查是否超过告警阈值，`min()` 检查是否低于下限：

```sql
SELECT
    max(usage) AS peak_usage,
    CASE WHEN max(usage) > 0.9 THEN 'ALERT' ELSE 'OK' END AS status
FROM cpu
WHERE host = 'server-01'
  AND time >= 1713657600000
  AND time < 1713657900000;
```

**容量规划场景**：使用 `avg()` 评估平均负载水平，结合 `count()` 确认数据样本量是否充足：

```sql
SELECT
    count(usage) AS sample_count,
    avg(usage)   AS avg_load,
    max(usage)   AS peak_load
FROM cpu
WHERE host = 'server-01';
```

**状态追踪场景**：使用 `first()` 和 `last()` 追踪字段的起始值和最新值，判断变化趋势：

```sql
SELECT
    first(usage) AS startup_usage,
    last(usage)  AS current_usage,
    last(usage) - first(usage) AS change
FROM cpu
WHERE host = 'server-01';
```

### 聚合与空值处理

SonnetDB 的聚合函数在计算时会自动跳过 NULL 值。`count(field)` 只统计非空行，`sum()`、`avg()`、`min()`、`max()` 同样忽略 NULL。如果所有值都为 NULL，`sum()`、`avg()` 等返回 NULL，而 `count()` 返回 0。

### 与 GROUP BY time 配合

基础聚合函数最强大的用法是与 `GROUP BY time` 配合，按时间桶计算聚合指标：

```sql
SELECT
    avg(usage)   AS avg_usage,
    max(usage)   AS peak_usage,
    min(usage)   AS min_usage,
    count(usage) AS sample_count
FROM cpu
WHERE host = 'server-01'
GROUP BY time(5m);
```

这条查询每 5 分钟计算一次平均、峰值、谷值和样本数，是监控仪表盘的典型查询模式。七种基础聚合函数覆盖了绝大多数日常分析需求，是学习 SonnetDB 聚合功能的起点。
