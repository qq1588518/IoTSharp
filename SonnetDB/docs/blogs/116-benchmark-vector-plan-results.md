## 向量基准通稿：Brute-force 与 HNSW 在时序数据库内的召回对比

向量计算让 SonnetDB 可以把语义检索、相似工况检索和 Copilot 知识库召回放在同一个时序数据库里。向量基准单独评估精确扫描、HNSW ANN 查询和 Recall@10。

### 通稿

本轮向量基准使用 384 维 L2 归一化向量，默认覆盖 10k 与 100k 两档数据集，每轮固定 10 个 query，比较 brute-force 精确 Top10 与 HNSW Top10 的延迟，并通过同批 query 计算 Recall@10。该测试既服务数据库向量字段，也服务 Copilot 文档知识库的召回回归。

当前可执行基准聚焦 SonnetDB 内部 brute-force 与 HNSW。pgvector、sqlite-vec、TimescaleDB + pgvector 的横向对照需要新增外部环境、索引构建参数和召回测量脚本，不能直接拿不同默认参数做结论。

### 对比方案

| 对照对象 | 当前状态 | 方案 |
| --- | --- | --- |
| SonnetDB brute-force | 已实现 | 精确 Top10，作为召回基线 |
| SonnetDB HNSW | 已实现 | `HnswVectorBlockIndex`，cosine Top10 |
| pgvector | 待补 | PostgreSQL + pgvector，HNSW/IVFFlat 分别测 |
| sqlite-vec | 待补 | SQLite 扩展，需固定向量存储和索引参数 |
| InfluxDB/TDengine/IoTDB | 待补/通常不适用 | 默认不是向量数据库，除非使用外部扩展或客户端计算 |

运行命令：

```powershell
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *Vector*

# 显式长测 1M 向量
$env:SONNETDB_VECTOR_BENCH_INCLUDE_1M="1"
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *Vector*
```

### 对比结果

| 方法 | 数据规模 | 平均耗时 | 分配 | Recall@10 | 备注 |
| --- | ---: | ---: | ---: | ---: | --- |
| Brute-force Top10 | 10k | 6.183 ms | 0 MB | 1.000 | 精确基线 |
| HNSW Top10 | 10k | 2.592 ms | 0.03 MB | 0.820 | ANN 查询，Recall 由同批 query probe |
| HNSW Recall@10 | 10k | 2.607 ms | 0.03 MB | 0.820 | 方法耗时，返回值另由 probe 读取 |
| Brute-force Top10 | 100k | 60.799 ms | <0.01 MB | 1.000 | 精确基线 |
| HNSW Top10 | 100k | 3.480 ms | 0.13 MB | 0.340 | ANN 查询，默认参数召回偏低 |
| HNSW Recall@10 | 100k | 3.598 ms | 0.13 MB | 0.340 | 方法耗时，返回值另由 probe 读取 |

### 结论口径

向量报告必须同时呈现延迟与召回率。HNSW 如果只快但召回不足，不能作为检索质量结论；brute-force 如果慢但精确，应作为正确性基线保留。
