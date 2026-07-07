## 坐标分量提取：lat() 和 lon() 函数详解

`GEOPOINT` 类型将纬度和经度打包为一个复合值。当需要在查询中单独使用纬度或经度时，SonnetDB 提供了 `lat()` 和 `lon()` 两个提取函数，从 GEOPOINT 值中解析出坐标分量。

### 函数签名与基本用法

`lat()` 返回纬度分量（类型 DOUBLE），`lon()` 返回经度分量（类型 DOUBLE）。两个函数都接收一个 GEOPOINT 参数：

```sql
-- 提取经纬度分量
SELECT time, device_id,
       lat(position) AS latitude,
       lon(position) AS longitude
FROM gps_tracks
WHERE device_id = 'device-001'
ORDER BY time
LIMIT 10;
```

### 在 WHERE 条件中过滤

`lat()` 和 `lon()` 可以直接出现在过滤条件中，实现矩形区域筛选。这对于快速定位某个地理范围内的数据点非常实用：

```sql
-- 筛选北京市区范围内的轨迹点
SELECT time, device_id, position, speed
FROM gps_tracks
WHERE lat(position) BETWEEN 39.80 AND 40.10
  AND lon(position) BETWEEN 116.20 AND 116.60
ORDER BY time;
```

### 与聚合函数结合

结合聚合函数，可以计算轨迹的中心点、坐标范围等统计信息：

```sql
-- 计算设备轨迹的统计信息
SELECT device_id,
       AVG(lat(position)) AS center_lat,
       AVG(lon(position)) AS center_lon,
       MIN(lat(position)) AS min_lat,
       MAX(lat(position)) AS max_lat,
       MIN(lon(position)) AS min_lon,
       MAX(lon(position)) AS max_lon,
       COUNT(*) AS point_count
FROM gps_tracks
WHERE time >= now() - INTERVAL '1 hour'
GROUP BY device_id;
```

### 与 GROUP BY 配合使用

使用 `lat()` 和 `lon()` 对坐标进行离散化分组，实现地理网格的统计聚合：

```sql
-- 按 0.01 度网格统计轨迹点数量
SELECT ROUND(lat(position), 2) AS grid_lat,
       ROUND(lon(position), 2) AS grid_lon,
       COUNT(*) AS point_count
FROM gps_tracks
WHERE time >= now() - INTERVAL '1 day'
GROUP BY ROUND(lat(position), 2), ROUND(lon(position), 2)
ORDER BY point_count DESC;
```

### 性能建议

使用 `lat()` 和 `lon()` 进行范围过滤时，如果有 GeoHash 索引，SonnetDB 会尝试自动利用索引进行预过滤：

```sql
-- 创建 GeoHash 索引以加速 lat/lon 范围查询
CREATE INDEX idx_geo ON gps_tracks (position)
WITH (index_type = 'geohash', precision = 6);

-- 此查询将尝试利用 GeoHash 索引加速
SELECT * FROM gps_tracks
WHERE lat(position) BETWEEN 39.0 AND 41.0
  AND lon(position) BETWEEN 115.0 AND 117.0;
```

但需要注意的是，`lat()`/`lon()` 的矩形过滤属于粗筛方法。如果需要进行圆形半径过滤，应使用 `geo_within()` 函数，后者的索引加速效果更优。

### 与 POINT() 构造函数的区别

`lat()` 和 `lon()` 是提取函数，不能反向用于插入。构造 GEOPOINT 使用 `POINT(lat, lon)`：

```sql
-- 错误：lat()/lon() 不是构造函数
INSERT INTO gps_tracks (position) VALUES (lat(39.9), lon(116.4));

-- 正确：使用 POINT() 构造函数
INSERT INTO gps_tracks (position) VALUES (POINT(39.9, 116.4));
```

`lat()` 和 `lon()` 是最基础的地理空间函数，配合聚合运算和 WHERE 条件，可以灵活地对坐标进行分解和分析，为更复杂的地理空间计算奠定基础。
