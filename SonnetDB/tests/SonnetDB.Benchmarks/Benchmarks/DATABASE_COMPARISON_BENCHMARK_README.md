# SonnetDB vs IoTDB 对比基准测试

## 简介

这是一个 SonnetDB 与 Apache IoTDB 的性能对比基准测试，用于对两个时序数据库进行写入性能评估。默认测试模拟 1,000 个设备、每设备 30 个测点、1 小时数据的写入场景，并按 `AB BA AB BA` 顺序逐次运行四轮。

当前文档同时记录两种口径：

- `--comparison`：SonnetDB 嵌入式引擎 vs IoTDB REST Server
- `--comparison-server`：SonnetDB HTTP Server vs IoTDB REST Server

如果目标是和 IoTDB 做同口径服务器对比，应使用 `--comparison-server`，不要把嵌入式结果直接与 IoTDB 服务端结果混为一谈。

> 当前实现口径：SonnetDB 与 IoTDB 都写入相同的设备、时间戳和 `c1..c30` 字段。默认正式规模为 1,000 个设备 × 12 个时间点 = 12,000 行；每行 30 个字段，总计 360,000 个字段值。吞吐量按 `values/sec` 统计，避免把“行”和“字段值”混在一起。
>
> IoTDB 侧使用每个设备一个 aligned timeseries，并通过 REST v2 `insertTablet` 写入同样的 `c1..c30` 字段。

## 功能特性

- **AB BA AB BA 四轮测试**：按照特定顺序运行四轮测试（A=SonnetDB, B=IoTDB）
- **不并行执行**：测试逐次执行，避免并发干扰
- **详细性能指标**：
  - 单次运行耗时（毫秒）
  - 写入行数和字段值总数
  - 吞吐量（字段值/秒，`values/sec`）
- **统计分析**：
  - 平均/最小/最大耗时
  - 平均吞吐量
  - 相对性能对比

## 测试数据规模

- **设备数**：1,000 个设备（`--comparison-full` 为 100,000 个设备）
- **测点数/设备**：30 个
- **时间范围**：1 小时（12 个 5 分钟间隔）
- **总行数**：12,000 行
- **总字段值数**：360,000 个字段值

## 环境要求

### 前置条件

1. **SonnetDB Server 模式**：Docker 容器或本地服务
2. **IoTDB**: Docker 容器运行
3. **.NET 10 SDK**: 编译和运行

### 启动外部数据库

```bash
# 启动 SonnetDB 与 IoTDB
docker compose -f tests/SonnetDB.Benchmarks/docker/docker-compose.yml up -d sonnetdb iotdb

# 检查 SonnetDB Server 是否就绪
curl http://localhost:5080/healthz

# 检查 IoTDB 是否就绪
curl -u root:root http://localhost:18080/rest/v2/query -d '{"sql":"SHOW VERSION"}' -H "Content-Type: application/json"
```

## 使用方法

### 方式一：命令行运行（推荐）

当前项目入口已支持以下参数，不需要再手动修改 `Program.cs`：

```bash
cd SonnetDB

# 小规模链路验证
dotnet run -c Release --project tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison-smoke

# 小规模 Server vs Server 链路验证
dotnet run -c Release --project tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison-server-smoke

# 默认四轮公平测试：AB BA AB BA
dotnet run -c Release --project tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison

# 默认四轮公平测试：SonnetDB Server vs IoTDB Server
dotnet run -c Release --project tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison-server

# 100,000 设备高基数模式，可能非常耗时
dotnet run -c Release --project tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison-full
```

### 方式二：作为库函数调用

创建一个独立的控制台应用：

```csharp
// 在您的程序中调用
await DatabaseComparisonBenchmark.RunComparison();
```

或参考示例代码：

```csharp
using SonnetDB.Benchmarks.Benchmarks;

// 运行对比测试
await DatabaseComparisonBenchmark.RunComparison();
```

### 编译

```bash
cd SonnetDB
dotnet build tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -c Release
```

## 实测结果

### 1. 同口径 Server vs Server 结果

以下结果来自 2026-05-06 在本机实际运行的 `--comparison-server` 四轮测试，不是示例值。完整日志文件：`tests/SonnetDB.Benchmarks/artifacts/database-comparison-server-20260506-183522.log`。

测试命令：

```bash
dotnet run -c Release --project tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison-server
```

测试规模：

| 项目 | 值 |
|------|------|
| 设备数 | 1,000 |
| 字段/设备 | 30 |
| 时间点 | 12 |
| 每阶段行数 | 12,000 |
| 每阶段字段值总数 | 360,000 |
| 执行顺序 | AB BA AB BA |
| A | SonnetDB Server |
| B | IoTDB |

四轮结果：

