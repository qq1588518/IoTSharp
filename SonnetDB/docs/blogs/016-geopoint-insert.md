## GEOPOINT 地理空间数据：使用 POINT 语法写入经纬度

随着物联网和移动设备的普及，地理空间数据已成为时序数据库中的重要组成部分。SonnetDB 内置 `GEOPOINT` 数据类型，配合 `POINT(lat, lon)` 字面量语法，让空间数据的写入和查询变得简单直观。

### GEOPOINT 类型与 POINT 字面量

在 SonnetDB 中，`GEOPOINT` 用于表示二维空间中的点坐标。使用 `POINT(lat, lon)` 语法创建地理点字面量——第一个参数是纬度（latitude），第二个参数是经度（longitude）。

```sql
-- 创建包含地理空间数据的时序表
CREATE TABLE vehicle_tracks (
    ts TIMESTAMP NOT NULL,
    vehicle_id TAG,
    location GEOPOINT,
    speed DOUBLE,
    fuel_level DOUBLE
);

-- 插入一条带经纬度的位置记录
INSERT INTO vehicle_tracks (ts, vehicle_id, location, speed)
VALUES ('2025-06-15T08:30:00Z', 'bus-102', POINT(39.9042, 116.4074), 45.2);
```

### POINT 语法的设计考量

`POINT(lat, lon)` 采用纬度在前、经度在后的顺序，这与地理信息系统中的通用惯例（"纬度-经度"顺序）一致。纬度范围是 -90 到 90，经度范围是 -180 到 180。超出范围的值会被拒绝或自动修正。

与传统使用两个独立 DOUBLE 列存储经纬度的方式相比，`GEOPOINT` 类型有以下优势：
- 语义更清晰，表明这是一对不可分割的空间坐标
- 支持空间索引，加速地理范围查询
- 数据存储更紧凑

### 批量插入地理数据

与普通数据一样，GEOPOINT 也支持批量插入：

```sql
INSERT INTO vehicle_tracks (ts, vehicle_id, location, speed)
VALUES
    ('2025-06-15T08:30:00Z', 'bus-102', POINT(39.9042, 116.4074), 45.2),
    ('2025-06-15T08:30:05Z', 'bus-102', POINT(39.9050, 116.4080), 47.1),
    ('2025-06-15T08:30:10Z', 'bus-102', POINT(39.9061, 116.4092), 46.8),
    ('2025-06-15T08:30:15Z', 'bus-103', POINT(39.9100, 116.4200), 30.0);
```

### 在 GEOPOINT 上创建空间索引

为了提高地理范围查询的性能，可以在 GEOPOINT 列上创建空间索引。SonnetDB 使用 Geohash 作为底层索引算法：

```sql
-- 为 location 列创建空间索引
CREATE INDEX idx_location ON vehicle_tracks (location)
WITH INDEX geohash(precision = 7);
```

`precision` 控制 Geohash 编码的精度。精度越高，索引越精细，但占用空间也越大。一般来说，precision=7 对应约 76 米 × 19 米的网格，适合城市级定位；precision=5 对应约 4.9 公里 × 4.9 公里的网格，适合区域级查询。

### 写入后的空间查询示例

数据写入并建索引后，就可以执行丰富的空间查询了：

```sql
-- 查询某辆车在特定时间段内
-- 距离天安门广场（39.9042, 116.4074）500 米范围内的轨迹点
SELECT ts, vehicle_id, speed,
       ST_Distance(location, POINT(39.9042, 116.4074)) AS dist_m
FROM vehicle_tracks
WHERE vehicle_id = 'bus-102'
  AND ST_DWithin(location, POINT(39.9042, 116.4074), 500)
  AND ts >= '2025-06-15T08:00:00Z'
  AND ts < '2025-06-15T09:00:00Z'
ORDER BY ts;
```

### 使用场景

GEOPOINT 类型适用于以下典型场景：
- **车辆轨迹追踪**：记录公交车、物流车辆、共享单车的实时位置
- **环境监测**：气象站、空气监测点的地理坐标
- **移动设备**：手机、可穿戴设备的位置采样
- **地理围栏**：判断设备是否进入或离开指定区域

通过 `POINT(lat, lon)` 字面量和 `GEOPOINT` 类型，SonnetDB 让空间时序数据的写入和查询变得像操作普通数值一样自然。
