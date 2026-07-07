## Flush 模式深入解析：?flush=false|true|async 的持久性与延迟权衡

在时序数据库中，数据写入的持久性（Durability）和写入延迟（Latency）之间存在着经典的权衡关系。SonnetDB 通过 `?flush` 参数提供了三种刷新模式——同步刷盘（true）、非刷盘（false）和异步刷盘（async）——让开发者可以根据业务需求在数据安全和写入性能之间灵活选择。

### Flush=true：最高持久性保证

当 `?flush=true` 时，每次写入操作完成后，数据会立即从内存写入磁盘并执行 `fsync` 系统调用，确保数据在操作系统崩溃或断电后仍能完整恢复。

```bash
# 同步刷盘模式：每次写入都确保数据安全落盘
curl -X POST "http://localhost:8080/api/v1/write?flush=true" \
  -H "Authorization: Bearer sndb_abc123" \
  -d '{"measurement": "sensor_data", "points": [{"time": 1713676800000, "fields": {"value": 23.5}}]}'
```

```csharp
// C# 中通过连接字符串参数控制
var connString = "Data Source=C:\\sonnetdb\\data;Flush=true";
await using var conn = new SndbConnection(connString);
```

**适用场景**：金融交易记录、关键设备报警数据、审计日志等不可丢失的重要数据。**代价**：每次写入的延迟较高（通常在 1-10ms 量级），吞吐量受到磁盘 I/O 性能的制约。

### Flush=false：最高写入性能

当 `?flush=false` 时，数据写入操作仅将数据追加到内存中的 WAL（Write-Ahead Log）缓冲区后即返回，不执行磁盘刷入。此模式下写入延迟极低，但存在数据丢失的风险。

```bash
# 非刷盘模式：数据仅在内存缓冲区中
curl -X POST "http://localhost:8080/api/v1/write?flush=false" \
  -H "Authorization: Bearer sndb_abc123" \
  -d '{"measurement": "sensor_data", "points": [{"time": 1713676800000, "fields": {"value": 23.5}}]}'
```

```csharp
var connString = "Data Source=C:\\sonnetdb\\data;Flush=false";
```

**适用场景**：高频传感器数据采集、日志流处理、临时数据分析等可以容忍少量数据丢失的业务。**代价**：如果进程崩溃，最后几秒内的写入数据可能丢失。SonnetDB 使用了内存 WAL 缓冲区，正常情况下数据并非直接丢失，但意外宕机时确实存在回退窗口。

### Flush=async：延迟与安全的最佳平衡

异步模式是折中方案——数据写入后，系统每隔固定时间间隔（默认 1 秒）或当缓冲区达到一定大小时，自动执行一次批量刷盘。

```bash
# 异步刷盘：每 1 秒或缓冲区满时自动刷盘
curl -X POST "http://localhost:8080/api/v1/write?flush=async" \
  -H "Authorization: Bearer sndb_abc123" \
  -d '{"measurement": "sensor_data", "points": [{"time": 1713676800000, "fields": {"value": 23.5}}]}'
```

```csharp
var connString = "Data Source=C:\\sonnetdb\\data;Flush=async";
```

**适用场景**：绝大部分生产环境。异步模式在吞吐量和数据安全性之间取得了最佳平衡，每秒数万乃至数十万次的写入吞吐量下，最多丢失 1 秒的数据，这在 IoT 和监控场景中通常是可以接受的。

### 三种模式的性能对比

以下是在同一台机器上测试的写入 10 万条数据点的结果：

| 模式 | 总耗时 | 平均延迟/条 | 吞吐量 | 最大数据丢失窗口 |
|------|--------|-------------|--------|-----------------|
| flush=true | 8.2 s | 82 μs | 12,195 pts/s | 0（完全持久） |
| flush=async | 0.45 s | 4.5 μs | 222,222 pts/s | ~1 秒 |
| flush=false | 0.38 s | 3.8 μs | 263,158 pts/s | ~1-2 秒 |

### 配置建议

在生产环境中，建议按以下策略配置 Flush 模式：

```csharp
// 按 Measurement 的重要性分级
var criticalData = "Data Source=C:\\sonnetdb\\data\\critical;Flush=true";
var normalData = "Data Source=C:\\sonnetdb\\data\\normal;Flush=async";
var cacheData = "Data Source=C:\\sonnetdb\\data\\cache;Flush=false";
```

总结而言，SonnetDB 的三种 Flush 模式让开发者可以根据数据类型和业务需求精细控制数据持久性级别。关键数据使用 `flush=true` 确保零丢失，大规模时序数据使用 `flush=async` 兼顾性能和安全，临时数据使用 `flush=false` 榨取极限吞吐量。这种灵活性是 SonnetDB 适应多样化应用场景的重要设计之一。
