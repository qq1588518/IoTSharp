## 异常检测方法对比：Z-Score、MAD 与 IQR

异常值是数据分析中不可忽视的问题——一个异常的温度读数可能预示着设备故障，一个异常的交易金额可能意味着欺诈行为。SonnetDB 提供了多种统计方法用于异常检测，其中最常用的三种是 Z-Score、MAD（中位数绝对偏差）和 IQR（四分位距法）。本文将从原理、优缺点和适用场景三个维度对比这三种方法，帮助开发者为自己的数据选择最合适的检测方案。

### Z-Score 方法

Z-Score 是最经典的异常检测方法，它计算每个数据点与均值的偏差，再除以标准差。一般而言，|Z| > 3 的数据点被认为是异常值（基于正态分布的 3σ 原则）。Z-Score 的优点是计算简单、直观易懂，但缺点是它对均值和标准差非常敏感，而这两个统计量本身容易被异常值影响（掩蔽效应）。

```sql
-- 使用 Z-Score 检测异常温度
SELECT time, temperature,
    (temperature - avg(temperature) OVER()) / stddev(temperature) OVER() AS z_score
FROM sensor_data
WHERE sensor_id = 'temp-01'
QUALIFY ABS(z_score) > 3;
```

### MAD 方法

MAD（Median Absolute Deviation）方法是 Z-Score 的稳健替代方案。它使用中位数代替均值，使用中位数绝对偏差代替标准差，因此对异常值本身具有很强的抗干扰能力。MAD 的定义是 `MAD = median(|Xi - median(X)|)`，其异常判据通常为 `|Xi - median(X)| / (MAD * 1.4826) > 3`，其中 1.4826 是使 MAD 在正态分布下与标准差一致的缩放因子。

```sql
-- 使用 MAD 方法检测异常值（更具鲁棒性）
WITH stats AS (
    SELECT 
        percentile_cont(0.5) WITHIN GROUP (ORDER BY temperature) AS med,
        percentile_cont(0.5) WITHIN GROUP (ORDER BY 
            ABS(temperature - percentile_cont(0.5) WITHIN GROUP (ORDER BY temperature))
        ) AS mad
    FROM sensor_data
    WHERE sensor_id = 'temp-01'
)
SELECT s.time, s.temperature,
    ABS(s.temperature - st.med) / (st.mad * 1.4826) AS modified_z_score
FROM sensor_data s, stats st
WHERE s.sensor_id = 'temp-01'
    AND ABS(s.temperature - st.med) / (st.mad * 1.4826) > 3;
```

### IQR 方法

IQR（Interquartile Range）方法基于四分位数检测异常值。它计算数据的 Q1（25% 分位数）和 Q3（75% 分位数），IQR = Q3 - Q1，然后将小于 Q1 - 1.5*IQR 或大于 Q3 + 1.5*IQR 的数据点标记为异常。IQR 方法同样具有较好的稳健性，且对数据分布没有正态性假设，适用面更广。

```sql
-- 使用 IQR 方法检测异常值
WITH stats AS (
    SELECT 
        percentile_cont(0.25) WITHIN GROUP (ORDER BY value) AS q1,
        percentile_cont(0.75) WITHIN GROUP (ORDER BY value) AS q3
    FROM sensor_data
)
SELECT time, value
FROM sensor_data, stats
WHERE value < q1 - 1.5 * (q3 - q1) 
   OR value > q3 + 1.5 * (q3 - q1);
```

### 如何选择

三种方法各有适用场景。如果数据近似正态分布且异常值比例较低（<5%），Z-Score 是最简单高效的选择。如果数据可能包含较多异常值或分布有偏，MAD 方法是更稳健的替代方案。对于分布未知且希望避免分布假设的场景，IQR 方法最为稳妥，1.5 倍 IQR 的经验阈值在工程实践中效果良好。在实际应用中，建议在 SonnetDB 中对多种方法进行对比测试，根据检测效果选择最适合当前数据的方法。
