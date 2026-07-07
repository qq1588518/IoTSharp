## PostGIS 兼容函数：ST_Distance / ST_Within / ST_DWithin

对于从 PostGIS 迁移到 SonnetDB 的用户，兼容性是最关心的问题之一。SonnetDB 提供了 ST_ 系列兼容函数映射，让熟悉 PostGIS 的开发者可以沿用已有的 SQL 经验。

### 兼容函数映射表

SonnetDB 实现了三个最常用的 PostGIS 空间函数别名，内部映射到对应的内置函数：

| PostGIS 函数 | SonnetDB 映射 | 说明 |
|-------------|--------------|------|
| `ST_Distance(A, B)` | `geo_distance(A, B)` | 返回两点间大圆距离（米） |
| `ST_Within(A, B, radius)` | `geo_within(A, B, radius)` | 判断点是否在圆形区域内 |
| `ST_DWithin(A, B, radius)` | `geo_within(A, B, radius)` | 判断两点距离是否小于半径 |

### ST_Distance 兼容

`ST_Distance` 返回两个 GEOPOINT 之间的最短距离，单位为米。在内部使用 Haversine 公式计算，与 PostGIS 的 geography 模式结果一致：

```sql
-- PostGIS 风格
SELECT ST_Distance(
    ST_MakePoint(116.4074, 39.9042)::geography,
    ST_MakePoint(121.4737, 31.2304)::geography
);

-- SonnetDB 风格
SELECT ST_Distance(
    POINT(39.9042, 116.4074),
    POINT(31.2304, 121.4737)
);
```

**关键差异**：PostGIS 使用 `ST_MakePoint(lon, lat)`（经度在前），SonnetDB 使用 `POINT(lat, lon)`（纬度在前）。迁移时需注意调整参数顺序。

### ST_Within 与 ST_DWithin

`ST_Within` 判断点是否在圆形区域内，等价于 PostGIS 中将点与缓冲区比较。`ST_DWithin` 判断两点距离是否小于给定半径。两者在 SonnetDB 中都映射到 `geo_within`：

```sql
-- PostGIS: 查找 5 公里内的 POI
SELECT id, name FROM pois
WHERE ST_DWithin(
    geom::geography,
    ST_SetSRID(ST_MakePoint(116.40, 39.90), 4326)::geography,
    5000
);

-- SonnetDB: 完全等价的查询
SELECT id, name FROM points_of_interest
WHERE ST_DWithin(location, POINT(39.90, 116.40), 5000);
```

### 完整迁移对比

| 功能 | PostGIS | SonnetDB |
|------|---------|----------|
| 坐标顺序 | (lng, lat) | (lat, lng) |
| 距离计算 | `ST_Distance(geog1, geog2)` | `geo_distance` / `ST_Distance` |
| 圆形范围 | `ST_DWithin(geog, geog, r)` | `geo_within` / `ST_Within` / `ST_DWithin` |
| 矩形范围 | `geom && ST_MakeEnvelope(...)` | `geo_bbox(p, min_lat, min_lon, max_lat, max_lon)` |
| 坐标提取 | `ST_X`, `ST_Y` | `lon()`, `lat()` |
| 数据类型 | `geometry` / `geography` | `GEOPOINT` |
| 空间索引 | GIST index | GeoHash index |

### 迁移建议

1. 逐一检查所有坐标参数顺序，将 `(lng, lat)` 调整为 `(lat, lng)`
2. 将数据类型 `geometry` / `geography` 替换为 `GEOPOINT`
3. 将 GIST 索引创建语句替换为 `CREATE INDEX ... WITH (index_type = 'geohash', precision = N)`
4. 使用 `ST_Distance` / `ST_DWithin` 别名在迁移过渡期保持代码可读性

通过这套兼容别名，PostGIS 用户可以快速上手 SonnetDB，在享受时序数据库高性能写入的同时复用已有的 GIS 查询逻辑。
