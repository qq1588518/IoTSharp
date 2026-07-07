## SonnetDB KNN 向量搜索入门：knn() TVF 语法与距离度量选择

随着 AI 应用的普及，向量检索已成为现代数据库的核心能力之一。SonnetDB 从 v0.6.0 版本开始内置了 KNN（K-Nearest Neighbors）向量搜索功能，通过 `knn()` 表值函数提供了简洁而强大的向量相似性搜索接口。

### knn() 函数语法

`knn()` 是一个表值函数（Table-Valued Function），必须出现在 `FROM` 子句中：

```sql
SELECT * FROM knn(
  measurement_name,
  vector_column_name,
  [query_vector_values],
  k
  [, 'metric']
) [WHERE tag_filter AND time_filter];
```

参数详解：
- **measurement_name**：包含向量列的表名
- **vector_column_name**：VECTOR 类型的字段列名
- **query_vector_values**：查询向量字面量，例如 `[0.1, 0.2, 0.3, ...]`
- **k**：返回的最相近结果数量（正整数）
- **metric**（可选）：距离度量，默认 `'cosine'`

### 基础使用示例

创建一个包含向量的 measurement 并执行 KNN 搜索：

```sql
CREATE MEASUREMENT embeddings (
    id TAG,
    content TAG,
    vector FIELD VECTOR(384)
);

-- 搜索与查询向量最接近的 10 条记录
SELECT time, id, content, distance
FROM knn(embeddings, vector, [0.12, 0.34, 0.56, ...], 10, 'cosine');
```

查询结果包含 `time`（时间戳）、`distance`（距离）以及 measurement 中的所有 tag 和 field 列，按距离升序排列。

### 距离度量选择

SonnetDB 支持三种距离度量：

**余弦距离（cosine，默认）**

`cosine_distance = 1 - cos(θ)`，值域 `[0, 2]`。衡量两个向量的方向差异而非大小差异：

```sql
SELECT * FROM knn(docs, embedding, [0.1, 0.2, 0.3], 5, 'cosine');
```

适用于文本嵌入、语义搜索等场景——当向量被归一化后，余弦距离等价于 L2 距离。

**L2 欧几里得距离（l2）**

`l2_distance = sqrt(Σ(a_i - b_i)²)`，衡量向量在欧几里得空间中的绝对距离：

```sql
SELECT * FROM knn(products, features, [1.5, -0.3, 2.1], 5, 'l2');
```

适用于图像特征、物理量等需要衡量幅度差异的场景。

**内积距离（inner_product）**

`inner_product = -a·b`，返回负点积，值越小表示点积越大（越相似）：

```sql
SELECT * FROM knn(recommender, user_vector, [0.5, 0.8, -0.2], 5, 'inner_product');
```

适用于推荐系统中的矩阵分解向量、需要捕捉偏好幅度的场景。

### 结果解读

`knn()` 的返回列中，`distance` 是关键指标。对于余弦距离，小于 0.2 表示非常相似，0.2~0.5 表示一般相似，大于 0.5 可能相关性较弱。用户可以根据业务需求设置 `distance` 的阈值过滤：

```sql
SELECT * FROM (
  SELECT * FROM knn(embeddings, vector, [0.1, 0.2, ...], 20)
) WHERE distance < 0.3;
```

SonnetDB 的 `knn()` 表值函数为向量搜索提供了声明式的 SQL 接口，无论是构建语义搜索引擎、推荐系统还是异常检测管道，都可以在纯 SQL 层面完成。
