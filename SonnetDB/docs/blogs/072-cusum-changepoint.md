## 结构变化检测：CUSUM 与 Changepoint 分析

时间序列数据往往会在某个时间点发生结构性变化——设备从正常运行状态退化到故障状态，用户行为因产品改版而转变，经济指标因政策调整而改道。检测这些变化发生的时刻，对于故障诊断、归因分析和决策制定具有重要意义。SonnetDB 提供了 `changepoint()` 函数，用于检测时间序列中的结构断点，帮助用户快速定位数据分布发生显著变化的时刻。

### CUSUM 算法原理

CUSUM（Cumulative Sum，累积和）是最常用的变化检测算法之一。其核心思想是对累积的偏差进行监控：当过程处于受控状态时，累积和围绕零随机波动；当过程发生偏移时，累积和会持续偏离零值。CUSUM 算法的优势在于即使变化很微弱，只要持续存在，累积效应也能使其被检测出来。

SonnetDB 的 `changepoint()` 函数默认使用 CUSUM 变体，支持两个关键参数：`threshold`（阈值）控制检测灵敏度，较小的阈值可以检测到更微弱的变化但也更容易误报；`drift`（漂移参数）用于容忍允许范围内的正常波动，防止频繁触发告警。

```sql
-- 使用 changepoint() 检测温度序列的结构变化
SELECT 
    time,
    temperature,
    changepoint(temperature, time, 
                threshold => 5.0, 
                drift => 0.5) AS change_detected
FROM reactor_data
WHERE reactor_id = 'R-101'
ORDER BY time;
```

### 变化时刻定位

`changepoint()` 函数返回一个布尔值，标记检测到结构变化的时刻。结合其他窗口函数，可以定位变化发生前后的统计特性变化，帮助分析变化的性质和幅度。

```sql
-- 定位变化前后的统计差异
WITH change_points AS (
    SELECT 
        time,
        changepoint(temperature, time, threshold => 4.0) AS is_change
    FROM reactor_data
    WHERE reactor_id = 'R-101'
),
segments AS (
    SELECT time, temperature,
        SUM(CASE WHEN is_change THEN 1 ELSE 0 END) OVER (ORDER BY time) AS segment_id
    FROM change_points
)
SELECT 
    segment_id,
    MIN(time) AS start_time,
    AVG(temperature) AS avg_temp,
    STDDEV(temperature) AS std_temp,
    COUNT(*) AS sample_count
FROM segments
GROUP BY segment_id
ORDER BY start_time;
```

### 多维度变化检测

在实际应用中，结构变化往往体现在多个维度上。例如，设备故障可能同时表现为温度升高、振动加剧和能耗增加。SonnetDB 允许对多个指标同时进行 changepoint 分析，综合判断系统中是否发生了结构性变化。

```sql
-- 多维变化检测
SELECT 
    time,
    temperature,
    vibration,
    power_consumption,
    changepoint(temperature, time, threshold => 3.0) OR 
    changepoint(vibration, time, threshold => 2.0) OR 
    changepoint(power_consumption, time, threshold => 4.0) AS multi_modal_change
FROM machine_data
WHERE machine_id = 'CNC-05'
ORDER BY time;
```

### 参数调优建议

`changepoint()` 的 `threshold` 和 `drift` 参数需要根据具体数据的特点进行调整。一个实用的方法是先使用较大的 threshold 值（如 10.0）运行检测，观察检测到的变化点是否合理；然后逐步降低 threshold 直到检测到所有预期的变化点；如果误报过多，适当增加 drift 参数的值。对于大多数工业传感器数据，threshold 在 3.0~8.0 之间、drift 在 0.5~2.0 之间是一个合理的起始范围。
