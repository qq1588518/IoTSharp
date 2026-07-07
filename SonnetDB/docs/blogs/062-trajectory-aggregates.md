## 轨迹聚合分析：全面洞察移动数据

轨迹数据分析不仅仅停留在查看单个坐标点的层面，更重要的是从整体上理解移动模式。SonnetDB 提供了一套丰富的轨迹聚合函数，包括 `trajectory_length()`、`trajectory_centroid()`、`trajectory_bbox()`、`trajectory_speed_max()`、`trajectory_speed_avg()` 和 `trajectory_speed_p95()`，使开发者能够通过 GROUP BY 时间窗口快速获取轨迹的整体统计特征。

### 聚合函数速览

每个轨迹聚合函数都服务于特定的分析目的。`trajectory_length()` 计算轨迹总长度（米），用于衡量行驶里程。`trajectory_centroid()` 返回轨迹的中心点坐标，可用于聚类分析或热点识别。`trajectory_bbox()` 计算轨迹的包围盒（最小外接矩形），直观地展示移动范围。速度相关的三个函数则分别从最大值、平均值和 P95 分位值三个角度描述速度分布特征。

```sql
-- 按小时聚合轨迹统计信息
SELECT 
    date_trunc('hour', time) AS hour_slot,
    device_id,
    trajectory_length(position) AS total_distance_m,
    trajectory_centroid(position) AS center_point,
    trajectory_bbox(position) AS bounding_box,
    trajectory_speed_max(position, time) AS max_speed_ms,
    trajectory_speed_avg(position, time) AS avg_speed_ms,
    trajectory_speed_p95(position, time) AS p95_speed_ms
FROM trajectory_data
WHERE time >= now() - INTERVAL '24 hours'
GROUP BY hour_slot, device_id;
```

### 车队管理与运营优化

在车队管理场景中，这些聚合函数的价值尤为突出。运营经理可以通过 `trajectory_speed_p95()` 快速识别有超速倾向的驾驶员，利用 `trajectory_length()` 统计每日总行驶里程，并结合 `trajectory_bbox()` 了解车辆的作业覆盖范围。将这些信息整合到仪表盘中，可以形成全面的车队运营视图。

```sql
-- 识别高风险驾驶行为
SELECT 
    device_id,
    trajectory_speed_p95(position, time) * 3.6 AS p95_speed_kmh,
    trajectory_speed_max(position, time) * 3.6 AS max_speed_kmh
FROM trajectory_data
WHERE time >= now() - INTERVAL '7 days'
GROUP BY device_id
HAVING trajectory_speed_p95(position, time) * 3.6 > 100;
```

### 野生动物追踪应用

在生态学研究领域，轨迹聚合函数同样大有用武之地。研究人员可以使用 `trajectory_centroid()` 追踪野生动物活动范围的季节性变化，通过 `trajectory_bbox()` 估算栖息地面积，利用 `trajectory_length()` 统计每日迁徙距离。这些分析能够为生态保护决策提供有力的数据支持。

```sql
-- 月度活动范围分析
SELECT 
    date_trunc('month', time) AS month,
    animal_id,
    trajectory_length(position) / 1000 AS total_km,
    trajectory_centroid(position) AS home_range_center,
    trajectory_bbox(position) AS activity_area
FROM animal_tracking
WHERE species = 'wolf'
GROUP BY month, animal_id;
```

通过合理组合这些轨迹聚合函数，开发者可以在单一 SQL 查询中完成从前需要多步骤编程实现的复杂分析任务，大幅提升数据处理效率。
