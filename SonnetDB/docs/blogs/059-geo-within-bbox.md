## 地理空间过滤：geo_within 圆形查询与 geo_bbox 矩形查询

在 GIS 应用中，最常见的两种空间过滤方式是圆形半径过滤和矩形范围过滤。SonnetDB 分别通过 `geo_within()` 和 `geo_bbox()` 两个函数实现，两者均可利用 GeoHash 索引加速。

### geo_within：圆形半径过滤

`geo_within(point, center_lat, center_lon, radius_m)` 判断一个 GEOPOINT 是否位于以指定经纬度为中心、给定半径（米）的圆形区域内。函数通过 `FunctionRegistry` 注册，接收 4 个参数：

```csharp
new BuiltInScalarFunction("geo_within", 4, 4, EvaluateGeoWithin),
```

内部实现先用 Haversine 公式计算距离，再与半径比较：`distance <= radius_m`。若半径小于 0 则抛出异常。

```sql
-- 筛选天安门广场周围 3 公里内的所有设备
SELECT time, device_id, position
FROM gps_tracks
WHERE geo_within(position, 39.9042, 116.3974, 3000)
  AND time >= now() - INTERVAL '1 hour';
```

### 多中心半径聚合分析

可以使用 `CASE WHEN` 配合 `geo_within` 实现多区域的热点统计：

```sql
-- 多中心半径查询：同时监控多个关键区域
SELECT
    CASE
        WHEN geo_within(position, 39.9042, 116.3974, 5000) THEN '天安门'
        WHEN geo_within(position, 39.9163, 116.3972, 5000) THEN '故宫'
        WHEN geo_within(position, 39.9929, 116.3914, 5000) THEN '鸟巢'
        ELSE '其他'
    END AS region,
    COUNT(*) AS cnt
FROM gps_tracks
WHERE time >= now() - INTERVAL '1 hour'
GROUP BY region;
```

### geo_bbox：矩形范围过滤

`geo_bbox(point, lat_min, lon_min, lat_max, lon_max)` 判断一个 GEOPOINT 是否位于指定的经纬度矩形范围内。函数接收 5 个参数，要求 `lat_min <= lat_max` 且 `lon_min <= lon_max`：

```csharp
new BuiltInScalarFunction("geo_bbox", 5, 5, EvaluateGeoBbox),
```

```sql
-- 筛选北京市五环内的记录（大致矩形范围）
SELECT time, device_id, position
FROM gps_tracks
WHERE geo_bbox(position, 39.80, 116.20, 40.00, 116.60)
  AND time >= now() - INTERVAL '1 hour';
```

### GeoHash 索引加速

创建 GeoHash 索引后，两个函数都可以利用索引进行预过滤：

```sql
-- 创建 GeoHash 索引
CREATE INDEX idx_geo ON gps_tracks (position)
WITH (index_type = 'geohash', precision = 7);

-- geo_within 和 geo_bbox 会自动使用该索引加速
```

GeoHash 精度为 7 时索引格约 150m；精度为 6 时约 1.2km。精度越高过滤越精确，但索引体积也越大。

### geo_within vs geo_bbox 对比

| 特性 | geo_within | geo_bbox |
|------|-----------|----------|
| 形状 | 圆形 | 轴对齐矩形 |
| 参数 | center_lat, center_lon, radius_m | lat_min, lon_min, lat_max, lon_max |
| 精确度 | 精确 Haversine 距离 | 矩形边界比较 |
| 典型场景 | 周边搜索、电子围栏 | 地图可视区域、区域划分 |

选择建议：需要精确距离控制用 `geo_within`，需要地图视口矩形裁剪用 `geo_bbox`。两者可以组合使用，先用 `geo_bbox` 快速裁剪视口范围，再对结果用 `geo_within` 精筛。
