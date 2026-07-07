## 案例：设备预测性维护——振动信号分析与故障预警

### 背景

某大型化工集团旗下工厂运行着数百台泵机、压缩机、风机等旋转设备。这些设备一旦故障停机，轻则造成数十万元的生产损失，重则引发安全事故。传统做法是定期停机检修（预防性维护），但往往"修好"的设备还能用很久，而"等待检修"的设备已经在跑故障。集团希望建立预测性维护体系：在设备真正故障前 1-2 周发出预警，实现"按需维修"。

### 挑战

- **信号复杂性**：振动信号包含丰富的频域特征，时域统计只是入门，需要提取 RMS、峭度等特征
- **基准漂移**：每台设备的振动"正常水平"不同，且随磨损缓慢升高，固定阈值会频繁误报
- **多故障类型**：不平衡、不对中、轴承磨损、松动等故障的振动特征各异
- **噪音干扰**：采样噪声、启停过渡、工况变化都会导致振动数据异常，需要区分真实故障

### 解决方案

**数据模型**：每台设备在 X/Y/Z 三个轴向各安装一个振动传感器，同时采集温度、转速等辅助指标。

```sql
CREATE MEASUREMENT vibration_metrics (
    machine_id  TAG,
    machine_type TAG,  -- pump / compressor / fan / motor
    location    TAG,   -- 安装位置：DE（驱动端）/ NDE（非驱动端）
    axis        TAG,   -- X / Y / Z
    plant       TAG,
    vibration   FIELD FLOAT,   -- 振动速度 mm/s RMS
    temperature FIELD FLOAT,   -- 轴承温度 °C
    rpm         FIELD FLOAT,   -- 转速 RPM
    current_a   FIELD FLOAT    -- 电机电流 A
);
```

**特征提取**：从原始振动时序中提取统计特征，用于状态判断。

```sql
-- 计算各设备过去 1 小时的振动统计特征（每 10 分钟一个特征窗口）
SELECT machine_id, axis,
       avg(vibration)     AS rms_vibration,
       max(vibration)     AS peak_vibration,
       max(vibration) / NULLIF(avg(vibration), 0) AS crest_factor,  -- 峰值因子
       stddev(vibration)  AS vibration_std,
       spread(vibration)  AS vibration_spread,   -- max - min
       -- 峭度近似（基于标准差和峰值的比值）
       (max(vibration) - min(vibration)) / NULLIF(stddev(vibration), 0) AS kurtosis_approx
FROM vibration_metrics
WHERE time > NOW() - INTERVAL '1h'
GROUP BY time(10m), machine_id, axis;
```

**基于 MAD 的自适应告警**：相比固定阈值，MAD 方法能自动适应每台设备的历史基准线，避免"基准漂移"导致的频繁误报。

```sql
-- 检测振动 RMS 异常（基于过去 7 天的历史数据计算基准）
SELECT machine_id, axis,
       avg(vibration) AS current_rms,
       anomaly(avg(vibration), 'mad', 3.0) AS is_anomaly
FROM vibration_metrics
WHERE time > NOW() - INTERVAL '1h'
GROUP BY time(10m), machine_id, axis
HAVING anomaly(avg(vibration), 'mad', 3.0) = 1;
```

**趋势检测——变点分析**：识别振动趋势发生结构性变化的时间点（轴承开始退化的起始点）。

```sql
-- CUSUM 变点检测：识别振动趋势的突变点
SELECT time, machine_id,
       avg(vibration) AS hourly_rms,
       changepoint(avg(vibration), 'cusum', 6.0) AS is_changepoint
FROM vibration_metrics
WHERE machine_id = 'PUMP-042'
  AND axis = 'X'
  AND time > NOW() - INTERVAL '90d'
GROUP BY time(1h), machine_id
HAVING changepoint(avg(vibration), 'cusum', 6.0) = 1;
```

**预测剩余使用寿命**：基于振动趋势预测设备何时达到警戒阈值（ISO 10816-3 标准：泵机振动 > 4.5 mm/s RMS 需停机检修）。

