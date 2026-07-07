## 统计聚合函数：stddev/variance/spread/median/mode

在基础聚合函数之外，SonnetDB 还提供了一组统计聚合函数，用于衡量数据的离散程度和分布特征。这些函数基于 Welford 在线算法实现，能够在单次数据扫描中高效计算出多个统计指标。

### 五种统计聚合函数

**stddev(field)**：标准差。衡量数据点与平均值之间的离散程度。标准差越大，数据波动越剧烈。在监控场景中，标准差可以帮助判断系统的稳定性。

**variance(field)**：方差。标准差的平方，同样是衡量数据离散程度的指标。方差越大，数据的波动范围越广。

**spread(field)**：极差。`max(field) - min(field)`，衡量数据的整体跨度。简单直观地反映数据的取值范围。

**median(field)**：中位数。将数据排序后处于中间位置的值。相比平均值，中位数对异常值不敏感，能更好地反映数据的中心趋势，特别是在数据分布偏斜时。

**mode(field)**：众数。数据中出现频率最高的值。适用于分类或离散型数据的分析，如确定最常见的状态值。

### 使用示例

```sql
SELECT
    stddev(usage)   AS std,       -- 标准差
    variance(usage) AS var,       -- 方差
    spread(usage)   AS spread,    -- 极差 (max-min)
    median(usage)   AS median,    -- 中位数
    mode(usage)     AS mode       -- 众数
FROM cpu
WHERE host = 'server-01';
```

### Welford 在线算法

SonnetDB 的 `stddev` 和 `variance` 实现基于 Welford 在线算法。与传统算法需要两遍扫描（第一遍计算均值，第二遍计算方差）不同，Welford 算法只需要一次扫描即可完成计算，且数值稳定性更好。

该算法的核心思想是维护三个运行变量：计数 n、均值 M_n 和平方差之和 S_n。每处理一个新数据点 x：

1. 更新计数：`n = n + 1`
2. 更新均值：`M_n = M_{n-1} + (x - M_{n-1}) / n`
3. 更新平方和：`S_n = S_{n-1} + (x - M_{n-1}) * (x - M_n)`

最终方差为 `S_n / n`（总体方差）或 `S_n / (n-1)`（样本方差），标准差为方差的平方根。

### 应用场景

**系统稳定性评估**：使用 `stddev` 判断系统负载的波动程度：

```sql
SELECT
    avg(usage)  AS avg_usage,
    stddev(usage) AS usage_stability
FROM cpu
WHERE host = 'server-01';
```

标准差越小，说明系统运行越稳定。如果标准差突然增大，可能预示着系统出现了异常波动。

**数据分布分析**：联合使用多个统计函数全面了解数据特征：

```sql
SELECT
    avg(usage)    AS mean,
    median(usage) AS median,
    stddev(usage) AS std,
    spread(usage) AS range,
    CASE
        WHEN abs(avg(usage) - median(usage)) > stddev(usage) / 2
        THEN '偏斜分布' ELSE '近似正态'
    END AS distribution
FROM cpu
WHERE host = 'server-01';
```

当平均值和中位数差异显著时，说明数据分布存在偏斜，此时中位数比平均值更能代表"典型值"。

这些统计聚合函数在性能监控、异常检测和质量分析等场景中发挥着重要作用，是时序数据分析工具箱中的重要组成部分。
