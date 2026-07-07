## SonnetDB INSERT 批处理性能优化：批量写入与 flush 策略

在高吞吐的时序数据写入场景中，单条 INSERT 的性能往往无法满足需求。SonnetDB 提供了丰富的批处理写入能力，帮助用户充分利用硬件性能。本文将深入介绍多行批量写入、批量优化技巧和刷新策略配置。

### 基本的多行批量 INSERT

SonnetDB 支持标准 SQL 的多行 VALUES 语法，可以在一条语句中写入多条数据点：

```sql
INSERT INTO cpu (time, host, usage, cores) VALUES
(1713600000000, 'server-01', 0.71, 8),
(1713600001000, 'server-01', 0.65, 8),
(1713600002000, 'server-01', 0.82, 8),
(1713600003000, 'server-01', 0.73, 8),
(1713600004000, 'server-01', 0.68, 8);
```

单条 INSERT 语句可包含数百甚至数千行 VALUES，能显著减少网络往返和解析开销。

### 批量大小优化建议

批量大小需要根据具体场景调优：

```sql
-- 小批量：适合实时写入，延迟敏感场景
INSERT INTO sensors (time, device_id, temperature) VALUES
(1713600000000, 'sensor-01', 23.5),
(1713600001000, 'sensor-02', 24.1);

-- 大批量：适合历史数据导入，吞吐优先
INSERT INTO historial_metrics (time, metric_name, value) VALUES
(1713600000000, 'cpu', 0.75), ... (最多数千行);
```

经验法则：
- 实时数据流：每批 100~500 行，延迟和吞吐的最佳平衡点
- 批量导入：每批 1000~10000 行，充分利用 SonnetDB 的批量写入路径
- 单批不建议超过 50000 行，以免占用过多内存

### MemTable 与 flush 机制

SonnetDB 使用 MemTable（内存表）暂存写入数据，在达到阈值后自动 flush 到 Segment 文件。理解这一机制对优化写入性能至关重要：

```sql
-- 大量写入后手动触发 flush（部分版本支持控制指令）
FLUSH;
```

自动 flush 的触发条件包括：
- MemTable 大小达到阈值（默认约 64MB）
- WAL 文件达到滚动阈值
- 数据库正常关闭时

### 写入一致性级别

SonnetDB 提供灵活的写入确认策略以平衡性能与数据安全：

- 同步 WAL 写入：每条写入等待 WAL 持久化，安全性最高
- 异步 WAL 写入：批量确认，写入性能更优
- 无 WAL 模式：纯内存写入，适合可容忍数据丢失的测试场景

```sql
-- 批量写入时开启事务可显著提升吞吐
BEGIN BATCH;
INSERT INTO metrics (time, tag, val) VALUES (1713600000000, 'a', 1);
INSERT INTO metrics (time, tag, val) VALUES (1713600001000, 'a', 2);
INSERT INTO metrics (time, tag, val) VALUES (1713600002000, 'a', 3);
COMMIT BATCH;
```

### 性能基准参考

在主流硬件上，SonnetDB 的批量写入吞吐可达：

| 批量大小 | 写入延迟（P50） | 吞吐量（行/秒） |
|---------|---------------|---------------|
| 单条写入 | ~500μs | ~2,000 |
| 100 行/批 | ~2ms | ~50,000 |
| 1000 行/批 | ~8ms | ~125,000 |
| 10000 行/批 | ~50ms | ~200,000 |

通过合理配置批量大小和 flush 策略，SonnetDB 可以在嵌入式模式下依然达到数十万行/秒的写入性能，充分满足 IoT 和运维监控的高吞吐需求。
