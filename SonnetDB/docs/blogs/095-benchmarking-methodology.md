# SonnetDB 基准测试方法论：BenchmarkDotNet、统一数据与多 DB 对比

严谨的基准测试是性能声明的基石。SonnetDB 团队基于 **BenchmarkDotNet** 构建了一套可复现的测试体系，覆盖写入、查询、聚合、Compaction 等全链路，并统一接入 SQLite、InfluxDB、TDengine 进行横向对比。

## 框架选择：BenchmarkDotNet

所有基准使用 .NET 生态最权威的 BenchmarkDotNet v0.14，自动处理预热、迭代、统计分析：

```csharp
[MemoryDiagnoser]
[MinColumn, MaxColumn]
public class InsertBenchmark
{
    private Tsdb _db;
    private Point[] _points;

    [Params(100_000, 1_000_000)]
    public int PointCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _db = Tsdb.Open(new TsdbOptions { RootDirectory = $"./bench-{Guid.NewGuid()}" });
        _points = DataGenerator.Generate(PointCount);
    }

    [Benchmark]
    public void WriteMany_SonnetDB()
    {
        _db.WriteMany(_points.AsSpan());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        Directory.Delete(_db.RootDirectory, true);
    }
}
```

## 统一数据规模

测试数据使用统一的 `DataGenerator`，确保跨数据库的数据分布一致：

```csharp
public static Point[] Generate(int count)
{
    var rng = new Random(42);  // 固定种子
    return Enumerable.Range(0, count).Select(i => new Point
    {
        Timestamp = 1713600000000 + i * 1000,
        Measurement = "cpu",
        Tags = new() { ["host"] = $"server-{i % 100}" },
        Fields = new() { ["usage"] = rng.NextDouble() }
    }).ToArray();
}
```

规模分三个档次：

- 小规模：10 万点（单元测试级别）
- 中规模：100 万点（标准报告基准）
- 大规模：1000 万点（可选，通过环境变量开启）

## 多数据库对比方案

`tests/SonnetDB.Benchmarks/` 中的跨数据库基准遵循以下原则：

1. **同机运行**：`docker-compose.yml` 启动 InfluxDB + TDengine + SonnetDB 容器
2. **等量数据**：各库写入完全相同的点集
3. **推荐配置**：各数据库使用官方推荐的最佳参数
4. **等量迭代**：BenchmarkDotNet 自动为每个目标分配足够迭代次数

```csharp
// InfluxDB 对比基准（通过 InfluxDB HTTP API）
[Benchmark]
public async Task InfluxDB_Insert_1M()
{
    var lp = BuildLineProtocol(_points);
    await _influxClient.WriteAsync(lp, "ns");
}
```

## 对比公平性：不要把嵌入式和服务端链路混为一谈

SonnetDB 同时支持嵌入式与服务端两种形态，这既是优势，也意味着做横向对比时必须先定义清楚“比较的到底是哪一层”。

- 如果对手库只能通过网络服务访问，而 SonnetDB 直接走进程内 `Tsdb.WriteMany(...)`，那么测到的是“引擎本体 vs 远程协议栈”的差异。
- 如果双方都走 HTTP/REST/RPC，则测到的是“服务端整体链路”的差异，包含协议解析、认证、中间件、序列化与网络回环成本。

因此，从 2026-05-06 起，SonnetDB 与 Apache IoTDB 的公开对比默认补充一组 `--comparison-server` 数据，确保两边都使用服务端路径：SonnetDB 走 `/v1/db/{db}/measurements/{m}/json`，IoTDB 走 REST v2 `insertTablet`。在该口径下，1,000 设备 × 30 字段 × 12 时间点、`AB BA AB BA` 四轮的实测结果为：

| 数据库 | 平均耗时(ms) | 平均吞吐量(values/sec) |
| --- | ---: | ---: |
| SonnetDB Server | 20,892 | 22,867 |
| IoTDB Server | 33,050 | 11,541 |

相对性能约为 **1.98x**。这组数据应优先用于对外描述“SonnetDB Server vs IoTDB Server”的结果；而嵌入式数据继续保留，但必须明确标注为不同方法学。

## DevOps 集成

两个 C# 工具链驱动全流程：

```bash
# 1. 启动基准环境（构建 + 启动容器 + 等待健康检查）
dotnet run --project eng/benchmarks/start-benchmark-env/

# 2. 运行全部基准（Release + BenchmarkDotNet）
dotnet run --project eng/benchmarks/run-benchmarks/ -- --filter *
```

## 报告规范

每项基准报告包含：

- Mean（均值）、StdDev（标准差）
- 吞吐量（points/sec）
- 内存分配（allocated bytes）
- 95% 置信区间
- BenchmarkDotNet 统计摘要

```text
| Method                     | Mean    | Allocated | Throughput      |
|---------------------------|---------|-----------|-----------------|
| WriteMany_SonnetDB        | 545 ms  | 530 MB    | 1,834,862 pts/s |
| WriteMany_SQLite          | 811 ms  | 465 MB    | 1,233,045 pts/s |
| WriteMany_InfluxDB        | 5,222 ms| 1,457 MB  | 191,496 pts/s   |
```

完整基准代码与结果位于 `tests/SonnetDB.Benchmarks/README.md`。所有测试数据开源可复现，团队在 CI 中集成 nightly 回归以防止性能退化。
