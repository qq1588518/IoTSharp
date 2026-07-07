## HNSW 向量索引：加速向量搜索的强力引擎

在时序数据库中，向量数据的使用越来越广泛——从嵌入向量相似度搜索到异常检测，向量检索已经是现代分析场景的核心需求。SonnetDB 提供了 HNSW（Hierarchical Navigable Small World）图索引来加速向量搜索，让海量向量中的近似最近邻查找变得飞快。

### HNSW 索引的原理简述

HNSW 是一种基于多层图的近似最近邻（ANN）索引结构。它构建了一个分层的可导航小世界图：顶层包含少量节点，用于快速粗筛；底层包含全部节点，用于精确细化。搜索时从顶层进入，逐层向下，每次只在当前层的邻域内贪婪搜索，最终在底层找到最近邻集合。这种分层策略让搜索复杂度从穷举的 O(n) 降到 O(log n)，在百万级向量数据上能实现毫秒级响应。

### 在 SonnetDB 中创建 HNSW 索引

SonnetDB 使用标准 SQL 语法创建 HNSW 索引。关键参数 `m` 控制每个节点的最大连接数，`ef_construction` 控制建图时的动态列表大小（影响索引质量与构建速度的平衡）。

```sql
-- 创建时序表，包含向量列
CREATE TABLE sensor_embeddings (
    ts TIMESTAMP NOT NULL,
    sensor_id TAG,
    embedding VECTOR(128),
    value DOUBLE
);

-- 为向量列创建 HNSW 索引
CREATE INDEX idx_embedding ON sensor_embeddings (embedding)
WITH INDEX hnsw(m = 16, ef_construction = 200);
```

索引创建后，SonnetDB 会自动在插入数据时构建 HNSW 图结构，并在查询时利用该索引加速向量搜索。

### 利用索引执行向量搜索

HNSW 索引主要加速两类查询：最近邻搜索（ORDER BY 距离 + LIMIT）和范围搜索（WHERE 距离阈值）。SonnetDB 会根据查询计划自动决定是否使用索引。

```sql
-- 最近邻搜索：查找与目标向量最相似的 10 条记录
-- HNSW 索引会自动加速距离计算和候选筛选
SELECT sensor_id, ts, value,
       l2_distance(embedding, '[0.12, 0.34, ...]') AS dist
FROM sensor_embeddings
ORDER BY dist
LIMIT 10;
```

```sql
-- 过滤+搜索结合：先按标签筛选，再向量搜索
SELECT sensor_id, ts, value
FROM sensor_embeddings
WHERE sensor_id = 'accelerometer-01'
  AND ts >= '2025-01-01'
ORDER BY cosine_distance(embedding, '[0.5, 0.2, ...]')
LIMIT 5;
```

### 参数调优建议

- **m（连接数）**：默认 16。值越大，图连接越密集，搜索精度更高但构建更慢。对于高维向量（128+），可以尝试 m=32；对于低维向量，m=8 可能就够了。
- **ef_construction**：默认 200。控制构建时的搜索宽度。值越大，索引质量越高，但构建时间线性增加。生产环境推荐 200-400，原型阶段可以用 100 以加快迭代。
- **ef_search**：搜索时的动态列表大小。SonnetDB 会在每个查询中根据 `LIMIT` 和精度需求自动调整，如果对召回率有特殊要求，可以通过查询 Hint 手动指定。

### 什么时候使用 HNSW

HNSW 特别适合以下场景：向量维度适中（16-512）、数据量大（百万级以上）、对查询延迟敏感。如果你的数据量很小（几千条）或要求绝对精确（需要穷举），可以不建索引，直接使用暴力搜索。但对于大多数生产级向量搜索需求，HNSW 是目前兼顾速度和精度的最佳选择之一。

SonnetDB 的 HNSW 索引让你可以用标准 SQL 语法获得专业级的向量搜索加速，无需学习专门的向量数据库或搜索引擎。
