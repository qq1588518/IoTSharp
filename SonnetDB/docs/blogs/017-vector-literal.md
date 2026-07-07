## VECTOR 字面量：使用 `[v0, v1, ...]` 语法操作嵌入向量

向量嵌入是机器学习和 AI 应用的核心数据形式。SonnetDB 原生支持 `VECTOR(n)` 数据类型，并通过方括号字面量语法 `[v0, v1, ...]` 让向量的写入和查询变得直观易用。

### VECTOR 类型定义

在创建表时，可以使用 `VECTOR(n)` 指定向量的维度：

```sql
-- 创建存储文本嵌入向量的时序表
CREATE TABLE document_embeddings (
    ts TIMESTAMP NOT NULL,
    doc_id TAG,
    chunk_id TAG,
    embedding VECTOR(384),
    token_count BIGINT
);

-- 创建存储图像特征向量的表
CREATE TABLE image_features (
    ts TIMESTAMP NOT NULL,
    image_id TAG,
    features VECTOR(512),
    label VARCHAR
);
```

维度 `n` 在表创建时确定，后续插入的向量必须与声明的维度一致，否则会报错。

### 方括号字面量语法

SonnetDB 使用 `[v0, v1, ..., vn]` 方括号语法表示向量字面量。元素之间用逗号分隔，支持整数和浮点数：

```sql
-- 插入一条 4 维向量记录
INSERT INTO document_embeddings (ts, doc_id, chunk_id, embedding, token_count)
VALUES ('2025-07-01T10:00:00Z', 'doc-001', 'chunk-01',
        [0.125, -0.034, 0.567, 0.891], 256);
```

对于高维向量，一次性写入所有元素即可：

```sql
-- 插入 384 维 BERT 嵌入向量
INSERT INTO document_embeddings (ts, doc_id, chunk_id, embedding, token_count)
VALUES ('2025-07-01T10:00:00Z', 'doc-002', 'chunk-01',
        [0.012, 0.345, -0.078, 0.234, 0.567, -0.123 /* ... 共 384 个元素 */],
        320);
```

当然，在实际代码中，高维向量通常由应用程序动态生成，而不是手写。

### 向量字面量的类型推断

向量字面量中的元素可以是整数或浮点数。如果所有元素都是整数，SonnetDB 会将其视为整数向量；如果至少有一个浮点数元素，则整个向量被推断为浮点向量：

```sql
-- 整数向量
INSERT INTO image_features (ts, image_id, features)
VALUES ('2025-07-01T12:00:00Z', 'img-001', [0, 255, 128, 64]);
-- 等价于 VECTOR(4) 类型

-- 浮点向量（推荐）
INSERT INTO image_features (ts, image_id, features)
VALUES ('2025-07-01T12:00:00Z', 'img-002', [0.0, 255.0, 128.5, 64.2]);
```

### 批量插入向量数据

向量数据同样支持批量插入，适合嵌入模型批量推理后的写入场景：

```sql
INSERT INTO document_embeddings (ts, doc_id, chunk_id, embedding, token_count)
VALUES
    ('2025-07-01T10:00:00Z', 'doc-001', 'chunk-01', [0.12, -0.03, 0.56, 0.89], 256),
    ('2025-07-01T10:00:00Z', 'doc-001', 'chunk-02', [-0.08, 0.45, 0.12, 0.33], 198),
    ('2025-07-01T10:00:00Z', 'doc-001', 'chunk-03', [0.67, -0.21, 0.05, 0.78], 302);
```

### 向量距离函数示例

写入向量数据后，就可以使用距离函数进行语义搜索了：

```sql
-- 余弦相似度搜索：查找语义上最相似的文档片段
SELECT doc_id, chunk_id, token_count,
       cosine_distance(embedding, [0.10, -0.05, 0.60, 0.85]) AS similarity
FROM document_embeddings
ORDER BY similarity
LIMIT 5;
```

```sql
-- L2 欧氏距离搜索：查找特征最近邻
SELECT image_id, ts,
       l2_distance(features, [128.0, 200.0, 64.0, 32.0]) AS dist
FROM image_features
ORDER BY dist
LIMIT 10;
```

### 性能建议

1. **合理选择维度**：维度越高，存储和计算开销越大。文本嵌入常用 384-768 维，图像特征常用 512-1024 维，根据业务需求选择恰当维度。
2. **配合 HNSW 索引**：对于大规模向量搜索（10 万条以上），建议创建 HNSW 索引来加速。
3. **归一化**：使用余弦距离时，建议在写入前对向量进行归一化，可以提升搜索精度。

VECTOR 类型和方括号字面量语法让 SonnetDB 成为一个天然的时序向量数据库，既能存储时序，又能执行语义搜索，一套系统解决两类需求。
