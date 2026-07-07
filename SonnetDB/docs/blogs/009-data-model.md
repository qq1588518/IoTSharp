## 深入理解 SonnetDB 数据模型：Measurement、Tag、Field 与 Time

正确理解数据模型是高效使用任何数据库的基础。SonnetDB 的数据模型专为时序数据优化，引入了 Measurement、Tag、Field、Time 和 Series 等核心概念。本文将逐一介绍这些概念，并通过实际例子帮助您更好地理解它们的含义和使用方式。

### Measurement：时序数据的容器

在关系数据库中，我们用"表（Table）"来组织数据。在 SonnetDB 中，对应的概念是 **Measurement（测量）**。一个 Measurement 代表一类时序数据的集合，例如 `cpu` 测量用于存储 CPU 使用率数据，`temperature` 测量用于存储温度传感器数据。

```sql
CREATE MEASUREMENT cpu (
    host TAG,
    usage FIELD FLOAT,
    cores FIELD INT
);
```

每个 Measurement 包含四类列：Time、Tag、Field 和 Vector。这四类列各有不同的语义和作用。

### Tag：元数据标签

**Tag（标签）** 用于描述数据点的元信息。Tag 是字符串类型，通常存储设备的标识符、地理位置、所属分组等不会频繁变化的元数据。在 SonnetDB 中，Tag 列会被自动索引，因此基于 Tag 的过滤查询（如 `WHERE host = 'server-01'`）非常高效。

```sql
-- host 和 region 是 Tag 列
CREATE MEASUREMENT sensor (
    device_id TAG,
    region TAG,
    temperature FIELD FLOAT,
    humidity FIELD FLOAT
);
```

Tag 的一个重要特性是：具有相同 Tag 组合的数据点属于同一个 **Series（时间序列）**。例如，`device_id = 'sensor-a'` 且 `region = 'beijing'` 的所有数据点构成一个独立的时间序列。

### Field：实际测量值

**Field（字段）** 存储实际的测量数值。与 Tag 不同，Field 是数值类型（FLOAT、INT、DOUBLE 等），代表传感器读取的物理量，如温度、湿度、电压、速度等。Field 是 SonnetDB 进行聚合计算（如 SUM、AVG、MAX）和数学运算的主要对象。

```sql
CREATE MEASUREMENT power (
    station TAG,
    voltage FIELD FLOAT,
    current FIELD FLOAT,
    power_factor FIELD FLOAT
);
```

每个 Measurement 可以包含多个 Field，它们共享同一个时间戳和 Tag 组合。这种设计符合实际场景中多维度数据采集的习惯——例如一个电力监测设备通常同时采集电压、电流和功率因数。

### Time：时间戳

**Time（时间）** 是时序数据的核心维度。每条数据记录都必须包含一个时间戳，表示数据点被采集或记录的时间。SonnetDB 支持毫秒级精度的时间戳（Unix 毫秒时间戳）。

```sql
INSERT INTO cpu (time, host, usage) VALUES (1713676800000, 'server-01', 0.71);
```

时间戳列在 SonnetDB 中具有特殊的语义：
- 时间戳是 **主键的一部分**，每条数据通过（时间戳 + Tag 组合）唯一标识。
- 数据按照时间戳 **自动排序存储**，这是时序数据的基础组织方式。
- 查询时利用 **时间分区** 特性，按时间范围过滤数据极为高效。

### Series：时间序列

**Series（时间序列）** 是由相同 Measurement 和相同 Tag 组合的所有数据点构成的有序集合。例如：

```sql
-- 以下两条记录属于同一个 Series
INSERT INTO cpu (time, host, usage) VALUES (1713676800000, 'server-01', 0.71);
INSERT INTO cpu (time, host, usage) VALUES (1713676801000, 'server-01', 0.85);

-- 以下记录属于另一个 Series（不同 Tag 值）
INSERT INTO cpu (time, host, usage) VALUES (1713676800000, 'server-02', 0.45);
```

每个 Series 在内部由唯一标识符（Series ID）标记，SonnetDB 利用 Series ID 进行高效的存储和查询。

### 数据类型总览

SonnetDB 支持以下数据类型：

| 类别 | 类型 | 说明 |
|------|------|------|
| TAG | STRING | 字符串标签，自动索引 |
| FIELD | FLOAT, INT, DOUBLE, BOOLEAN | 数值型或布尔型测量值 |
| VECTOR | VECTOR(dim) | 高维向量数据，支持 HNSW 索引 |
| GEOPOINT | GEOPOINT | 地理空间坐标点 |

理解这些核心概念后，您就可以更加自信地设计和优化您的 SonnetDB 数据模型了。在实际项目中，合理的 Tag 和 Field 划分、恰当的数据类型选择，将直接影响查询性能和数据存储效率。
