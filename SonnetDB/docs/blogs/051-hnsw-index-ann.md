## SonnetDB HNSW 索引架构：m/ef 参数调优与 .SDBVIDX 文件格式

SonnetDB 为 VECTOR 类型字段内置了 HNSW（Hierarchical Navigable Small World）图索引，将大规模向量搜索的延迟从秒级降至毫秒级。本文将深入其架构设计，解析 `m` 与 `ef` 参数对性能的影响，以及 `.SDBVIDX` 侧边文件的存储格式。

### Block 级 HNSW 架构

SonnetDB 的 HNSW 索引以存储段（Segment）内的数据 Block 为单位独立构建。每个 VECTOR Block 对应一个 `HnswVectorBlockIndex` 实例，包含完整的多层邻接图。构建完成后，索引序列化写入 `.SDBVIDX` 侧边文件，与主数据文件 `.SDBSEG` 一一对应。搜索时，系统逐 Block 执行 ANN 查询，最终合并各 Block 的 Top-K 结果返回。

```csharp
// HnswVectorBlockIndex.Build() 签名
public static HnswVectorBlockIndex Build(
    int blockIndex,
    ReadOnlySpan<DataPoint> points,
    HnswVectorIndexOptions options)
```

索引构建过程首先为每个向量采样层级（level），然后逐节点插入图中：从顶层入口点开始贪心下降，在每层执行 e f 宽度搜索候选邻居，选取至多 M 个最近邻建立双向连接。

### m 参数详解

`m` 控制图中每个节点在每层保留的最大邻接数。SonnetDB 的 `HnswVectorIndexOptions` 定义如下：

```csharp
public sealed record HnswVectorIndexOptions(int M, int Ef);
```

当插入新节点时，如果候选邻居数超过 M，代码会按距离排序截断：

```csharp
int take = Math.Min(options.M, candidates.Count);
```

调优建议：
- **m=8**：紧凑索引，适合内存敏感场景，召回率约 85-90%
- **m=16**：默认值，大多数场景的平衡选择，召回率约 95%
- **m=24~32**：高精度配置，召回率 98%+，索引体积和构建时间相应增长

### ef 参数详解

`ef` 参数同时影响构建和搜索两个阶段。构建时使用配置的 ef 值作为搜索宽度；搜索时则取 `max(k, Ef)` 作为动态 ef 值：

```csharp
int efSearch = Math.Min(Count, Math.Max(resultLimit, Ef));
var layer = SearchLayer(valPayload, queryVector, entry, 0, efSearch, metric);
```

经验表明，ef 设为 k 的 2-5 倍可在 95% 召回率附近取得最佳性价比。当 ef=200 时典型召回率可达 98% 以上。

### .SDBVIDX 文件格式

`.SDBVIDX` 文件的二进制布局如下：

```
偏移  内容
0     Magic: "SDBVIDX1" (8 bytes)
8     FormatVersion: int32 LE = 1
12    HeaderSize: int32 LE = 32
16    BlockCount: int32 LE
20    填充到 32 字节

[Block 1 序列化]
  BlockIndex: int32
  Count: int32
  Dimension: int32
  M: int32
  Ef: int32
  MaxLevel: int32
  EntryPoint: int32
  [节点 0 的层级数据...]
  [节点 1 的层级数据...]
```

文件通过 `SegmentVectorIndexFile.Write()` 写入，通过 `TryLoad()` 懒加载。若文件缺失或损坏，系统静默回退到精确搜索，保证查询不中断。通过合理调整 m 和 ef，用户可在索引体积、搜索精度和查询延迟之间找到最佳平衡点。
