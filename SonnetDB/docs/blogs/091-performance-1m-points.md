## SonnetDB 写入性能实录：545 ms 写入 100 万点，吞吐 1.83 M pts/s

时序数据库的写入性能是衡量系统能力的核心指标。在 PR #49 的全量基准测试中，SonnetDB 在标准硬件上实现了 545 ms 写入 100 万时序数据点、等效吞吐量 **1.83 M pts/s** 的成果。本文拆解这一数字背后的技术栈。

### 测试环境与结果

| 配置项 | 参数 |
|--------|------|
| CPU | Intel i9-13900HX |
| 内存 | 32 GB DDR5 |
| 存储 | NVMe SSD |
| 操作系统 | Windows 11 / .NET 10.0.6 |
| 数据量 | 1,000,000 点 |

横向对比结果：

| 数据库 | 写入耗时 | 吞吐量 | 倍数 |
|--------|---------|--------|------|
| **SonnetDB** | **545 ms** | **1,834,862 pts/s** | **1.0x** |
| SQLite | 811 ms | 1,233,045 pts/s | 1.49x |
| TDengine Schemaless LP | 996 ms | 1,004,016 pts/s | 1.83x |
| InfluxDB 2.7 | 5,222 ms | 191,496 pts/s | 9.58x |
| TDengine REST INSERT | 44,137 ms | 22,656 pts/s | 81x |

### 核心优化一：真批量写入（PR #46）

`Tsdb.WriteMany(ReadOnlySpan<Point>)` 整批仅取一次 `_writeSync` 锁、批末仅 `Signal` 一次后台 Flush，消除了逐点加锁的串行化开销。内存分配减少 42~58%：

```csharp
// 批量写入，整批仅一次锁
var points = new Point[batchSize];
// 填充 points
db.WriteMany(points.AsSpan());
```

### 核心优化二：服务端零拷贝路径（PR #47）

`BulkIngestEndpointHandler` 通过 `ArrayPool<byte>` 租借缓冲区，`JsonPointsReader` 直接持有 `ReadOnlyMemory<byte>` 而非解码中间字符串，1M 点内存分配从 668 MB 降至 34~71 MB：

```bash
# 服务端 LP 端点写入 100 万点
curl -X POST "http://127.0.0.1:5080/v1/db/metrics/measurements/sensor/lp?flush=false" \
  -H "Authorization: Bearer <token>" \
  --data-binary @1m-points.lp
```

服务端结果：LP 端点 1.20 s / 52 MB，JSON 端点 1.20 s / 71 MB，Bulk 端点 1.10 s / 34 MB，比 SQL Batch 路径快 15~17x。

### 核心优化三：三档 flush 模式（PR #48）

```bash
# 仅入 MemTable + WAL，立即返回
?flush=false

# 发出 Flush 信号后返回，不等待落盘
?flush=async

# 同步等待完全落盘
?flush=true
```

高吞吐场景推荐 `flush=false` 或 `flush=async`，将落盘延迟从关键路径剥离。

### LSM-Tree 写入路径

```text
WriteMany → WAL（顺序 I/O）→ MemTable（内存跳表）→ BackgroundFlush → Segment
```

读写职责分离确保写入不阻塞查询，WAL 组提交减少 fsync 调用频率。545 ms / 1M 点的成绩验证了 SonnetDB 嵌入式架构的高效写入路径，对于 IoT 平台和运维监控等高吞吐场景完全胜任。