```sql
-- 预测 PUMP-042 振动趋势，估算何时超过 4.5 mm/s 阈值
SELECT *
FROM forecast(
    SELECT avg(vibration) AS daily_rms
    FROM vibration_metrics
    WHERE machine_id = 'PUMP-042'
      AND axis = 'X'
    GROUP BY time(1d),
    60,  -- 预测 60 天
    'holt_winters'
)
WHERE forecast_value > 4.5  -- 找到第一个超阈值预测点
LIMIT 1;
```

**温度-振动关联**：振动增大往往伴随轴承温度升高，双重指标确认可减少误报。

```sql
-- 同时出现振动异常和温度异常的设备（双重确认，更可靠的告警）
SELECT v.machine_id, v.time,
       v.vibration AS vib_rms,
       t.temperature AS bearing_temp
FROM vibration_metrics v
JOIN vibration_metrics t 
    ON v.machine_id = t.machine_id 
    AND v.time = t.time
    AND t.location = v.location
WHERE v.axis = 'X'
  AND v.time > NOW() - INTERVAL '1h'
  AND anomaly(v.vibration, 'mad', 3.0) = 1  -- 振动异常
  AND t.temperature > 85;                     -- 且轴承温度高
```

**多机横向对比**：在同类设备中识别明显偏离"群体基准"的异常设备。

```sql
-- 与同类型同工况的泵机对比，找出显著偏高的设备
SELECT machine_id, plant,
       avg(vibration) AS avg_rms,
       -- 与同类设备均值的偏离程度（倍数）
       avg(vibration) / avg(avg(vibration)) OVER (PARTITION BY machine_type) AS vs_fleet_avg
FROM vibration_metrics
WHERE machine_type = 'pump'
  AND time > NOW() - INTERVAL '24h'
GROUP BY machine_id, plant, machine_type
ORDER BY vs_fleet_avg DESC
LIMIT 10;
```

**故障模式分析**：结合转速和频域特征区分故障类型。

```sql
-- 分析 PUMP-042 振动与转速的关系（转速相关振动通常是不平衡/不对中）
SELECT time,
       avg(vibration) AS vib_rms,
       avg(rpm)       AS speed,
       -- 振动/转速 比值：不变表示与转速无关（轴承故障）；线性变化表示转速相关（不平衡）
       avg(vibration) / NULLIF(avg(rpm), 0) AS vib_per_rpm
FROM vibration_metrics
WHERE machine_id = 'PUMP-042'
  AND time > NOW() - INTERVAL '30d'
GROUP BY time(1h);
```

**维修效果验证**：检修后振动水平是否恢复正常。

```sql
-- 检修前后振动对比（检修时间戳：2026-04-15 08:00）
SELECT 
    'before' AS period,
    avg(vibration) AS avg_rms, max(vibration) AS peak_rms
FROM vibration_metrics
WHERE machine_id = 'PUMP-042'
  AND time BETWEEN '2026-04-08' AND '2026-04-15'
UNION ALL
SELECT 
    'after' AS period,
    avg(vibration) AS avg_rms, max(vibration) AS peak_rms
FROM vibration_metrics
WHERE machine_id = 'PUMP-042'
  AND time BETWEEN '2026-04-15' AND '2026-04-22';
```

### AI Copilot 辅助诊断

维修工程师向 Copilot 提问：

> "PUMP-042 最近振动在升高，我判断是轴承磨损，但也有可能是不对中，你能帮我分析吗？"

Copilot 自动查询振动与转速的相关性、温升趋势、频谱峭度变化，综合给出诊断建议："振动/转速比值稳定（排除转速相关故障），同时峰值因子从 2.1 升至 3.8，轴承温度升高 12°C，与轴承磨损特征吻合，建议优先检查驱动端轴承。"

### 实施效果

| 指标 | 传统定期检修 | 预测性维护 |
| --- | --- | --- |
| 计划外停机次数/年 | 18 次 | 4 次 |
| 维修提前预警时间 | — | 平均 16 天 |
| 年维修总费用 | 基准 | ↓35%（减少过修和紧急抢修） |
| 备件库存金额 | 高（需备齐全） | ↓40%（按需备件） |
| 设备可用率 | 94.2% | 98.7% |

预测性维护的本质是让数据代替人"盯着"设备。SonnetDB 的自适应异常检测、趋势预测和变点分析，让维修团队能够在故障发生前就收到可靠的预警，而不是被大量误报淹没。
