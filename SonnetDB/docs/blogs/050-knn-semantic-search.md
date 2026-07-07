## SonnetDB 语义搜索实战：knn() 与标签过滤和时间范围的联合查询

在生产级向量搜索应用中，单纯的向量相似性往往不够——用户通常还需要结合业务标签过滤和时间范围约束来精确控制搜索结果。SonnetDB 的 `knn()` 表值函数天然支持 `WHERE` 子句中的标签过滤和时间范围过滤，实现了"语义相似度 + 业务条件 + 时间窗口"的三维联合查询。

### 标签过滤 + KNN 的黄金组合

考虑一个文档/知识库搜索场景，每条记录包含 `author`、`category` 等标签，以及文本嵌入向量：

```sql
-- 在特定分类中搜索语义相关文档
SELECT time, title, author, distance
FROM knn(knowledge_base, embedding, [0.15, 0.28, ..., 0.09], 10, 'cosine')
WHERE category = 'technical'
  AND author = 'zhangsan';
```

SonnetDB 的查询引擎会先通过 tag 索引过滤出符合 `category='technical'` 和 `author='zhangsan'` 的序列，然后仅在匹配的序列中执行 KNN 向量搜索，避免了全量扫描。

### 时间范围过滤

时序数据库的核心优势在于时间维度。结合时间过滤可以进行高效的范围限定：

```sql
-- 搜索最近一周内与查询向量最相似的文档
SELECT time, title, content, distance
FROM knn(knowledge_base, embedding, [0.1, 0.2, 0.3], 5)
WHERE time > NOW() - 7d
  AND distance < 0.4;
```

`WHERE distance < 0.4` 对 `knn()` 输出的 `distance` 列做后过滤，确保只返回语义相关性足够的搜索结果。

### 多标签组合查询

实际场景中往往需要多个标签的联合过滤：

```sql
-- 支持 AND / OR 多条件组合
SELECT time, doc_id, title, distance
FROM knn(documents, text_vector, [0.22, 0.45, ..., 0.77], 20, 'cosine')
WHERE (department = 'engineering' OR department = 'research')
  AND language = 'zh-CN'
  AND time BETWEEN 1713600000000 AND 1713686400000
  AND distance < 0.5;
```

### 语义搜索 + 聚合分析

更近一步，可以使用 `knn()` 的搜索结果做后续聚合分析：

```sql
-- 搜索相似文档并按作者统计
SELECT author, COUNT(*) AS match_count, AVG(distance) AS avg_dist
FROM knn(documents, embedding, [0.33, 0.11, ..., 0.99], 50, 'cosine')
WHERE project_id = 'proj-alpha'
  AND time > NOW() - 30d
GROUP BY author
ORDER BY match_count DESC;
```

### 混合搜索架构建议

在实际应用中，推荐采用"标签粗筛 → 向量精排"的分层架构：

```
查询请求
  ↓
标签索引过滤（WHERE 条件） → 快速缩小候选集
  ↓
向量相似度搜索（knn TVF） → 语义排序
  ↓
结果后处理（distance 阈值） → 质量控制
```

这种架构下，SonnetDB 同时发挥了倒排索引和向量索引的各自优势。对于包含 10 万个序列但每个序列只有少量数据点的场景，标签过滤可以快速将搜索范围缩小到数百个序列，然后再进行精确的向量距离计算，整体性能提升可达百倍以上。SonnetDB 的这一能力使其成为构建生产级 RAG（检索增强生成）系统和智能推荐引擎的理想选择。
