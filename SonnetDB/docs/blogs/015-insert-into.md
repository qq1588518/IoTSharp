## INSERT INTO：向时序表写入数据

数据写入是时序数据库中最基础也最频繁的操作。SonnetDB 支持标准 SQL `INSERT INTO` 语法，并提供针对时序场景的语义增强。本文将介绍 INSERT 的各种用法和注意事项。

### 基本语法

SonnetDB 的 INSERT 语句遵循 SQL 标准，要求显式指定时间戳列和所有必填字段：

```sql
INSERT INTO sensor_data (ts, sensor_id, temperature, humidity)
VALUES ('2025-06-01T12:00:00Z', 'sensor-001', 23.5, 65.2);
```

时间戳列在表创建时通过 `PRIMARY KEY` 和 `TIMESTAMP` 类型定义，插入时必须提供有效的 ISO 8601 格式时间值。

### 多行批量插入

时序数据通常以批量方式到达，SonnetDB 支持单条语句插入多行，大幅提升写入吞吐量：

```sql
INSERT INTO sensor_data (ts, sensor_id, temperature, humidity)
VALUES
    ('2025-06-01T12:00:00Z', 'sensor-001', 23.5, 65.2),
    ('2025-06-01T12:00:01Z', 'sensor-001', 23.6, 65.0),
    ('2025-06-01T12:00:02Z', 'sensor-001', 23.4, 65.3),
    ('2025-06-01T12:00:03Z', 'sensor-001', 23.7, 64.8);
```

批量插入时，所有行的时间戳和标签（TAG）应尽量保持一致以避免跨分片写入。每条记录占用一行，括号包裹，逗号分隔。

### 时间语义与行为

SonnetDB 处理时间戳时有几个重要行为需要了解：

**去重策略**：如果新插入的数据与已有数据具有相同的时间戳和标签组合，SonnetDB 默认使用"最后写入胜利"（last-write-wins）策略，后写入的数据会覆盖之前的数据。

```sql
-- 先插入一条记录
INSERT INTO sensor_data (ts, sensor_id, temperature)
VALUES ('2025-06-01T12:00:00Z', 'sensor-001', 23.5);

-- 再次插入同时间戳+标签，temperature 自动更新为 25.0
INSERT INTO sensor_data (ts, sensor_id, temperature)
VALUES ('2025-06-01T12:00:00Z', 'sensor-001', 25.0);
```

**乱序写入**：SonnetDB 原生支持乱序数据（out-of-order data）。如果新数据的时间戳早于已有数据，不会导致错误或性能大幅下降。这在实际场景中非常有用——比如 IoT 设备离线后重新连网，补发历史数据。

### 字段类型注意事项

**TAG 类型**：标签列通常用于过滤和分组，建议使用低基数的标识符。插入时 TAG 列的值会被自动推断为字符串类型。

**FIELD 类型**：字段列必须与建表时的类型声明一致。常见的 FIELD 类型包括 `DOUBLE`、`BIGINT`、`BOOLEAN` 和 `VARCHAR`。插入类型不匹配时，SonnetDB 会尝试隐式转换：

```sql
-- temperature 是 DOUBLE 类型
-- 字符串 "23.5" 会被自动转换为 DOUBLE 23.5
INSERT INTO sensor_data (ts, sensor_id, temperature)
VALUES ('2025-06-01T12:00:00Z', 'sensor-001', '23.5');
```

### 使用建议

1. **合理设置批量大小**：单次批量建议 100-1000 行，既能发挥批量插入的性能优势，又避免单条语句过大。
2. **指定列名**：始终显式列出列名，避免建表时列顺序变化导致写入错误。
3. **避免过多标签**：每个表建议标签数量不超过 10 个，过高的标签基数会影响压缩率和查询性能。

通过掌握 INSERT INTO 语法和时序语义，你可以在 SonnetDB 中高效地管理海量时序数据的写入。
