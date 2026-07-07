## pgvector 兼容运算符：<=> <-> <#>

SonnetDB 在提供完整的标量向量函数的同时，还兼容了 pgvector 的运算符语法。这意味着如果你之前使用过 PostgreSQL 的 pgvector 扩展，可以几乎无缝地将查询迁移到 SonnetDB，只需修改表名和列名即可。

### 三种运算符

pgvector 定义了三种用于向量距离计算的运算符，SonnetDB 完整支持了这三种语法：

**`<=>` 余弦距离运算符**：等价于 `cosine_distance()` 函数，计算两个向量之间的余弦距离。

**`<->` L2 距离运算符**：等价于 `l2_distance()` 函数，计算两个向量之间的欧几里得距离。

**`<#>` 负内积运算符**：注意这是负的内积（`-inner_product`），因为 pgvector 中将 `<#>` 定义为按负内积排序（越小表示越相似），以保持"距离越小越相似"的一致性。

### 基本用法

运算符语法可以将向量距离计算直接嵌入 SELECT 子句，写法更加简洁：

```sql
-- 余弦距离（等价于 cosine_distance）
SELECT embedding <=> [0.10, 0.20, 0.30, 0.40] AS cos_dist
FROM documents
WHERE source = 'wiki';

-- L2 距离（等价于 l2_distance）
SELECT embedding <-> [0.10, 0.20, 0.30, 0.40] AS l2_dist
FROM documents
WHERE source = 'wiki';

-- 负内积（等价于 -inner_product）
SELECT embedding <#> [0.10, 0.20, 0.30, 0.40] AS neg_inner_product
FROM documents
WHERE source = 'wiki';
```

### 运算符与函数对比

两种语法在功能和性能上完全等价，区别仅在于写法风格：

```sql
-- 函数风格
SELECT
    cosine_distance(embedding, [0.10, 0.20, 0.30, 0.40]) AS cos_dist,
    l2_distance(embedding, [0.10, 0.20, 0.30, 0.40])     AS l2_dist,
    inner_product(embedding, [0.10, 0.20, 0.30, 0.40])   AS dot_prod
FROM documents;

-- 运算符风格（完全等价）
SELECT
    embedding <=> [0.10, 0.20, 0.30, 0.40] AS cos_dist,
    embedding <-> [0.10, 0.20, 0.30, 0.40] AS l2_dist,
    -(embedding <#> [0.10, 0.20, 0.30, 0.40]) AS dot_prod
FROM documents;
```

两种风格可以在同一查询中混合使用，SonnetDB 的解析器会统一处理。

### 从 pgvector 迁移到 SonnetDB

如果你正在使用 PostgreSQL + pgvector，迁移到 SonnetDB 只需要做少量修改：

1. 将 `CREATE TABLE` 改为 `CREATE MEASUREMENT`
2. 将 `vector(n)` 类型改为 `FIELD VECTOR(n)`
3. 运算符语法保持不变

```sql
-- PostgreSQL + pgvector
CREATE TABLE documents (
    id SERIAL PRIMARY KEY,
    title TEXT,
    embedding vector(4)
);
SELECT * FROM documents ORDER BY embedding <=> '[0.1, 0.2, 0.3, 0.4]' LIMIT 3;

-- SonnetDB（运算符语法完全兼容）
CREATE MEASUREMENT documents (
    title     FIELD STRING,
    embedding FIELD VECTOR(4)
);
SELECT * FROM knn(documents, embedding, [0.10, 0.20, 0.30, 0.40], 3);
```

运算符向量语法的兼容性使得从 pgvector 迁移到 SonnetDB 的学习成本大大降低。无论你习惯使用函数风格还是运算符风格，SonnetDB 都能提供一致的向量距离计算能力。
