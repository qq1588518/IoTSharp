## GEOPOINT 类型入门：地理空间数据存储与查询

SonnetDB 从早期版本即原生支持地理空间数据类型。`GEOPOINT` 是一种专为经纬度坐标设计的紧凑类型，让开发者可以在时序数据库中直接存储和查询地理位置信息。

### GEOPOINT 类型定义

`GEOPOINT` 采用 WGS84 坐标系，使用双精度浮点数存储纬度和经度，共 16 字节。其 C# 定义如下：

```csharp
public readonly record struct GeoPoint(double Lat, double Lon)
{
    public static GeoPoint Create(double lat, double lon)
    {
        if (double.IsNaN(lat) || lat < -90d || lat > 90d)
            throw new ArgumentOutOfRangeException(nameof(lat), "纬度必须位于 [-90, 90]。");
        if (double.IsNaN(lon) || lon < -180d || lon > 180d)
            throw new ArgumentOutOfRangeException(nameof(lon), "经度必须位于 [-180, 180]。");
        return new GeoPoint(lat, lon);
    }
    public override string ToString() => $"POINT({Lat:G},{Lon:G})";
}
```

### POINT 字面量语法

SonnetDB 支持两种插入坐标的方式。推荐使用 `POINT(lat, lon)` 字面量语法：

```sql
-- 创建包含 GEOPOINT 的 measurement
CREATE MEASUREMENT gps_tracks (
    device_id TAG,
    position FIELD GEOPOINT,
    altitude FIELD DOUBLE,
    speed FIELD DOUBLE
);

-- 使用 POINT 字面量插入（推荐）
INSERT INTO gps_tracks (device_id, position, altitude, speed, time)
VALUES ('device-001', POINT(39.9042, 116.4074), 50.5, 12.3, 1776477601000);

-- 使用数组语法插入
INSERT INTO gps_tracks (device_id, position, time)
VALUES ('device-002', [31.2304, 121.4737], 1776477701000);
```

**注意**：`POINT(lat, lon)` 参数顺序是纬度在前、经度在后，即 POINT(纬度, 经度)。这与许多 GIS 系统中的 (经度, 纬度) 习惯不同，请务必注意。

### 查询地理空间数据

可以直接在 SELECT 中返回 GEOPOINT 列：

```sql
-- 查询设备轨迹
SELECT time, device_id, position, altitude, speed
FROM gps_tracks
WHERE device_id = 'device-001'
  AND time >= '2025-06-01 00:00:00'
ORDER BY time
LIMIT 100;
```

### GeoHash 空间索引

为了加速地理空间查询，SonnetDB 支持为 GEOPOINT 列创建 GeoHash 索引。GeoHash 将二维坐标编码为一维字符串，支持高效的前缀匹配过滤：

```sql
-- 创建 GeoHash 空间索引
CREATE INDEX idx_gps_location ON gps_tracks (position)
WITH (index_type = 'geohash', precision = 7);
```

`precision` 参数控制 GeoHash 精度级别。精度 7 对应的网格约 150m x 150m，适合城市级定位；精度 5 约 5km x 5km，适合区域级概览。

### 实际应用场景

**车辆轨迹追踪**：物流公司可以使用 GEOPOINT 记录每辆运输车的实时位置，结合 `geo_speed()` 和 `geo_distance()` 函数分析行驶路径和速度。

**共享单车管理**：记录每辆单车的停放位置，使用 `geo_within()` 函数查找电子围栏内的车辆，辅助运营调度决策。

**环境监测**：将气象站的地理位置与传感器读数关联，使用 `geo_bbox()` 筛选特定区域内的监测站数据，用于区域环境质量分析。

GEOPOINT 类型的引入让 SonnetDB 成为真正的多模态时序数据库，能够统一处理时间、空间和数值的联合分析。
