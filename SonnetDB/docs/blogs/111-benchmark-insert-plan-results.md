# 写入基准通稿：SonnetDB 在嵌入式与服务端两条路径上的吞吐对比

SonnetDB 的写入基准分为两组：进程内嵌入式基准与服务端 HTTP 基准。嵌入式路径关注引擎本体的 WAL、MemTable、Flush 与 Segment 写入效率；服务端路径关注协议解析、认证、HTTP/Kestrel 与批量端点带来的额外开销。

## 通稿

本轮写入基准使用统一的 100 万点 IoT 数据集，在同一台 Windows 11 开发机上对比 SonnetDB Core、SQLite、InfluxDB、TDengine 与 SonnetDB Server。SonnetDB 同时提供进程内嵌入式和服务端部署形态，因此报告会把“引擎本体能力”和“远程服务能力”分开呈现，避免把 HTTP 协议成本误算到核心存储引擎里。

对边缘网关、采集代理、工控盒子等嵌入式场景，核心指标是单进程内批量写入耗时与内存分配；对平台化服务端场景，核心指标是 Line Protocol、JSON、Bulk VALUES 与 SQL Batch 端点的吞吐差异。

## 对比方案

| 组别 | 对照对象 | 当前状态 |
| --- | --- | --- |
| 嵌入式 | SonnetDB Core | 已实现，`InsertBenchmark.SonnetDB_Insert_1M` |
| 嵌入式 | SQLite | 已实现，`InsertBenchmark.SQLite_Insert_1M` |
| 嵌入式 | LiteDB | 已实现，`InsertBenchmark.LiteDB_Insert_1M` |
| 服务端 | SonnetDB Server | 已实现，`ServerInsertBenchmark` |
| 服务端 | InfluxDB 2.7 | 已实现，Line Protocol 写入 |
| 服务端 | TDengine 3.3.4.3 | 已实现，REST INSERT 与 schemaless LP |
| 服务端 | Apache IoTDB | 已实现，REST v2 `insertTablet`，10,000 行/批 |
| 服务端 | PostgreSQL/TimescaleDB | 已实现，hypertable + binary COPY |

统一数据集：

- 数据量：1,000,000 点
- 时间间隔：1,000 ms 1 点
- measurement：`sensor_data`
- tag：`host=server001`
- field：`value FLOAT`
- 随机种子：固定，确保每个数据库输入一致

运行命令：

```powershell
$env:SONNETDB_BENCH_PORT="5081"
$env:SONNETDB_BENCH_URL="http://localhost:5081"
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *Insert*
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *ServerInsert*
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *IoTDB_Insert_1M*
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *TimescaleDB_Insert_1M*
```

## 对比结果

本轮实测环境：

| 项 | 值 |
| --- | --- |
| 日期 | 2026-04-26 |
| CPU | Intel Core Ultra 9 185H，16C/22T |
| OS | Windows 11 10.0.26200 |
| .NET | SDK 10.0.202，Runtime 10.0.7 |
| Docker | Docker 29.3.1 / Compose v5.1.1 |

| 方法 | 平均耗时 | 分配 | 吞吐 | 备注 |
| --- | ---: | ---: | ---: | --- |
| SonnetDB Core 写入 1M | 704.8 ms | 693.29 MB | 141.9 万点/秒 | 嵌入式基线 |
| SQLite 写入 1M | 1,183.2 ms | 465.40 MB | 84.5 万点/秒 | 嵌入式关系库对照 |
| LiteDB 写入 1M | 10,960 ms | 15,308.80 MB | 9.1 万点/秒 | 嵌入式文档库，`InsertBulk` |
| InfluxDB 写入 1M | 7,392.0 ms | 1,458.95 MB | 13.5 万点/秒 | 服务端 TSDB 对照 |
| TDengine REST INSERT 写入 1M | 16,421.5 ms | 169.33 MB | 6.1 万点/秒 | SQL REST 路径 |
| TDengine schemaless LP 写入 1M | 2,097.0 ms | 61.35 MB | 47.7 万点/秒 | 快速写入路径 |
| Apache IoTDB 写入 1M | 7,036 ms | 148.87 MB | 14.2 万点/秒 | REST v2 `insertTablet`，本轮波动较大 |
| PostgreSQL/TimescaleDB 写入 1M | 5,285 ms | 0.09 MB | 18.9 万点/秒 | binary COPY 到 hypertable；分配为客户端托管分配 |
| SonnetDB Server SQL Batch 写入 1M | 13,469 ms | 676.03 MB | 7.4 万点/秒 | HTTP + SQL parser |
| SonnetDB Server LP 写入 1M | 1,651 ms | 52.41 MB | 60.6 万点/秒 | HTTP Line Protocol 快路径 |
| SonnetDB Server JSON 写入 1M | 2,309 ms | 71.46 MB | 43.3 万点/秒 | HTTP JSON 批量路径 |
| SonnetDB Server Bulk VALUES 写入 1M | 1,691 ms | 34.27 MB | 59.1 万点/秒 | HTTP Bulk VALUES 快路径 |

