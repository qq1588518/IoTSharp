## 案例：光伏电站运维——基于 SonnetDB 的发电量异常检测

### 背景

某新能源集团运营着分布在华北、西北的 12 座光伏电站，总装机容量 800MW。每座电站包含数千块光伏板，每块组串配有逆变器，实时采集电压、电流、功率、温度等指标。集团的核心诉求是：在不派人巡检的情况下，能够及时发现"躺平组串"（发电量异常低的组串），减少因遮挡、热斑、老化导致的电量损失。

### 挑战

- **数据体量**：12 座电站 × 平均 2000 台逆变器 × 每 5 分钟上报一次，每天约 3400 万条记录
- **异常检测精度**：需要区分"天气因素导致的整体发电量下降"与"单台设备故障"，误报率要低
- **长期趋势**：光伏板随时间老化，需要逐年对比衰减率，预测剩余寿命
- **地理可视化**：运维人员需要直观看到哪片区域的组串出现异常

### 解决方案

**数据模型**：每座电站一个数据库，逆变器 ID、组串 ID 作为 TAG，位置信息存储为 GEOPOINT。

```sql
-- 电站 SX-FARM-01 数据库的主数据表
CREATE MEASUREMENT inverter_metrics (
    inverter_id   TAG,
    string_id     TAG,
    station_id    TAG,
    location      GEOPOINT,
    dc_voltage    FIELD FLOAT,
    dc_current    FIELD FLOAT,
    ac_power      FIELD FLOAT,
    temperature   FIELD FLOAT,
    irradiance    FIELD FLOAT   -- 组串级辐照度（来自气象站插值）
);
```

**核心思路——归一化发电效能**：直接比较各组串的绝对发电量会受天气影响，因此采用"实际效能 / 理论最大效能"的归一化指标（Performance Ratio，PR）。

```sql
-- 计算各组串过去 1 小时的效能比 (PR)
SELECT string_id,
       avg(ac_power) / (avg(irradiance) * 0.18 * 1.6) AS performance_ratio,
       -- 0.18 为转换效率标称值，1.6 为组串面积（㎡）
       avg(temperature)                                AS avg_temp,
       count(*)                                        AS sample_count
FROM inverter_metrics
WHERE station_id = 'SX-FARM-01'
  AND time > NOW() - INTERVAL '1h'
  AND irradiance > 100  -- 排除清晨/傍晚低辐照时段
GROUP BY string_id
ORDER BY performance_ratio ASC;
```

**MAD 异常检测**：用中位数绝对偏差方法识别低效组串，对天气影响具有鲁棒性。

```sql
-- 检测当前效能比显著偏低的组串
SELECT string_id, location,
       performance_ratio,
       anomaly(performance_ratio, 'mad', 2.5) AS is_anomaly
FROM (
    SELECT string_id, location,
           avg(ac_power) / NULLIF(avg(irradiance) * 0.288, 0) AS performance_ratio
    FROM inverter_metrics
    WHERE station_id = 'SX-FARM-01'
      AND time > NOW() - INTERVAL '2h'
      AND irradiance > 200
    GROUP BY string_id, location
)
WHERE anomaly(performance_ratio, 'mad', 2.5) = 1;
```

**地理可视化**：将异常组串的位置直接返回给 Web 地图，运维人员在地图上点击即可定位。

```sql
-- 返回异常组串的地理坐标，供 Web 地图标注
SELECT string_id,
       lat(location) AS lat,
       lon(location) AS lng,
       performance_ratio,
       '异常' AS alert_type
FROM anomaly_strings  -- 上面查询的结果作为子查询或临时视图
ORDER BY performance_ratio ASC;
```

**长期衰减趋势预测**：按年统计电站发电量，预测未来 3 年的衰减。

```sql
-- 逐年发电总量与效能比趋势（2022-2025）
SELECT 
    strftime('%Y', time) AS year,
    sum(ac_power) * 5 / 60 / 1000  AS annual_kwh,  -- 5分钟采样转kWh
    avg(ac_power) / avg(irradiance) AS avg_pr
FROM inverter_metrics
WHERE station_id = 'SX-FARM-01'
GROUP BY year
ORDER BY year;
```

```sql
-- Holt-Winters 预测未来 3 年效能比趋势（年度数据作为输入）
SELECT *
FROM forecast(
    SELECT avg(ac_power) / avg(irradiance) AS pr
    FROM inverter_metrics
    WHERE station_id = 'SX-FARM-01'
    GROUP BY time(30d),
    36,
    'holt_winters'
);
```

**温度-功率关联**：验证高温对发电效率的影响，量化温度系数。

```sql
-- 分温度区间分析平均输出功率（每 5°C 一组）
SELECT 
    round(temperature / 5) * 5 AS temp_bucket,
    avg(ac_power)               AS avg_power,
    count(*)                    AS sample_count
FROM inverter_metrics
WHERE time > NOW() - INTERVAL '30d'
  AND irradiance BETWEEN 600 AND 700  -- 控制辐照度变量
GROUP BY temp_bucket
ORDER BY temp_bucket;
```

### AI Copilot 集成

运维平台集成了 SonnetDB Copilot，运维工程师可以直接用自然语言提问：

> "上个月 SX-FARM-03 的发电量比计划低了 8%，主要是哪些组串导致的？"

Copilot 自动生成月度组串效能分析 SQL，识别出 17 块长期低效组串，并推断其中 9 块可能存在热斑问题（基于温度异常高、功率异常低的特征）。

### 实施效果

| 指标 | 改造前 | 改造后 |
| --- | --- | --- |
| 异常发现时效 | 次日人工巡检 | 实时（15 分钟内） |
| 年度电量损失 | 约 2.1% | 约 0.6% |
| 巡检人力 | 每站每周 2 人次 | 每站每月 1 人次（精准派单） |
| 数据分析覆盖范围 | 抽样 10% 设备 | 全量 100% 设备 |

12 座电站的 800MW 容量，每减少 1% 的电量损失，年增收约 600 万元。SonnetDB 的异常检测与地理空间可视化组合，将运维从"巡逻式"变成了"精准制导"。
