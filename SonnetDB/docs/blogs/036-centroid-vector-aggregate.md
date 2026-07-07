## 向量维度平均：centroid(embedding) 聚合函数详解

随着 AI 嵌入（embedding）向量的广泛应用，时间序列数据库中存储向量数据已经成为常态。SonnetDB 提供了 `centroid(embedding)` 聚合函数，用于计算一组向量的逐维度平均值——即这些向量的几何中心（centroid）。这个函数对聚类分析、原型向量提取和向量质量监控等场景至关重要。

### 什么是向量 centroid？

假设你有一个 3 维向量集合：`[1,2,3]`、`[3,4,5]`、`[5,6,7]`，那么 centroid 就是 `[(1+3+5)/3, (2+4+6)/3, (3+5+7)/3] = [3, 4, 5]`。SonnetDB 的 `centroid()` 对所有输入向量做逐维度平均，输出一个同维度的向量。

### 基本用法

```sql
-- 计算某类图像嵌入的平均向量
SELECT
  category,
  centroid(embedding) AS avg_embedding
FROM image_features
WHERE ts >= '2025-01-01' AND ts < '2025-02-01'
GROUP BY category;
```

```sql
-- 计算每个用户最近 30 天的对话嵌入中心
SELECT
  user_id,
  time_bucket('1 day', ts) AS day,
  centroid(session_embedding) AS daily_center
FROM chat_sessions
WHERE ts >= NOW() - INTERVAL '30 days'
GROUP BY user_id, day
ORDER BY user_id, day;
```

### 与向量距离函数配合使用

SonnetDB 支持 `distance()`、`cosine_similarity()` 等向量运算函数，可以将 centroid 结果作为参照点：

```sql
-- 计算每条记录与当日平均向量的余弦相似度
WITH daily_center AS (
  SELECT
    time_bucket('1 day', ts) AS day,
    centroid(embedding)       AS center_vec
  FROM product_vectors
  WHERE ts >= '2025-04-01'
  GROUP BY day
)
SELECT
  pv.id,
  pv.ts,
  cosine_similarity(pv.embedding, dc.center_vec) AS similarity_to_center
FROM product_vectors pv
JOIN daily_center dc
  ON time_bucket('1 day', pv.ts) = dc.day
WHERE pv.ts >= '2025-04-01'
ORDER BY similarity_to_center DESC;
```

### 批量聚类场景

```sql
-- 用 k-means 风格的 centroid 分析检测 embedding 漂移
SELECT
  time_bucket('1 week', ts)                                 AS week,
  model_version,
  centroid(embedding)                                       AS cluster_center,
  COUNT(*)                                                   AS vector_count,
  ROUND(AVG(vector_magnitude(embedding))::numeric, 4)       AS avg_magnitude
FROM llm_embeddings
WHERE ts >= '2025-01-01'
GROUP BY week, model_version
ORDER BY week, model_version;
```

### 注意事项

- 所有输入向量必须具有相同的维度，否则会抛出异常
- 输入为 NULL 的向量会被自动忽略
- 对于高维向量（如 768 维或 1536 维），centroid 的计算是逐元素 O(d*n) 的。在涉及数十亿向量的超大规模聚合中，建议配合时间分区使用

### 适用场景

- 聚类分析中的质心计算
- embedding 分布漂移监控（生产环境 AI 模型行为变化检测）
- 降维前的向量聚合
- 原型样本提取（每个类别的"平均"语义表示）

SonnetDB 将 `centroid()` 作为内置向量聚合函数，使向量数据的时域聚合变得和传统数值聚合一样自然。
