## 案例：农业 IoT——温室大棚环境数据采集与智能灌溉

### 背景

某现代农业企业在华南地区运营 120 座智能温室大棚，种植高附加值的草莓、番茄、辣椒等经济作物。每座大棚面积约 2000 平方米，部署了温度、湿度、CO₂、光照、土壤水分等传感器，以及自动灌溉、遮阳、通风等执行机构。企业希望通过数据驱动的精准农业，将水肥利用率提升 30%，同时减少人工巡检成本。

### 挑战

- **环境复杂性**：温室内不同区域的微气候差异显著，需要分区精细控制
- **作物生长阶段**：不同生长阶段（苗期、开花期、结果期）对环境参数的需求不同
- **灌溉决策**：灌溉时机和水量需要综合土壤水分、蒸腾量、天气预报等多维度数据
- **病害预警**：高温高湿是灰霉病、白粉病的温床，需要提前预警

### 解决方案

**数据模型**：

```sql
CREATE MEASUREMENT greenhouse_env (
    greenhouse_id TAG,
    zone_id       TAG,   -- 大棚内分区（A/B/C/D）
    crop_type     TAG,
    growth_stage  TAG,   -- seedling / flowering / fruiting
    temperature   FIELD FLOAT,
    humidity      FIELD FLOAT,
    co2_ppm       FIELD FLOAT,
    light_lux     FIELD FLOAT,
    soil_moisture FIELD FLOAT,  -- 土壤含水率 %
    soil_ec       FIELD FLOAT,  -- 土壤电导率（肥力指标）
    leaf_temp     FIELD FLOAT   -- 叶面温度（红外传感器）
);
```

**实时环境监控**：

```sql
-- 各大棚当前环境状态概览
SELECT greenhouse_id, zone_id,
       last(temperature)   AS temp,
       last(humidity)      AS humidity,
       last(co2_ppm)       AS co2,
       last(soil_moisture) AS soil_moisture,
       -- 温湿度综合舒适度指数（草莓最适 18-22°C，60-80% RH）
       CASE 
           WHEN last(temperature) BETWEEN 18 AND 22 
                AND last(humidity) BETWEEN 60 AND 80 THEN '最适'
           WHEN last(temperature) BETWEEN 15 AND 25 THEN '适宜'
           ELSE '需调节'
       END AS comfort_level
FROM greenhouse_env
WHERE time > NOW() - INTERVAL '5m'
GROUP BY greenhouse_id, zone_id;
```

**PID 温度控制**：通过 `pid_series()` 精确控制通风机和加热器的输出，维持最适温度。

```sql
-- 草莓大棚 GH-012 A 区的温度 PID 控制（目标 20°C）
SELECT time, zone_id,
       temperature AS actual_temp,
       pid_series(20.0, temperature, time, 2.0, 0.3, 0.15) AS ventilation_output
       -- 正值：开通风降温；负值：开加热升温
FROM greenhouse_env
WHERE greenhouse_id = 'GH-012'
  AND zone_id = 'A'
  AND time > NOW() - INTERVAL '1h';
```

**智能灌溉决策**：综合土壤水分趋势和蒸腾速率，计算最优灌溉时机。

```sql
-- 计算土壤水分下降速率（蒸腾量指标），预测何时需要灌溉
SELECT greenhouse_id, zone_id,
       last(soil_moisture)                    AS current_moisture,
       derivative(soil_moisture, 1h)          AS moisture_drop_per_hour,
       -- 预计多少小时后降到灌溉阈值（35%）
       (last(soil_moisture) - 35.0) / 
           ABS(NULLIF(derivative(soil_moisture, 1h), 0)) AS hours_to_irrigation
FROM greenhouse_env
WHERE crop_type = 'strawberry'
  AND growth_stage = 'fruiting'
  AND time > NOW() - INTERVAL '3h'
GROUP BY greenhouse_id, zone_id
HAVING hours_to_irrigation < 4  -- 4 小时内需要灌溉
ORDER BY hours_to_irrigation ASC;
```

**病害风险预警**：高温高湿持续时间是灰霉病爆发的关键指标。

