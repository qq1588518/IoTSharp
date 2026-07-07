# 性能对比：SonnetDB vs InfluxDB vs TDengine vs SQLite

在选型时序数据库时，性能是最关键的考量因素之一。但"性能"是一个多维度的概念，包括写入吞吐量、查询响应时间、资源消耗等。本文将通过基准测试数据，对比 SonnetDB 与 InfluxDB、TDengine、SQLite 在典型场景下的表现，并补充一组 SonnetDB Server 与 Apache IoTDB Server 的同口径服务端写入结果，帮助您做出更明智的选择。

## 测试环境与方法

所有测试在相同硬件条件下进行：Intel Xeon E5-2686 v4 @ 2.30GHz，16GB RAM，SSD 存储，Ubuntu 22.04 LTS。测试数据集为 100 万个模拟 IoT 传感器数据点，包含时间戳、设备 ID（标签）、温度值、湿度值和电压值（字段）。每个数据库均使用其推荐的配置进行调优。

## 批量写入性能

在批量写入 100 万条时序数据的测试中，结果如下：

| 数据库   | 写入耗时 | 每秒写入条数 | CPU 使用率 |
| -------- | -------: | -----------: | ---------: |
| SonnetDB | 545 ms   |    1,834,862 |        35% |
| SQLite   | 1,820 ms |      549,450 |        28% |
| TDengine | 3,150 ms |      317,460 |        52% |
| InfluxDB | 5,200 ms |      192,307 |        68% |

SonnetDB 在此项测试中表现突出，仅用 545 毫秒就完成了 100 万条数据的写入，吞吐量达到每秒 183 万条。这一成绩得益于其 LSM-Tree 架构的高效写入路径和纯 C# 实现的低开销运行时。SQLite 作为通用嵌入式数据库也表现不错，但在时序写入优化上不如 SonnetDB。InfluxDB 和 TDengine 由于客户端-服务器通信开销和各自的数据传输协议，写入延迟较高。

## 补充：SonnetDB Server vs Apache IoTDB Server 同口径写入

上面的主表更偏向“SonnetDB 嵌入式 / 服务端能力总览”，而不是严格意义上的同链路对打。为了避免把 SonnetDB 的进程内写入路径和 IoTDB 的 REST 服务端路径直接混为一谈，我们在 2026-05-06 又补跑了一组专门的 server-vs-server 对比。

这组测试固定为 1,000 个设备、每设备 30 个字段、12 个时间点，总计每阶段 12,000 行、360,000 个字段值，按 `AB BA AB BA` 四轮执行。SonnetDB 走 HTTP JSON points 批量端点，IoTDB 走 REST v2 `insertTablet`。

| 数据库 | 平均耗时(ms) | 最小耗时(ms) | 最大耗时(ms) | 平均吞吐量(values/sec) |
| --- | ---: | ---: | ---: | ---: |
| SonnetDB Server | 20,892 | 7,402 | 28,342 | 22,867 |
| IoTDB Server | 33,050 | 22,019 | 41,267 | 11,541 |

结论很直接：在同口径服务端写入场景下，SonnetDB Server 平均吞吐约为 Apache IoTDB Server 的 **1.98x**。

因此，阅读这篇横评时可以按下面的方式理解结果：

- 如果你关心嵌入式或边缘部署，优先看前面的 SonnetDB vs SQLite / InfluxDB / TDengine 总表。
- 如果你关心平台化服务端部署，并且要把 SonnetDB 与 IoTDB 正面比较，优先引用这组 server-vs-server 数据。
- 不要把 SonnetDB 的嵌入式结果直接拿去和 IoTDB 服务端结果做宣传口径上的等价对比。

## 查询性能

在典型时序查询场景下的表现（查询 10 万条数据，包含时间范围过滤和标签过滤）：

| 查询类型 | SonnetDB | InfluxDB | TDengine | SQLite |
| --- | ---: | ---: | ---: | ---: |
| 按时间范围查询 | 12 ms | 18 ms | 15 ms | 45 ms |
| 带标签过滤 | 8 ms | 14 ms | 10 ms | 38 ms |
| 聚合查询（AVG） | 22 ms | 31 ms | 28 ms | 78 ms |
| 降采样查询 | 35 ms | 42 ms | 38 ms | 120 ms |

SonnetDB 在各类查询场景中均保持领先，这得益于其列式存储格式、时间分区索引和内存友好的数据结构设计。

## 资源消耗对比

| 指标 | SonnetDB | InfluxDB | TDengine | SQLite |
| --- | ---: | ---: | ---: | ---: |
| 空闲内存 | 18 MB | 85 MB | 45 MB | 2 MB |
| 磁盘占用（100万条） | 28 MB | 35 MB | 22 MB | 32 MB |
| 二进制体积 | 12 MB | 280 MB | 95 MB | 1.5 MB |

SonnetDB 的资源消耗控制得相当出色：空闲内存仅 18 MB，比 InfluxDB 低了近 5 倍。需要注意的是，SQLite 虽然资源占用更低，但它是通用嵌入式数据库，在时序数据读写优化和功能丰富度上无法与 SonnetDB 相提并论。

## 功能特性对比

| 特性       | SonnetDB | InfluxDB  | TDengine | SQLite        |
| ---------- | -------- | --------- | -------- | ------------- |
| 嵌入式部署 | 是       | 否        | 否       | 是            |
| 向量索引   | 是       | 否        | 否       | 否            |
| AI Copilot | 是       | 否        | 否       | 否            |
| 许可证     | MIT      | AGPL/商用 | AGPL     | Public Domain |
| AOT 兼容   | 是       | 否        | 否       | 是            |

## 选型建议

- **如果您需要嵌入式时序数据库**：SonnetDB 是首选，它在功能丰富度和性能上都优于 SQLite。
- **如果您需要大规模集群部署**：TDengine 和 InfluxDB 在分布式架构上更为成熟。
- **如果您需要单机或中小规模服务端部署，并希望直接与 IoTDB 做 HTTP 服务对比**：当前实测中 SonnetDB Server 的写入吞吐约为 IoTDB Server 的 1.98 倍。
- **如果您追求极低的资源占用**：SQLite 体积最小，但功能受限；SonnetDB 提供了更好的功能与尺寸平衡。
- **如果您需要向量搜索和 AI 能力**：SonnetDB 是目前唯一提供内置向量索引和 AI Copilot 的时序数据库。

总体而言，SonnetDB 在嵌入式时序数据库领域展现了强劲的竞争力；而在服务端同口径写入对比中，也已经证明自己相对 Apache IoTDB 具备明显优势。在性能、功能丰富度和资源效率之间，SonnetDB 仍然保持了非常均衡的取舍。
