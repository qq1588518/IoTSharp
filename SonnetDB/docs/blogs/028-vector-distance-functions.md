## 标量向量函数：cosine_distance / l2_distance / inner_product / vector_norm

向量检索是 SonnetDB 的核心功能之一，而向量距离度量是向量检索的基础。SonnetDB 提供了四种标量向量函数，让你可以在普通的 SELECT 查询中直接计算向量之间的距离和范数，无需借助外部计算工具。

### 四种向量距离函数

SonnetDB 支持以下四种向量距离/度量函数：

**cosine_distance(vector1, vector2)**：余弦距离。衡量两个向量在方向上的差异，取值范围为 `[0, 2]`。值越小表示方向越一致，常用于文本和语义搜索。

**l2_distance(vector1, vector2)**：欧几里得距离（L2 范数距离）。衡量两个向量在欧几里得空间中的直线距离。值越小表示在空间上越接近。

**inner_product(vector1, vector2)**：内积（点积）。`sum(v1_i * v2_i)`，反映两个向量的相似程度。内积越大表示相似度越高（注意与余弦距离的方向相反）。

**vector_norm(vector)**：向量范数（L2 范数）。`sqrt(sum(v_i^2))`，计算向量自身的长度。

### 在查询中使用

假设我们有一个文档向量表，存储了文本的嵌入向量（4 维示例）：

```sql
SELECT
    title,
    cosine_distance(embedding, [0.10, 0.20, 0.30, 0.40]) AS cos_dist,
    l2_distance(embedding, [0.10, 0.20, 0.30, 0.40])     AS l2_dist,
    inner_product(embedding, [0.10, 0.20, 0.30, 0.40])   AS dot_prod,
    vector_norm(embedding)                                AS norm
FROM documents
WHERE source = 'wiki';
```

这条查询会返回每个文档标题与目标向量之间的三种距离/相似度指标，以及每个嵌入向量自身的 L2 范数。

### 函数与度量的关系

理解这些函数之间的关系，有助于正确选择和解读结果：

- `cosine_distance(a, b) = 1 - cosine_similarity(a, b)`
- `l2_distance(a, b) = sqrt(sum((a_i - b_i)^2))`
- `inner_product(a, b) = sum(a_i * b_i)`
- `vector_norm(a) = sqrt(inner_product(a, a))`

余弦距离与内积之间存在关联：如果两个向量都已归一化（`vector_norm = 1`），则 `cosine_distance = 1 - inner_product`。

### 选择合适的距离度量

在实际应用中，选择合适的距离度量取决于数据的特性和业务需求：

- **余弦距离**最适合文本搜索和语义相似度场景，因为它只关注方向而不受向量长度的影响
- **L2 距离**适合向量长度本身有意义的场景，例如物理测量数据或已经归一化的嵌入
- **内积**适合需要考虑向量长度和方向的场景，如推荐系统中的矩阵分解

### 与 KNN 表值函数的关系

这些标量向量函数与 `knn()` 表值函数使用相同的底层距离计算实现。当你使用 `knn()` 时，可以指定距离度量类型：

```sql
-- 余弦距离 KNN（默认）
SELECT * FROM knn(documents, embedding, [0.10, 0.20, 0.30, 0.40], 3);

-- L2 距离 KNN
SELECT * FROM knn(documents, embedding, [0.10, 0.20, 0.30, 0.40], 3, 'l2');

-- 内积距离 KNN
SELECT * FROM knn(documents, embedding, [0.10, 0.20, 0.30, 0.40], 3, 'inner_product');
```

使用标量向量函数则更加灵活，可以在投影中同时计算多个距离指标，或与其他标量函数组合使用，构建更复杂的分析逻辑。
