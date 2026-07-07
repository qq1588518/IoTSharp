## 向量距离度量深度对比：Cosine vs L2 vs Inner Product

选择正确的距离度量是向量搜索成功的关键。SonnetDB 内置三种距离度量，通过 `KnnMetric` 枚举统一管理。本文将深入分析它们的数学定义、适用场景和选择策略。

### 余弦距离（Cosine）

余弦距离衡量向量之间的方向差异而非大小差异。其公式为 `1 - cos(θ) = 1 - (a·b) / (||a|| * ||b||)`，值域 [0, 2]。在 SonnetDB 中实现为 `VectorDistance.ComputeCosine`：

```csharp
public static double ComputeCosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
{
    double dot = 0, normA2 = 0, normB2 = 0;
    for (int i = 0; i < a.Length; i++)
    {
        dot += (double)a[i] * b[i];
        normA2 += (double)a[i] * a[i];
        normB2 += (double)b[i] * b[i];
    }
    if (normA2 == 0 || normB2 == 0) return 1.0;
    return 1.0 - dot / (Math.Sqrt(normA2) * Math.Sqrt(normB2));
}
```

余弦距离最适合文本嵌入和语义搜索。BERT、bge-large-zh 等模型输出的句子向量，其方向编码语义内容，模长意义有限。

```sql
-- 语义搜索：使用余弦距离
SELECT id, content, distance
FROM knn(documents, embedding, [0.12, 0.34, 0.56], 10, 'cosine')
WHERE distance < 0.3;
```

### L2 欧几里得距离

L2 距离计算向量在欧几里得空间中的直线距离：`sqrt(Σ(ai - bi)²)`，值域 [0, +∞)，同时编码方向和大小差异：

```sql
-- 图像特征检索：使用 L2 距离
SELECT image_id, distance
FROM knn(images, feature_vector, [0.5, -0.3, 0.8], 10, 'l2');
```

L2 距离适用于图像特征、音频 MFCC、传感器数值等各维度量纲一致的场景。当两张内容相似但亮度不同的图片，L2 距离能体现亮度差异，余弦距离则不能。

### 负内积距离（Inner Product）

内积距离定义为 `-a·b`（负点积），值域 (-∞, +∞)。值越小表示原始点积越大、越相似。在推荐系统中，用户向量与物品向量的点积直接反映偏好程度：

```sql
-- 推荐系统：使用内积距离
SELECT item_id, -distance AS score
FROM knn(recommender, user_vec, [0.3, 0.7, -0.1], 20, 'inner_product')
ORDER BY score DESC;
```

### 距离度量选择指南

SonnetDB 统一通过 `KnnMetric` 枚举和 `VectorDistance.Compute()` 调度：

```csharp
public static double Compute(KnnMetric metric, ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    => metric switch
    {
        KnnMetric.L2 => ComputeL2(a, b),
        KnnMetric.InnerProduct => ComputeNegativeInnerProduct(a, b),
        _ => ComputeCosine(a, b),
    };
```

| 度量 | 枚举值 | 值域 | 最佳场景 |
|------|--------|------|---------|
| 余弦 | Cosine (默认) | [0, 2] | 语义搜索、文档相似度 |
| L2 | L2 | [0, +∞) | 图像检索、数值特征 |
| 负内积 | InnerProduct | (-∞, +∞) | 推荐系统、协同过滤 |

**选择原则**：文本/NLP 任务首选 Cosine；图像/音频等信号首选 L2；推荐排序选择 InnerProduct。如果向量已归一化，三者排序等价，但 Cosine 可解释性最佳。