```sql
-- 检测高温高湿持续时长（灰霉病风险：温度 > 20°C 且湿度 > 85% 持续 > 4 小时）
SELECT greenhouse_id, zone_id,
       state_duration(
           CASE WHEN temperature > 20 AND humidity > 85 THEN 1 ELSE 0 END,
           1
       ) / 3600 AS high_risk_hours
FROM greenhouse_env
WHERE time > NOW() - INTERVAL '24h'
GROUP BY greenhouse_id, zone_id
HAVING state_duration(
    CASE WHEN temperature > 20 AND humidity > 85 THEN 1 ELSE 0 END, 1
) / 3600 > 4;
```

**光照积累分析**：计算每日有效光照积累量（DLI），指导补光灯开关策略。

```sql
-- 计算今日各大棚的日光照积累量（DLI，mol/m²/day）
SELECT greenhouse_id,
       -- 光照强度（lux）转换为 PAR（μmol/m²/s），再积分得 DLI
       integral(light_lux * 0.0185, 1s) / 1000000 AS dli_mol_per_m2
FROM greenhouse_env
WHERE time >= strftime('%Y-%m-%d 00:00:00', NOW())
  AND time <= NOW()
GROUP BY greenhouse_id;
```

**生长阶段对比分析**：不同生长阶段的环境参数对产量的影响。

```sql
-- 分析各生长阶段的平均环境参数（用于优化种植方案）
SELECT growth_stage,
       avg(temperature)   AS avg_temp,
       avg(humidity)      AS avg_humidity,
       avg(co2_ppm)       AS avg_co2,
       avg(soil_moisture) AS avg_soil_moisture,
       avg(light_lux)     AS avg_light,
       count(DISTINCT greenhouse_id) AS greenhouse_count
FROM greenhouse_env
WHERE crop_type = 'strawberry'
  AND time > NOW() - INTERVAL '90d'
GROUP BY growth_stage;
```

**Holt-Winters 温度预测**：预测未来 24 小时温度趋势，提前调整通风策略。

```sql
-- 预测 GH-012 未来 24 小时温度趋势（每小时一个预测点）
SELECT *
FROM forecast(
    SELECT avg(temperature) AS hourly_temp
    FROM greenhouse_env
    WHERE greenhouse_id = 'GH-012'
    GROUP BY time(1h),
    24,
    'holt_winters'
);
```

### 嵌入式边缘部署

每座大棚配备一台树莓派 4B 运行嵌入式 SonnetDB，即使网络中断也能持续本地控制：

```csharp
// 大棚边缘控制器
using var db = Tsdb.Open("/data/greenhouse");
var executor = db.GetExecutor();

// 每 30 秒采集一次传感器数据
while (true)
{
    var sensors = await ReadSensorsAsync();
    await executor.ExecuteAsync(
        "INSERT INTO greenhouse_env (time, greenhouse_id, zone_id, temperature, humidity, soil_moisture) " +
        "VALUES (@t, @gid, @zid, @temp, @hum, @sm)",
        new { t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
              gid = "GH-012", zid = "A",
              temp = sensors.Temperature, hum = sensors.Humidity, sm = sensors.SoilMoisture }
    );
    
    // 本地 PID 计算，直接控制执行机构
    var pidResult = await executor.QueryAsync(
        "SELECT pid_series(20.0, temperature, time, 2.0, 0.3, 0.15) AS output " +
        "FROM greenhouse_env WHERE greenhouse_id = 'GH-012' AND zone_id = 'A' " +
        "AND time > NOW() - INTERVAL '5m' ORDER BY time DESC LIMIT 1"
    );
    await SetVentilationAsync(pidResult.First().output);
    
    await Task.Delay(30_000);
}
```

### 实施效果

| 指标 | 上线前 | 上线后 |
| --- | --- | --- |
| 灌溉用水量/亩/季 | 180 吨 | 126 吨（↓30%） |
| 化肥施用量 | 基准 | ↓22%（精准施肥） |
| 灰霉病发病率 | 8.3% | 2.1% |
| 草莓亩产量 | 2800 kg | 3350 kg（↑20%） |
| 人工巡检频次 | 每天 3 次/棚 | 每天 1 次/棚（异常才派人） |
| 边缘断网可用性 | 断网即停控 | 本地持续运行 |

精准农业的核心是"数据驱动决策"。SonnetDB 将传感器数据、PID 控制、预测分析融为一体，让农业生产从"凭经验"走向"凭数据"。
