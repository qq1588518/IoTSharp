## CREATE MEASUREMENT：定义您的时序数据结构

在 SonnetDB 中，创建数据表的语句是 `CREATE MEASUREMENT`。这与传统关系数据库的 `CREATE TABLE` 类似，但专门针对时序数据的特点进行了优化。本文将详细介绍 `CREATE MEASUREMENT` 的语法和使用方法，并通过多个实例帮助您快速上手。

### 基本语法

`CREATE MEASUREMENT` 语句的基本语法如下：

```sql
CREATE MEASUREMENT measurement_name (
    column_name TAG,
    column_name FIELD data_type,
    ...
);
```

其中 `measurement_name` 是您要创建的 Measurement 名称，列定义部分指定了各列的名称和类型。Tag 列使用 `TAG` 关键字标记，Field 列需要指定具体的数据类型。

### 简单示例：监控 CPU 数据

让我们从一个最常用的场景开始——监控多台服务器的 CPU 使用率：

```sql
CREATE MEASUREMENT cpu (
    host TAG,
    usage FIELD FLOAT,
    cores FIELD INT
);
```

这条语句创建了一个名为 `cpu` 的 Measurement，包含：
- `host`（TAG）：标识数据来自哪台服务器，如 `server-01`、`server-02`。
- `usage`（FIELD FLOAT）：CPU 使用率，浮点数类型，取值范围通常为 0.0 到 1.0。
- `cores`（FIELD INT）：CPU 核心数，整数类型。

Tag 列 `host` 会被自动索引，因此按主机名过滤数据的查询会非常高效。

### 多字段的 Measurement

在实际场景中，一个传感器设备通常会同时采集多种数据。您可以在一个 Measurement 中包含多个 Field 列：

```sql
CREATE MEASUREMENT environment (
    device_id TAG,
    location TAG,
    temperature FIELD FLOAT,
    humidity FIELD FLOAT,
    pressure FIELD DOUBLE,
    is_active FIELD BOOLEAN
);
```

使用多字段设计，可以在一次写入中记录设备的所有读数，避免了为每个物理量创建单独的 Measurement。这不仅简化了数据模型，还减少了时间戳和 Tag 的重复存储开销。

### 使用高级数据类型

SonnetDB 还支持 GEOPOINT 和 VECTOR 这两种高级数据类型，适用于地理空间和 AI 向量搜索场景：

```sql
CREATE MEASUREMENT fleet_tracking (
    vehicle_id TAG,
    driver TAG,
    speed FIELD FLOAT,
    fuel_level FIELD FLOAT,
    location GEOPOINT,
    engine_vibration VECTOR(128)
) WITH INDEX hnsw(m=16, ef=200);
```

这个例子创建了一个车队跟踪的 Measurement：
- `location`（GEOPOINT）：存储车辆的经纬度坐标，支持地理空间查询。
- `engine_vibration`（VECTOR(128)）：存储发动机振动数据的 128 维向量嵌入，配合 HNSW 索引可以高效进行相似性搜索，用于故障诊断。

`WITH INDEX hnsw(m=16, ef=200)` 子句为向量列创建了 HNSW 索引，`m` 和 `ef` 参数分别控制索引的连通性和搜索精度。

### 使用 IF NOT EXISTS

与标准 SQL 一致，SonnetDB 支持 `IF NOT EXISTS` 子句来避免重复创建导致的错误：

```sql
CREATE MEASUREMENT IF NOT EXISTS cpu (
    host TAG,
    usage FIELD FLOAT,
    cores FIELD INT
);
```

如果 `cpu` Measurement 已经存在，这条语句不会报错，而是静默返回成功。这在自动化部署脚本中非常有用。

### 设计最佳实践

在设计 Measurement 结构时，以下建议值得参考：

1. **Tag 的选择**：Tag 用于存储**标识性**和**分类性**的元数据，如设备 ID、地理位置、数据类型等。不要将随着时间频繁变化的值设为 Tag（例如温度值本身）。

2. **Field 的选择**：Field 存储实际的测量值。具有相同时间戳和 Tag 的相关测量值建议放在同一个 Measurement 中。

3. **数据类型的选择**：选择合适的数据类型可以节省存储空间并提高查询性能。整数用 `INT`，浮点数用 `FLOAT`（32位）或 `DOUBLE`（64位），布尔值用 `BOOLEAN`。

4. **避免过多的 Tag 组合**：每个独特的 Tag 组合会创建一个新的 Series。过多的 Series（通常超过百万级）会增加索引开销和存储压力。

5. **命名的规范**：使用有意义的命名，建议采用小写字母和下划线组合，例如 `device_temperature`、`power_consumption`。

`CREATE MEASUREMENT` 是 SonnetDB 中最基础也最重要的语句之一。一个精心设计的 Measurement 结构可以为后续的数据写入和查询分析奠定良好的基础。花一些时间在设计阶段，往往能在后续的开发运维中节省大量时间。