## 补充结果：SonnetDB Server vs Apache IoTDB Server 同口径写入对比

2026-05-06 我们补跑了一组更严格的同口径对比，用来回答一个更具体的问题：如果 SonnetDB 和 IoTDB 都以“服务端”方式接入，且都通过 HTTP 路径写入相同的数据，那么结果如何。

这组测试与上面的 1M 点 BenchmarkDotNet 写入基准不是同一套方法学。这里使用 `DatabaseComparisonBenchmark`，固定为 1,000 个设备、每设备 30 个字段、12 个时间点，总计每阶段 12,000 行、360,000 个字段值，并按 `AB BA AB BA` 跑四轮，尽量消除预热顺序偏差。

测试命令：

```bash
docker compose -f tests/SonnetDB.Benchmarks/docker/docker-compose.yml up -d sonnetdb iotdb
dotnet run -c Release --project tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison-server
```

实测环境：

| 项 | 值 |
| --- | --- |
| 日期 | 2026-05-06 |
| CPU | Intel Core Ultra 9 185H，16C/22T |
| OS | Windows 11 10.0.26200 |
| .NET | SDK 10.0.202，Runtime 10.0.7 |
| SonnetDB 路径 | HTTP JSON points，Bearer 鉴权 |
| IoTDB 路径 | REST v2 `insertTablet` |

分轮结果：

| 轮次 | 数据库 | 耗时(ms) | 吞吐量(values/sec) |
| --- | --- | ---: | ---: |
| 1 | SonnetDB Server | 7,402 | 48,636 |
| 1 | IoTDB Server | 22,019 | 16,350 |
| 2 | IoTDB Server | 31,142 | 11,560 |
| 2 | SonnetDB Server | 23,297 | 15,453 |
| 3 | SonnetDB Server | 28,342 | 12,702 |
| 3 | IoTDB Server | 41,267 | 8,724 |
| 4 | IoTDB Server | 37,774 | 9,530 |
| 4 | SonnetDB Server | 24,529 | 14,677 |

统计结果：

| 数据库 | 平均耗时(ms) | 最小耗时(ms) | 最大耗时(ms) | 平均吞吐量(values/sec) |
| --- | ---: | ---: | ---: | ---: |
| SonnetDB Server | 20,892 | 7,402 | 28,342 | 22,867 |
| IoTDB Server | 33,050 | 22,019 | 41,267 | 11,541 |

结论：在这组同口径服务端写入测试中，SonnetDB Server 平均吞吐约为 Apache IoTDB Server 的 **1.98x**。

如果面向外部发布，建议直接引用这组 `--comparison-server` 数据来说明“SonnetDB Server vs IoTDB Server”的对比关系；而嵌入式结果应继续单独表述为“引擎本体能力”，不要与服务端结果混合引用。完整日志位于 `tests/SonnetDB.Benchmarks/artifacts/database-comparison-server-20260506-183522.log`。

## 结论口径

发布时只使用本轮 CSV/Markdown 报告中的实测值。IoTDB 与 TimescaleDB 已纳入可执行基准，但其数值代表同机 Docker + 本地客户端路径；TimescaleDB 的 `Allocated` 仅表示 BenchmarkDotNet 观察到的 .NET 客户端托管分配，不包含服务端进程内存。

对于 SonnetDB 与 IoTDB 的公开比较，当前建议使用两层口径：

- 想证明引擎本体写入效率时，引用嵌入式 SonnetDB 结果，并明确说明对侧不是同一链路。
- 想证明平台化部署下的对比结果时，引用上面的 `--comparison-server` 四轮结果，并明确说明双方都走服务端 HTTP 路径。
