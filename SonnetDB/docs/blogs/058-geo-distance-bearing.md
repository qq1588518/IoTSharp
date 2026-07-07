## 地理距离与方位角计算：geo_distance 与 geo_bearing

在地理空间分析中，距离和方向是两个最基本的度量。SonnetDB 提供了 `geo_distance()` 和 `geo_bearing()` 两个标量函数，分别基于 Haversine 公式和大圆方位角算法实现。

### geo_distance：Haversine 大圆距离

`geo_distance(point1, point2)` 基于 Haversine 公式计算两个 GEOPOINT 之间的大圆距离，单位为米。它通过 `FunctionRegistry` 注册为内置标量函数：

```csharp
new BuiltInScalarFunction("geo_distance", 2, 2, EvaluateGeoDistance),
```

```sql
-- 计算北京到上海的大圆距离
SELECT geo_distance(
    POINT(39.9042, 116.4074),
    POINT(31.2304, 121.4737)
) AS distance_m;
```

结果约 1067 公里，与真实距离高度吻合。`geo_distance` 在内部使用优化的 Haversine 算法，在保持计算精度的同时确保良好的执行性能。

### 实用查询示例

结合 `LAG` 窗口函数，可以计算车辆行驶的逐段距离和总里程：

```sql
-- 计算车辆总行驶里程
SELECT device_id,
       SUM(geo_distance(
           LAG(position) OVER (ORDER BY time),
           position
       )) AS total_distance_m,
       SUM(geo_distance(
           LAG(position) OVER (ORDER BY time),
           position
       )) / 1000.0 AS total_distance_km
FROM gps_tracks
WHERE device_id = 'vehicle-01'
  AND time >= '2025-06-01'
GROUP BY device_id;
```

### geo_bearing：初始方位角

`geo_bearing(point1, point2)` 计算从 point1 指向 point2 的初始方位角，单位为度，正北为 0 度，顺时针递增：

```sql
-- 计算从北京到上海的初始方位角
SELECT geo_bearing(
    POINT(39.9042, 116.4074),
    POINT(31.2304, 121.4737)
) AS bearing_deg;
```

结果约为 118 度（东南方向）。方位角在导航、路径规划和定向分析中非常有用。

### 组合使用：距离 + 方向

两个函数经常一起使用，全面描述两点间的空间关系：

```sql
-- 为每条记录计算相对于参考点的距离和方向
SELECT time, device_id,
       geo_distance(position, POINT(39.90, 116.40)) AS distance_m,
       geo_bearing(POINT(39.90, 116.40), position) AS bearing_deg
FROM gps_tracks
WHERE time >= now() - INTERVAL '30 minutes'
ORDER BY distance_m;
```

### 性能建议

`geo_distance()` 和 `geo_bearing()` 都是纯计算函数，不依赖索引。当需要对大量数据进行距离过滤时，建议结合 GeoHash 索引先使用 `geo_within()` 粗筛，再使用 `geo_distance()` 精算：

```sql
-- 高效的距离查询：先粗筛再精算
SELECT time, device_id, position,
       geo_distance(position, POINT(39.90, 116.40)) AS exact_dist
FROM gps_tracks
WHERE geo_within(position, 39.90, 116.40, 10000)
  AND geo_distance(position, POINT(39.90, 116.40)) <= 5000;
```

这两个函数构成了地理空间分析的基础，无论是计算配送距离、追踪运动方向，还是分析轨迹模式，都离不开它们。
