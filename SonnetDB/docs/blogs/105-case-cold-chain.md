## 案例：冷链物流——全程温湿度监控与超标告警系统

### 背景

某医药冷链物流公司承运疫苗、血液制品、生物药品等对温度极度敏感的货物。国家药监局规定，疫苗运输全程须保持 2-8°C，超出范围的货物必须追溯记录并可能需要销毁处置。公司每天运营约 3000 辆冷藏车，每辆车有 3-5 个温度传感器，还需要监控冷库、中转站的温湿度数据。

### 挑战

- **合规要求**：每批次货物需要完整的温湿度链记录，数据必须不可篡改、可追溯
- **实时告警**：超温须在 5 分钟内通知司机和调度中心，否则可能损失整批货物
- **异常分析**：需要区分传感器故障、门封不严、制冷故障等不同异常原因
- **报告生成**：每批次货物交接时需要自动生成温控合规报告（PDF）

### 解决方案

每辆冷藏车内置 4G 数传模块，每 30 秒上报一次数据。公司总部部署 SonnetDB 服务端，冷库现场部署嵌入式 SonnetDB 作为本地缓存（网络中断时不丢数据）。

**数据模型**：

```sql
CREATE MEASUREMENT cold_chain_temp (
    vehicle_id  TAG,
    sensor_id   TAG,
    batch_id    TAG,   -- 货批 ID，用于追溯
    cargo_type  TAG,   -- vaccine / blood / biologics
    location    GEOPOINT,
    temperature FIELD FLOAT,
    humidity    FIELD FLOAT,
    door_open   FIELD INT,   -- 车门开关状态 0/1
    battery_pct FIELD FLOAT  -- 传感器电量
);
```

**实时超温检测**：

```sql
-- 检测最近 5 分钟有温度超标的车辆（疫苗标准 2-8°C）
SELECT vehicle_id, sensor_id, batch_id,
       last(temperature)   AS current_temp,
       last(location)      AS current_pos,
       max(temperature)    AS peak_temp,
       count(*) FILTER (WHERE temperature > 8 OR temperature < 2) AS out_of_range_count
FROM cold_chain_temp
WHERE cargo_type = 'vaccine'
  AND time > NOW() - INTERVAL '5m'
GROUP BY vehicle_id, sensor_id, batch_id
HAVING count(*) FILTER (WHERE temperature > 8 OR temperature < 2) > 0;
```

**告警升级逻辑**：持续超温时间越长，告警等级越高。

```sql
-- 统计各车辆连续超温时长（分钟）
SELECT vehicle_id, batch_id,
       state_duration(
           CASE WHEN temperature > 8 OR temperature < 2 THEN 1 ELSE 0 END,
           1
       ) / 60 AS out_of_range_minutes
FROM cold_chain_temp
WHERE time > NOW() - INTERVAL '2h'
GROUP BY vehicle_id, batch_id
HAVING state_duration(
    CASE WHEN temperature > 8 OR temperature < 2 THEN 1 ELSE 0 END, 1
) / 60 > 5;  -- 持续超温超过 5 分钟升级为紧急告警
```

**开门影响分析**：区分"开门导致的短暂升温"与"制冷系统故障"。

```sql
-- 检测开门事件及随后的温度恢复曲线
SELECT time, vehicle_id,
       temperature,
       door_open,
       -- 门关闭后温度是否在 10 分钟内恢复正常
       lead(temperature, 20) OVER (PARTITION BY vehicle_id ORDER BY time) AS temp_10min_later
FROM cold_chain_temp
WHERE vehicle_id = 'VEH-0523'
  AND time > NOW() - INTERVAL '4h'
ORDER BY time;
```

**批次合规报告**：货物交接时生成完整温控记录。

```sql
-- 某批次（BATCH-20260415-001）的完整温控统计
SELECT batch_id,
       min(time)          AS trip_start,
       max(time)          AS trip_end,
       min(temperature)   AS min_temp,
       max(temperature)   AS max_temp,
       avg(temperature)   AS avg_temp,
       percentile(temperature, 95) AS p95_temp,
       -- 超标样本数和占比
       count(*) FILTER (WHERE temperature > 8 OR temperature < 2) AS excursion_count,
       count(*) AS total_count,
       count(*) FILTER (WHERE temperature > 8 OR temperature < 2) * 100.0 
           / count(*) AS excursion_pct
FROM cold_chain_temp
WHERE batch_id = 'BATCH-20260415-001'
GROUP BY batch_id;
```

**轨迹重放**：结合 GEOPOINT 数据，可以在地图上回放货物的完整运输路径，并标注超温区间。

```sql
-- 获取批次的地理轨迹数据（每 2 分钟一个轨迹点）
SELECT time,
       lat(location)  AS lat,
       lon(location)  AS lng,
       temperature,
       CASE WHEN temperature > 8 OR temperature < 2 THEN 'red' ELSE 'green' END AS color
FROM cold_chain_temp
WHERE batch_id = 'BATCH-20260415-001'
  AND sensor_id = 'S1'
ORDER BY time;
```

**传感器故障识别**：通过 IQR 方法检测传感器突变（区别于真实温度变化）。

```sql
-- 检测传感器数据跳变（可能是传感器故障而非真实温度变化）
SELECT time, vehicle_id, sensor_id, temperature,
       difference(temperature) AS temp_diff,
       anomaly(difference(temperature), 'iqr', 3.0) AS is_sensor_fault
FROM cold_chain_temp
WHERE time > NOW() - INTERVAL '1h'
  AND anomaly(difference(temperature), 'iqr', 3.0) = 1;
```

### 边缘侧断网缓存

车载 4G 信号不稳定时，嵌入式 SonnetDB 在本地缓存数据：

```csharp
// 车载系统：网络恢复后批量同步本地数据
using var localDb = Tsdb.Open("/data/local_cache");
var pending = await localDb.GetExecutor().QueryAsync(
    "SELECT * FROM cold_chain_temp WHERE synced = 0 ORDER BY time LIMIT 1000"
);
// 上传至总部，成功后标记为已同步
await UploadToHeadquartersAsync(pending);
```

### 实施效果

| 指标 | 上线前 | 上线后 |
| --- | --- | --- |
| 超温事件平均发现时延 | 2-4 小时（到站后人工检查） | 3 分钟（实时告警） |
| 因超温销毁货物批次/月 | 4-6 批 | 0-1 批 |
| 合规报告生成时间 | 30 分钟（人工整理） | 30 秒（自动查询） |
| 药监局飞行检查合格率 | 78% | 100% |
| 客户理赔纠纷 | 每月 3-5 起 | 每月 0 起（有完整记录） |

冷链合规是医药物流的生命线。SonnetDB 将 100% 的温控数据实时落盘、可追溯，让公司从"事后被动处理"变成了"实时主动防范"。
