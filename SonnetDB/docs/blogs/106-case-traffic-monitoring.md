## 案例：城市交通监控——路口车流量时序分析与拥堵预测

### 背景

某省会城市交通管理局在主城区 1200 个路口部署了视频检测设备，每个路口每分钟统计一次各方向的车流量、平均车速、排队长度。交管局希望基于这些数据实现：实时拥堵感知、信号灯配时优化、节假日出行预测，以及事故快速响应。

### 挑战

- **数据规模**：1200 路口 × 4 方向 × 每分钟上报，每天约 700 万条记录
- **实时性**：信号灯配时调整需要在 30 秒内响应当前流量变化
- **预测精度**：节假日出行预测需要结合历史同期数据，考虑天气、节假日等因素
- **地理分析**：需要识别"拥堵传播路径"——某路口拥堵如何蔓延到周边路口

### 解决方案

**数据模型**：

```sql
CREATE MEASUREMENT traffic_flow (
    intersection_id TAG,
    direction       TAG,   -- N/S/E/W
    road_name       TAG,
    district        TAG,
    location        GEOPOINT,
    vehicle_count   FIELD INT,
    avg_speed_kmh   FIELD FLOAT,
    queue_length_m  FIELD FLOAT,
    occupancy_pct   FIELD FLOAT  -- 车道占用率
);
```

**实时拥堵指数计算**：

```sql
-- 当前各路口拥堵指数（基于速度和占用率综合计算）
SELECT intersection_id, road_name,
       lat(location) AS lat, lon(location) AS lng,
       avg(avg_speed_kmh)   AS current_speed,
       avg(occupancy_pct)   AS current_occupancy,
       -- 拥堵指数：速度越低、占用率越高，指数越高
       (1 - avg(avg_speed_kmh) / 60.0) * 0.6 
           + avg(occupancy_pct) / 100.0 * 0.4 AS congestion_index
FROM traffic_flow
WHERE time > NOW() - INTERVAL '3m'
GROUP BY intersection_id, road_name, location
ORDER BY congestion_index DESC
LIMIT 20;
```

**信号灯配时优化**：基于当前流量动态调整绿灯时长。

```sql
-- 计算某路口各方向的流量比，用于绿灯时长分配
SELECT direction,
       sum(vehicle_count)                                    AS total_flow,
       sum(vehicle_count) * 100.0 / sum(sum(vehicle_count)) 
           OVER ()                                           AS flow_ratio_pct,
       -- 建议绿灯时长（总周期 120 秒，按流量比分配）
       round(sum(vehicle_count) * 120.0 / sum(sum(vehicle_count)) OVER ()) AS suggested_green_sec
FROM traffic_flow
WHERE intersection_id = 'INT-0523'
  AND time > NOW() - INTERVAL '5m'
GROUP BY direction;
```

**高峰时段识别**：分析工作日各时段的流量规律。

```sql
-- 工作日各小时平均流量（过去 90 天）
SELECT strftime('%H', time) AS hour_of_day,
       avg(vehicle_count)   AS avg_flow,
       max(vehicle_count)   AS peak_flow,
       percentile(vehicle_count, 90) AS p90_flow
FROM traffic_flow
WHERE intersection_id = 'INT-0523'
  AND direction = 'N'
  AND strftime('%w', time) BETWEEN '1' AND '5'
  AND time > NOW() - INTERVAL '90d'
GROUP BY hour_of_day
ORDER BY hour_of_day;
```

**节假日流量预测**：

```sql
-- 预测五一假期（5 天）的每小时流量
SELECT *
FROM forecast(
    SELECT avg(vehicle_count) AS hourly_flow
    FROM traffic_flow
    WHERE intersection_id = 'INT-0523'
      AND direction = 'N'
    GROUP BY time(1h),
    120,   -- 预测 120 小时（5 天）
    'holt_winters'
);
```

**拥堵传播分析**：识别某路口拥堵后，哪些周边路口会在多少分钟后受影响。

```sql
-- 分析 INT-0523 拥堵时，周边 1km 内路口的滞后相关性
SELECT b.intersection_id AS downstream_intersection,
       geo_distance(a.location, b.location) AS distance_m,
       -- 计算 INT-0523 拥堵指数与下游路口的时间滞后相关
       corr(a.occupancy_pct, b.occupancy_pct) AS correlation
FROM traffic_flow a
JOIN traffic_flow b ON b.time = a.time + INTERVAL '5m'  -- 5 分钟滞后
WHERE a.intersection_id = 'INT-0523'
  AND ST_DWithin(a.location, b.location, 1000)
  AND a.time > NOW() - INTERVAL '7d'
GROUP BY b.intersection_id, b.location, a.location
ORDER BY correlation DESC;
```

**事故快速响应**：检测车速突降事件（可能是事故）。

```sql
-- 检测过去 10 分钟内车速突降超过 30% 的路口（可能发生事故）
SELECT intersection_id, direction,
       avg_speed_kmh AS current_speed,
       lag(avg_speed_kmh, 3) OVER (PARTITION BY intersection_id, direction ORDER BY time) AS speed_3min_ago,
       (lag(avg_speed_kmh, 3) OVER (PARTITION BY intersection_id, direction ORDER BY time) 
           - avg_speed_kmh) / 
       NULLIF(lag(avg_speed_kmh, 3) OVER (PARTITION BY intersection_id, direction ORDER BY time), 0) 
           AS speed_drop_ratio
FROM traffic_flow
WHERE time > NOW() - INTERVAL '10m'
HAVING speed_drop_ratio > 0.3
ORDER BY speed_drop_ratio DESC;
```

**变点检测**：识别道路施工、事故等导致的流量结构性变化。

```sql
-- 检测 INT-0523 北向流量的结构性变化点
SELECT time, vehicle_count,
       changepoint(vehicle_count, 'cusum', 8.0) AS is_changepoint
FROM traffic_flow
WHERE intersection_id = 'INT-0523'
  AND direction = 'N'
  AND time > NOW() - INTERVAL '30d'
  AND changepoint(vehicle_count, 'cusum', 8.0) = 1;
```

### 地图可视化

Web 管理平台使用 SonnetDB 的 SQL 控制台地图视图，将路口拥堵指数以热力图形式展示。查询返回 GEOPOINT 字段时，系统自动在地图上渲染标注点，颜色按拥堵指数从绿到红渐变。

### 实施效果

| 指标 | 上线前 | 上线后 |
| --- | --- | --- |
| 拥堵感知时延 | 5-10 分钟（人工巡视） | 30 秒（实时计算） |
| 信号灯配时调整频率 | 每季度人工调整 | 每 5 分钟自动优化 |
| 主干道平均通行速度 | 28 km/h | 34 km/h（↑21%） |
| 节假日预测准确率 | — | 87%（MAE < 8%） |
| 事故响应时间 | 平均 12 分钟 | 平均 4 分钟 |

交管局将 SonnetDB 的时序分析能力与地理空间功能结合，实现了从"被动响应"到"主动预测"的跨越，城市道路通行效率显著提升。
