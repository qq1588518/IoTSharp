## SELECT 查询基础：投影、标签过滤与时间范围

查询数据是时序数据库最核心的操作。SonnetDB 的 SELECT 语句兼容标准 SQL，同时针对时序场景提供了专门的优化。本文将介绍最基本的 SELECT 查询操作。

### 基础投影查询

最简单的查询是选择所有列或指定列。`*` 表示返回所有字段，这在探索数据时非常方便：

```sql
-- 查询所有列（用于快速浏览数据）
SELECT *
FROM sensor_data
LIMIT 10;
```

```sql
-- 投影查询：只选择需要的列，减少数据传输量
SELECT ts, temperature, humidity
FROM sensor_data
LIMIT 10;
```

在生产环境中，建议始终使用投影查询指定列名。这不仅能减少网络开销，还能让查询意图更加清晰。

### 标签过滤（TAG 过滤）

时序数据中的标签列（TAG）用于描述数据来源的元信息，例如设备 ID、地区、类型等。在 WHERE 子句中过滤标签可以精准定位目标数据：

```sql
-- 等值过滤：查询指定传感器的数据
SELECT ts, temperature
FROM sensor_data
WHERE sensor_id = 'sensor-001'
ORDER BY ts
LIMIT 100;
```

```sql
-- 多标签组合过滤
SELECT ts, temperature, humidity
FROM sensor_data
WHERE sensor_id = 'sensor-001'
  AND region = 'beijing'
ORDER BY ts
LIMIT 100;
```

```sql
-- IN 条件：查询多个指定设备
SELECT ts, temperature
FROM sensor_data
WHERE sensor_id IN ('sensor-001', 'sensor-002', 'sensor-003')
ORDER BY ts;
```

标签过滤在 SonnetDB 中经过特别优化，过滤效率远高于字段列（FIELD）过滤，因此高频使用的过滤条件应设计为 TAG 列。

### 时间范围过滤

时间范围过滤是时序查询中最重要的 WHERE 条件。SonnetDB 利用时间戳索引来加速范围查询：

```sql
-- 查询某一天的数据
SELECT ts, sensor_id, temperature
FROM sensor_data
WHERE ts >= '2025-06-01T00:00:00Z'
  AND ts < '2025-06-02T00:00:00Z'
ORDER BY ts;
```

```sql
-- 查询最近一小时的实时数据
SELECT ts, sensor_id, temperature
FROM sensor_data
WHERE ts >= NOW() - INTERVAL '1 hour'
ORDER BY ts;
```

```sql
-- 精确时间点查询
SELECT ts, sensor_id, temperature
FROM sensor_data
WHERE ts = '2025-06-01T12:00:00Z';
```

时间列是时序表的主键组成部分，因此时间范围过滤总是能利用主键索引。建议在查询中始终包含时间范围条件，避免全表扫描。

### 组合查询：标签 + 时间 + 值过滤

在实际应用中，查询条件通常是多维度的。SonnetDB 允许在 WHERE 子句中自由组合各种条件：

```sql
-- 综合过滤条件
SELECT ts, sensor_id, temperature, humidity
FROM sensor_data
WHERE sensor_id = 'sensor-001'
  AND ts >= '2025-06-01T00:00:00Z'
  AND ts < '2025-06-02T00:00:00Z'
  AND temperature > 30.0
ORDER BY ts;
```

这个查询同时使用了标签过滤（`sensor_id`）、时间范围（`ts`）和值过滤（`temperature > 30.0`）。SonnetDB 的查询优化器会选择最佳的索引组合来执行。

### ORDER BY 和 LIMIT

时序查询通常需要按时间排序，并使用 LIMIT 控制返回行数：

```sql
-- 查询最近的 50 条记录
SELECT ts, sensor_id, temperature
FROM sensor_data
WHERE sensor_id = 'sensor-001'
ORDER BY ts DESC
LIMIT 50;
```

- `ORDER BY ts DESC` 按时间降序排列，是最常见的时序查询模式
- `ORDER BY ts ASC` 按时间升序，适合查看历史数据
- `LIMIT n` 限制返回行数，避免结果集过大

掌握这些基本的 SELECT 操作，你就可以在 SonnetDB 中高效地查询时序数据了。后续文章将介绍聚合、窗口函数和更高级的查询技巧。