| 轮次 | 阶段 | 数据库 | 耗时(ms) | 行数 | 字段值 | 吞吐量(values/sec) |
|------|------|--------|---------:|-----:|-------:|-------------------:|
| 1 | A | SonnetDB Server | 7,402 | 12,000 | 360,000 | 48,636 |
| 1 | B | IoTDB | 22,019 | 12,000 | 360,000 | 16,350 |
| 2 | B | IoTDB | 31,142 | 12,000 | 360,000 | 11,560 |
| 2 | A | SonnetDB Server | 23,297 | 12,000 | 360,000 | 15,453 |
| 3 | A | SonnetDB Server | 28,342 | 12,000 | 360,000 | 12,702 |
| 3 | B | IoTDB | 41,267 | 12,000 | 360,000 | 8,724 |
| 4 | B | IoTDB | 37,774 | 12,000 | 360,000 | 9,530 |
| 4 | A | SonnetDB Server | 24,529 | 12,000 | 360,000 | 14,677 |

统计结果：

| 数据库 | 平均耗时(ms) | 最小耗时(ms) | 最大耗时(ms) | 平均吞吐量(values/sec) |
|--------|-------------:|-------------:|-------------:|-----------------------:|
| SonnetDB Server | 20,892 | 7,402 | 28,342 | 22,867 |
| IoTDB | 33,050 | 22,019 | 41,267 | 11,541 |

相对性能：SonnetDB Server 平均吞吐量为 IoTDB 的 1.98 倍。

说明：该实测结果只代表上述环境、上述数据规模和当前测试代码路径。`--comparison-full` 保留 100,000 设备高基数模式，但本次统计没有使用该模式，避免把未完成长跑或高基数建 series 开销混入这份四轮结果。

### 2. 历史口径：嵌入式 vs IoTDB Server

以下历史结果保留用于说明 SonnetDB 嵌入式引擎与 IoTDB 服务端路径的差异，不应作为“同口径 server 对比”引用。完整日志文件：`tests/SonnetDB.Benchmarks/artifacts/database-comparison-20260506-174447.log`。

| 数据库 | 平均耗时(ms) | 平均吞吐量(values/sec) |
|--------|-------------:|-----------------------:|
| SonnetDB Embedded | 2,317 | 160,924 |
| IoTDB Server | 32,633 | 11,324 |

相对性能：SonnetDB Embedded 平均吞吐量为 IoTDB Server 的 14.21 倍。

## 代码位置

- 测试类：[DatabaseComparisonBenchmark.cs](./DatabaseComparisonBenchmark.cs)
- 所需库：
  - SonnetDB.Benchmarks.Helpers.IoTDBRestClient

## 性能测试指标说明

| 指标 | 说明 |
|------|------|
| 耗时(ms) | 测试运行的总耗时，单位毫秒 |
| 行数 | 写入的时序行数；一行包含一个设备在一个时间戳上的 30 个字段 |
| 字段值 | 写入的字段值总数 = 行数 × 字段数 |
| 吞吐量(values/sec) | 每秒写入的字段值数 = 字段值 × 1000 / 耗时(ms) |
| 平均耗时 | 四轮测试中相同数据库的平均耗时 |
| 相对性能对比 | SonnetDB 平均吞吐量 / IoTDB 平均吞吐量 |

## 常见问题

### Q: IoTDB 连接失败

**A**: 确保 IoTDB 已启动：
```bash
docker ps | grep iotdb
# 如果未启动，运行：
docker compose -f tests/SonnetDB.Benchmarks/docker/docker-compose.yml up -d iotdb
```

### Q: 测试耗时很长

**A**: `--comparison-server` 模式是和 IoTDB 同口径的四轮公平测试，当前实测四轮总耗时约 3 分 36 秒。`--comparison` 仍保留历史嵌入式口径。`--comparison-full` 是 100,000 设备高基数模式，可能非常耗时，不应把未完成的 full 模式数据写入统计结果。

### Q: 可以减少测试数据量吗？

**A**: 可以。当前测试通过 `DatabaseComparisonOptions` 控制规模，主要参数包括 `DeviceCount`、`FieldCount`、`TimeSlotCount`、`DeviceBatchSize` 和 `IotDbTabletBatchSize`。默认 `--comparison` 使用 1,000 个设备；`--comparison-full` 使用 100,000 个设备。

### Q: 如何导出测试结果？

**A**: 可以修改 `PrintStatistics()` 方法，将结果导出为 CSV 或 JSON 格式。

## 注意事项

1. **数据清理**：每轮测试前会清空旧数据，确保测试的独立性
2. **两种写入口径**：引用结果时务必注明是 `--comparison`（嵌入式）还是 `--comparison-server`（服务端）
3. **网络延迟**：IoTDB 通过 HTTP REST API 通信，网络延迟可能影响结果
4. **资源占用**：测试过程会占用大量 CPU 和 内存，建议在专用测试机上运行

## 扩展建议

- 添加 TDengine、InfluxDB 等其他数据库的对比
- 支持自定义测试参数（设备数、测点数等）
- 添加读取性能测试
- 集成测试结果历史跟踪和趋势分析

## 许可证

本测试代码遵循 SonnetDB 开源许可证。
