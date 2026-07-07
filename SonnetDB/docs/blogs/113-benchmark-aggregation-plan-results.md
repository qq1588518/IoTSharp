## 聚合基准通稿：60,000 ms 时间桶上的 AVG/MIN/MAX/COUNT 对比

聚合查询决定了时序数据库能否支撑仪表盘、报表和规则引擎。SonnetDB 的聚合基准以 100 万点全量数据为输入，按 60,000 ms 窗口生成约 16,667 个时间桶，并对每个桶计算常见统计指标。

### 通稿

本轮聚合基准对比 SonnetDB Core、SQLite、InfluxDB、TDengine 与 SonnetDB Server 在同一数据集上的时间桶聚合能力。测试重点不是单个 `avg` 函数的算法开销，而是完整 SQL 路径：解析、过滤、分桶、聚合、结果构建和输出。

对生产场景而言，这个测试接近“过去两周按 60,000 ms 采样的均值趋势图”的常见查询。它能体现数据库是否能在大结果集上稳定执行时间分桶，而不是只在小窗口查询里表现好。

### 对比方案

| 组别 | 对照对象 | 当前状态 |
| --- | --- | --- |
| 嵌入式 | SonnetDB Core | 已实现，`AggregateBenchmark.SonnetDB_Aggregate_1Min` |
| 嵌入式 | SQLite | 已实现，整数除法分桶 + SQL 聚合 |
| 嵌入式 | LiteDB | 已实现，`AggregateBenchmark.LiteDB_Aggregate_1Min`，文档顺扫 + 进程内分桶 |
| 服务端 | SonnetDB Server | 已实现，`ServerAggregateBenchmark` |
| 服务端 | InfluxDB 2.7 | 已实现，Flux `aggregateWindow` |
| 服务端 | TDengine 3.3.4.3 | 已实现，60,000 ms 时间窗口 |
| 服务端 | Apache IoTDB | 已实现，`GROUP BY ([start,end), 60000ms)` 时间窗口 |
| 服务端 | PostgreSQL/TimescaleDB | 已实现，`time_bucket('1 minute', time)` |

标准查询：

```sql
SELECT avg(value), min(value), max(value), count(value)
FROM sensor_data
WHERE time >= <start> AND time < <end>
GROUP BY time(60000ms);
```

运行命令：

```powershell
$env:SONNETDB_BENCH_URL="http://localhost:5081"
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *Aggregate*
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *ServerAggregate*
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *IoTDB_Aggregate_1Min*
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *TimescaleDB_Aggregate_1Min*
```

### 对比结果

| 方法 | 平均耗时 | 分配 | 时间桶数 | 备注 |
| --- | ---: | ---: | ---: | --- |
| SonnetDB Core 60,000 ms 聚合 | 12.18 ms | 3.84 MB | ~16,667 | 嵌入式基线 |
| SQLite 60,000 ms 聚合 | 387.64 ms | 2.50 MB | ~16,667 | SQL 分桶对照 |
| LiteDB 60,000 ms 聚合 | 1,550 ms | 2,037.76 MB | ~16,667 | 文档顺扫 + 进程内 AVG/MIN/MAX/COUNT |
| InfluxDB 60,000 ms 聚合 | 122.05 ms | 47.25 MB | ~16,667 | Flux 聚合 |
| TDengine 60,000 ms 聚合 | 93.16 ms | 4.10 MB | ~16,667 | TSDB 时间窗口 |
| Apache IoTDB 60,000 ms 聚合 | 866.1 ms | 4.38 MB | ~16,667 | REST v2 SQL，`GROUP BY` 时间窗口 |
| PostgreSQL/TimescaleDB 60,000 ms 聚合 | 90.43 ms | 0.01 MB | ~16,667 | `time_bucket`；分配为客户端托管分配 |
| SonnetDB Server 60,000 ms 聚合 | 205.6 ms | 2.76 MB | ~16,667 | HTTP SQL |

### 结论口径

聚合报告应避免只强调最短耗时，也要展示内存分配和结果桶数量。若数据库只返回单一 `avg` 而没有同时计算 `min/max/count`，需在表格备注中标清语义差异。
