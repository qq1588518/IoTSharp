# 快速启动指南

## 如何运行 SonnetDB vs IoTDB 对比基准测试

当前实现已接入项目入口：

```bash
# 真实小规模冒烟：20 设备 × 30 字段 × 3 时间点
dotnet run -c Release --project tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison-smoke

# 真实小规模 Server vs Server 冒烟
dotnet run -c Release --project tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison-server-smoke

# 正式 AB BA AB BA：1,000 设备 × 30 字段 × 12 时间点
dotnet run -c Release --project tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison

# 正式 AB BA AB BA（两边都走服务端）
dotnet run -c Release --project tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison-server

# 高基数完整模式：100,000 设备 × 30 字段 × 12 时间点，可能非常耗时
dotnet run -c Release --project tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison-full
```

正式默认规模是 12,000 行、360,000 个字段值，吞吐量按 `values/sec` 统计。SonnetDB 与 IoTDB 写入同样的设备、时间戳和 `c1..c30` 字段。若要做和 IoTDB 同口径的公平对比，优先运行 `--comparison-server`。

### 步骤 1：准备环境

#### 1.1 启动 SonnetDB 与 IoTDB 容器

```bash
# 在项目根目录运行
docker compose -f SonnetDB/tests/SonnetDB.Benchmarks/docker/docker-compose.yml up -d sonnetdb iotdb

# 验证 SonnetDB 是否就绪
curl -s http://localhost:5080/healthz

# 验证 IoTDB 是否就绪（HTTP 连接）
curl -s -u root:root http://localhost:18080/rest/v2/query \
  -H "Content-Type: application/json" \
  -d '{"sql":"SHOW VERSION"}' | jq .

# 或用 PowerShell
Invoke-WebRequest -Uri "http://localhost:18080/rest/v2/query" `
  -Method Post `
  -Headers @{"Authorization" = "Basic $(([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('root:root'))))"} `
  -ContentType "application/json" `
  -Body '{"sql":"SHOW VERSION"}'
```

#### 1.2 编译测试项目

```bash
cd SonnetDB
dotnet build tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -c Release
```

### 步骤 2：创建测试运行程序

方式 A：创建独立的控制台应用（推荐）

```bash
# 1. 创建新的控制台应用
dotnet new console -n BenchmarkRunner
cd BenchmarkRunner

# 2. 添加项目引用
dotnet add reference ../SonnetDB/tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj

# 3. 编辑 Program.cs
```

**Program.cs** 内容：

```csharp
using SonnetDB.Benchmarks.Benchmarks;

Console.WriteLine("正在初始化 SonnetDB vs IoTDB 对比基准测试...");
Console.WriteLine();

try
{
    // 运行对比基准测试
    await DatabaseComparisonBenchmark.RunComparison();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"错误: {ex.Message}");
    Environment.Exit(1);
}
```

```bash
# 4. 运行测试
dotnet run -c Release
```

方式 B：在现有的 BenchmarkDotNet 框架中运行

修改 `SonnetDB/tests/SonnetDB.Benchmarks/Program.cs`：

```csharp
using BenchmarkDotNet.Running;
using SonnetDB.Benchmarks.Benchmarks;

// 检查命令行参数
if (args.Length > 0 && args[0] == "--comparison")
{
    // 运行对比测试
    await DatabaseComparisonBenchmark.RunComparison();
}
else
{
    // 运行 BenchmarkDotNet 测试
    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
```

然后运行：

```bash
cd SonnetDB
dotnet run -c Release --project tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison
```

### 步骤 3：运行测试

```bash
# 确保 IoTDB 正在运行
docker ps | grep iotdb

# 运行测试
dotnet run -c Release

# 或如果使用方式 B
dotnet run -c Release -p tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison
```

## Server vs Server 实测结果

以下结果来自 2026-05-06 实际执行：

```bash
dotnet run -c Release --project tests/SonnetDB.Benchmarks/SonnetDB.Benchmarks.csproj -- --comparison-server
```

| 数据库 | 平均耗时(ms) | 平均吞吐量(values/sec) |
|--------|-------------:|-----------------------:|
| SonnetDB Server | 20,892 | 22,867 |
| IoTDB Server | 33,050 | 11,541 |

相对性能：SonnetDB Server 约快 **1.98x**。

完整分轮日志：`tests/SonnetDB.Benchmarks/artifacts/database-comparison-server-20260506-183522.log`

## 预期输出示例

