## 案例：楼宇自动化——SonnetDB 助力智慧园区能耗管理

### 背景

某科技园区拥有 28 栋办公楼、总建筑面积 42 万平方米，每年电费支出超过 3000 万元。暖通空调（HVAC）是最大的耗能系统，占总用电量的 55%。园区管理方希望通过精细化的数据分析，识别能耗浪费点，将整体能耗强度（单位面积年耗电量）降低 15% 以上，同时维持人员的舒适度。

### 挑战

- **多系统接入**：空调、照明、电梯、给排水共 4 大系统、8000+ 个传感器点位，协议不统一
- **细粒度计量**：需要精确到楼层、功能区的能耗分摊，方便向租户计费
- **舒适度约束**：节能不能以牺牲温湿度舒适度为代价，需要在约束下优化
- **节假日模式**：不同楼栋有不同的使用计划，需要按时间策略自动调整运行参数

### 解决方案

**能耗数据模型**：

```sql
CREATE MEASUREMENT energy_metrics (
    building_id TAG,
    floor_id    TAG,
    zone_id     TAG,
    system_type TAG,      -- hvac / lighting / elevator / water
    temperature FIELD FLOAT,
    humidity    FIELD FLOAT,
    co2_ppm     FIELD FLOAT,
    power_kw    FIELD FLOAT,
    occupancy   FIELD INT  -- 人员占用数（来自 WiFi 探针统计）
);
```

**楼层级能耗分析**：

```sql
-- B 栋各楼层上周每日空调能耗（kWh）
SELECT floor_id,
       strftime('%Y-%m-%d', time) AS day,
       sum(power_kw) * (5.0 / 60)  AS kwh   -- 5 分钟采样间隔
FROM energy_metrics
WHERE building_id = 'B-BUILDING'
  AND system_type = 'hvac'
  AND time > NOW() - INTERVAL '7d'
GROUP BY floor_id, day
ORDER BY floor_id, day;
```

**HVAC PID 温度控制**：通过 SonnetDB 的 `pid_series()` 计算每个空调机组的阀门开度调节量，实现更精细的温控，减少过冷/过热导致的额外能耗。

```sql
-- 计算 5F-East 区域空调阀门的 PID 调节输出（目标 24°C）
SELECT time, zone_id,
       temperature AS actual_temp,
       pid_series(24.0, temperature, time, 1.5, 0.2, 0.1) AS valve_adjustment
FROM energy_metrics
WHERE zone_id = '5F-EAST'
  AND system_type = 'hvac'
  AND time > NOW() - INTERVAL '2h';
```

**人员占用与能耗关联**：分析人员密度与能耗效率，找到最优的空调预冷/预热策略。

```sql
-- 分析工作日不同时段的人均能耗
SELECT 
    strftime('%H', time) AS hour_of_day,
    avg(occupancy)        AS avg_occupancy,
    avg(power_kw)         AS avg_hvac_kw,
    avg(power_kw) / NULLIF(avg(occupancy), 0) AS kw_per_person
FROM energy_metrics
WHERE building_id = 'B-BUILDING'
  AND system_type = 'hvac'
  AND strftime('%w', time) BETWEEN '1' AND '5'  -- 工作日
  AND time > NOW() - INTERVAL '30d'
GROUP BY hour_of_day
ORDER BY hour_of_day;
```

**异常能耗告警**：识别非工作时间的能耗异常（如忘关空调、设备空转）。

```sql
-- 检测夜间（22:00-06:00）异常高能耗
SELECT time, building_id, floor_id, power_kw,
       anomaly(power_kw, 'iqr', 1.5) AS is_anomaly
FROM energy_metrics
WHERE system_type = 'hvac'
  AND strftime('%H', time) NOT BETWEEN '06' AND '22'
  AND time > NOW() - INTERVAL '7d'
  AND anomaly(power_kw, 'iqr', 1.5) = 1
ORDER BY power_kw DESC;
```

**舒适度趋势分析**：监控 CO₂ 浓度变化率，预判新风量不足。

```sql
-- CO₂ 浓度变化率（ppm/min），变化率 > 5 时触发新风加量
SELECT time, zone_id,
       co2_ppm,
       derivative(co2_ppm, 1m) AS co2_rate_per_min
FROM energy_metrics
WHERE time > NOW() - INTERVAL '30m'
  AND derivative(co2_ppm, 1m) > 5;
```

**能耗基准线预测**：基于历史数据建立天气-能耗回归模型，识别高于基准线的浪费。

```sql
-- 月度能耗趋势与预测（识别同比变化）
SELECT *
FROM forecast(
    SELECT sum(power_kw) * (5.0 / 60) AS monthly_kwh
    FROM energy_metrics
    WHERE building_id = 'B-BUILDING'
    GROUP BY time(30d),
    6,
    'holt_winters'
);
```

**租户计量分摊**：精确到楼层的用电量，用于按月向租户结算。

```sql
-- 各租户楼层上月电费计算
SELECT zone_id,
       sum(power_kw) * (5.0 / 60)        AS total_kwh,
       sum(power_kw) * (5.0 / 60) * 0.85 AS electricity_cost_rmb  -- ¥0.85/kWh
FROM energy_metrics
WHERE time >= '2026-03-01'
  AND time <  '2026-04-01'
  AND system_type IN ('hvac', 'lighting')
GROUP BY zone_id
ORDER BY total_kwh DESC;
```

### 实施效果

上线运行 6 个月后：

| 指标 | 基线 | 优化后 |
| --- | --- | --- |
| 园区总能耗强度 | 85 kWh/㎡/年 | 71 kWh/㎡/年（↓16.5%） |
| 年节省电费 | — | ≈ 495 万元 |
| HVAC 过冷/过热投诉次数/月 | 42 次 | 9 次 |
| 夜间漏开空调次数/月 | 23 次 | 3 次（自动告警） |
| 租户用电分摊准确性 | 楼栋级（误差 ±15%） | 区域级（误差 ±2%） |

园区管理团队将 SonnetDB 定位为"能耗大脑"——不只是存储传感器数据，而是通过 PID 控制、异常检测、趋势预测的组合，真正将数据转化为节能行动。
