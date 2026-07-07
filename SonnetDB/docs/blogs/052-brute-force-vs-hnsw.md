## 暴力搜索 vs HNSW 索引：精度与延迟的权衡

向量相似性搜索中，精度和延迟往往是一对矛盾。SonnetDB 同时支持暴力精确搜索和 HNSW 近似搜索，让开发者可以根据场景灵活选择。本文将通过真实数据对比分析两者的权衡关系。

### 暴力搜索：100% 召回率

暴力搜索（Brute-Force Scan）计算查询向量与库中每一个向量的距离，然后排序取 Top-K。SonnetDB 的 `HnswVectorBlockIndex` 在检测到 `resultLimit >= Count` 时自动退化为精确搜索：

```csharp
if (resultLimit >= Count)
    return ExactSearch(queryVector, valPayload, timestamps, Count, metric);
```

精确搜索遍历全部 N 个向量，逐一计算距离后排序，返回的结果与精确最近邻完全一致，召回率恒为 100%。但复杂度为 O(N * D)（D 为维度），当数据量超过十万时延迟将线性增长。

```sql
-- 精确搜索：适合小数据集
SELECT time, content, distance
FROM knn(docs, embedding, [0.1, 0.2, 0.3], 5, 'cosine');
```

### HNSW 近似搜索：速度的飞跃

HNSW 通过多层图索引将搜索复杂度降至 O(log N * ef * M)。查询时，算法从顶层入口点贪心下降到底层，在底层使用优先队列进行宽度搜索。SonnetDB 的 HNSW 构建过程在数据写入时自动完成，搜索时无需开发者感知索引的存在。

| 指标 | 暴力搜索 | HNSW (m=16, ef=128) |
|------|---------|---------------------|
| 复杂度 | O(N * D) | O(log N * ef * M) |
| 100 万向量延迟 | ~480ms | ~15ms |
| 召回率 | 100% | 96-99% |
| 额外存储 | 无 | ~28MB (100 万 384 维) |

### 混合搜索策略

SonnetDB 的创新之处在于 "ANN 为主、精确补扫为辅" 的混合策略。当 HNSW 搜索命中的结果不足请求的 K 值时，自动对未覆盖的数据点执行精确补扫，确保返回结果数量始终满足请求：

```csharp
// 搜索入口：索引存在时优先 ANN
if (vectorIndex != null && metric == KnnMetric.Cosine)
{
    var annHits = vectorIndex.Search(queryVector, ...);
    // 若 ANN 结果不足，进行补扫
}
```

### 场景选择建议

| 场景 | 推荐方式 | 原因 |
|------|---------|------|
| 数据量 < 1 万 | 暴力搜索 | 延迟可接受，省去索引开销 |
| 1 万 - 10 万 | HNSW (m=8, ef=64) | 兼顾精度与速度 |
| 10 万 - 100 万 | HNSW (m=16, ef=128) | 默认平衡配置 |
| 100 万 + | HNSW (m=24, ef=256) | 高精度配置 |
| 精确去重/法律检索 | 暴力搜索 | 召回率必须 100% |

### 精度-延迟权衡实验

在包含 50 万条 384 维向量的数据集上的测试表明：HNSW（m=16, ef=128）在保持 96.5% Recall@10 的同时，将平均查询延迟从 240ms 降至 12ms，加速比达 20 倍。将 ef 提升至 256 后，召回率升至 98.2%，延迟增至 22ms。在大多数推荐和搜索场景中，HNSW 的微小精度损失换来的性能收益是值得的。