```
═════════════════════════════════════════════════════════════════
  SonnetDB vs IoTDB 对比基准测试 (Server vs Server | AB BA AB BA，4 轮)
═════════════════════════════════════════════════════════════════

╔═══ 第 1 轮：AB ═══╗

● A 阶段开始...
    SonnetDB Server 进度: 设备 100/1,000 | 已写 1,200 行/36,000 值 | 吞吐 56,773 values/sec
    ...
    SonnetDB Server 写入完成: 12,000 行/360,000 值，耗时 7.26 秒
  耗时: 7402ms | 吞吐量: 48636 values/sec

● B 阶段开始...
    IoTDB 进度: 设备 100/1,000 | 已写 1,200 行/36,000 值 | 吞吐 9,685 values/sec
    ...
    IoTDB 写入完成: 12,000 行/360,000 值，耗时 21.08 秒
  耗时: 22019ms | 吞吐量: 16350 values/sec

...

═════════════════════════════════════════════════════════════════
  性能对比总结
═════════════════════════════════════════════════════════════════

╔════════╦═════════════╦════════════╦═══════════════╦═══════════════════╗
║ 轮数   ║ 数据库      ║ 耗时(ms)   ║ 行数          ║ 吞吐量(values/sec)║
╠════════╬═════════════╬════════════╬═══════════════╬═══════════════════╣
║      1 ║ SonnetDB    ║       7402 ║        12,000 ║              48636 ║
║      1 ║ IoTDB       ║      22019 ║        12,000 ║              16350 ║
║      2 ║ IoTDB       ║      31142 ║        12,000 ║              11560 ║
║      2 ║ SonnetDB    ║      23297 ║        12,000 ║              15453 ║
║      3 ║ SonnetDB    ║      28342 ║        12,000 ║              12702 ║
║      3 ║ IoTDB       ║      41267 ║        12,000 ║               8724 ║
║      4 ║ IoTDB       ║      37774 ║        12,000 ║               9530 ║
║      4 ║ SonnetDB    ║      24529 ║        12,000 ║              14677 ║
╚════════╩═════════════╩════════════╩═══════════════╩═══════════════════╝

● SonnetDB 统计:
  平均耗时: 20892 ms
  最小耗时: 7402 ms
  最大耗时: 28342 ms
  平均吞吐量: 22867 values/sec

● IoTDB 统计:
  平均耗时: 33050 ms
  最小耗时: 22019 ms
  最大耗时: 41267 ms
  平均吞吐量: 11541 values/sec

● 相对性能对比:
  SonnetDB 比 IoTDB 快 1.98x
```

## 性能调优建议

为了获得最准确的结果：

```bash
# 1. 关闭不必要的后台服务
sudo systemctl stop \
  avahi-daemon \
  cups \
  bluetooth

# 2. 设置 CPU 频率缩放为性能模式（Linux）
echo performance | sudo tee /sys/devices/system/cpu/cpu*/cpufreq/scaling_governor

# 3. 检查系统负载
top -b -n 1 | head -15

# 4. 运行测试
dotnet run -c Release

# 5. 恢复 CPU 频率缩放
echo ondemand | sudo tee /sys/devices/system/cpu/cpu*/cpufreq/scaling_governor
```

## 清理和清除数据

```bash
# 停止 IoTDB 并删除数据
docker compose -f SonnetDB/tests/SonnetDB.Benchmarks/docker/docker-compose.yml down -v

# 删除 SonnetDB 临时数据
rm -rf /tmp/sonnetdb_bench_*
```

## 故障排除

### IoTDB 连接超时

```bash
# 检查 IoTDB 容器状态
docker ps | grep iotdb

# 查看日志
docker logs sndb-bench-iotdb | tail -50

# 重启 IoTDB
docker compose -f SonnetDB/tests/SonnetDB.Benchmarks/docker/docker-compose.yml restart iotdb
```

### 内存不足错误

减少测试数据量：

编辑 `DatabaseComparisonBenchmark.cs`，修改循环参数：

```csharp
// 从 288 改为 28（减少 90%）
for (var i = 0; i < 28; i++)  // 原为 288

// 从 100 改为 10（再减少 90%）
for (var j = 0; j < 10; j++)  // 原为 100
```

### 网络速度慢

IoTDB 通过 HTTP 通信，网络延迟可能显著影响性能。建议：

1. 使用本地 IoTDB（而非远程）
2. 检查网络连接质量
3. 增加 HTTP 超时时间

## 数据分析

导出结果供进一步分析：

```csharp
// 修改 PrintStatistics 方法，添加 CSV 导出
var csv = string.Join("\n", results.Select(r =>
    $"{r.RunNumber},{r.DatabaseName},{r.TotalMilliseconds},{r.PointsPerSecond:F0}"));
File.WriteAllText("benchmark_results.csv", csv);
```

## 参考资源

- [SonnetDB 文档](https://github.com/IoTSharp/SonnetDB)
- [Apache IoTDB 文档](https://iotdb.apache.org/)
- [基准测试最佳实践](https://github.com/dotnet/performance)

---

有任何问题，请参考 `DATABASE_COMPARISON_BENCHMARK_README.md` 获取详细文档。
