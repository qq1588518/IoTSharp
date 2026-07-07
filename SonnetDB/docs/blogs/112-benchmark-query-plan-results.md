## 查询基准通稿：100 万点数据集上的时间范围检索对比

范围查询是时序数据库最常见的读取路径。SonnetDB 的查询基准关注“按 tag + 时间窗口”取回最近 10% 数据时，各数据库从索引定位、数据扫描、结果物化到客户端消费的整体耗时。

### 通稿

本轮查询基准使用写入完成后的 100 万点数据集，查询最后 10% 时间窗口，约 100,000 行结果。该测试模拟设备详情页、告警排查、最近趋势加载等高频场景：查询必须足够快，且结果量足够大，能暴露扫描和反序列化成本。

SonnetDB 嵌入式查询走本地 `QueryEngine` 与 SQL executor；服务端查询走 HTTP SQL 接口并消费 NDJSON 响应。两者分开报告，分别代表边缘进程内使用和远程 API 使用。

### 对比方案

| 组别 | 对照对象 | 当前状态 |
| --- | --- | --- |
| 嵌入式 | SonnetDB Core | 已实现，`QueryBenchmark.SonnetDB_Query_Range` |
| 嵌入式 | SQLite | 已实现，索引表范围查询 |
| 嵌入式 | LiteDB | 已实现，`QueryBenchmark.LiteDB_Query_Range`，使用 `Ts` 索引范围查询 |
| 服务端 | SonnetDB Server | 已实现，`ServerQueryBenchmark` |
| 服务端 | InfluxDB 2.7 | 已实现，Flux range/filter |
| 服务端 | TDengine 3.3.4.3 | 已实现，REST SQL |
| 服务端 | Apache IoTDB | 已实现，non-aligned timeseries + REST v2 SQL 查询 |
| 服务端 | PostgreSQL/TimescaleDB | 已实现，hypertable + `(host, time DESC)` 索引 |

标准查询窗口：

```sql
SELECT time, value
FROM sensor_data
WHERE host = 'server001'
  AND time >= <last_10_percent_from>
  AND time <  <end>;
```

运行命令：

```powershell
$env:SONNETDB_BENCH_URL="http://localhost:5081"
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *Query*
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *ServerQuery*
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *IoTDB_Query_Range*
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *TimescaleDB_Query_Range*
```

### 对比结果

| 方法 | 平均耗时 | 分配 | 返回行数 | 备注 |
| --- | ---: | ---: | ---: | --- |
| SonnetDB Core 范围查询 | 12.04 ms | 22.11 MB | ~100k | 嵌入式基线 |
| SQLite 范围查询 | 54.60 ms | 9.82 MB | ~100k | B-tree 时间索引对照 |
| LiteDB 范围查询 | 239.0 ms | 208.73 MB | ~100k | 单文件文档库，`Ts` 索引范围查询 |
| InfluxDB 范围查询 | 726.14 ms | 280.54 MB | ~100k | Flux 查询 |
| TDengine 范围查询 | 103.36 ms | 22.02 MB | ~100k | REST SQL |
| Apache IoTDB 范围查询 | 113.7 ms | 8.23 MB | ~100k | REST v2 SQL，返回 JSON |
| PostgreSQL/TimescaleDB 范围查询 | 36.81 ms | 3.86 MB | ~100k | hypertable + host/time 索引 |
| SonnetDB Server 范围查询 | 139.5 ms | 18.97 MB | ~100k | HTTP SQL + NDJSON |

### 结论口径

查询类报告重点解释“嵌入式本地扫描”和“服务端 HTTP 返回”之间的差异。若服务端耗时明显高于嵌入式，应把差距拆成网络栈、认证、JSON/NDJSON 序列化和客户端消费成本，而不是简单归因于存储引擎。
