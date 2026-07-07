## 从 pgvector 迁移到 SonnetDB：运算符兼容性与 SQL 差异

pgvector 是 PostgreSQL 生态中最流行的向量扩展。SonnetDB 在设计上充分考虑 pgvector 用户的迁移体验，在运算符层面做了完整兼容。本文将详细介绍两者差异以及迁移要点。

### 运算符兼容

SonnetDB 完全兼容 pgvector 的三个核心距离运算符。内部通过 SQL 解析层将其映射到 `VectorDistance.Compute()` 方法：

| pgvector 运算符 | SonnetDB 运算符 | KnnMetric | 内部实现 |
|----------------|----------------|-----------|---------|
| `<->` | `<->` | L2 | `ComputeL2(a, b)` |
| `<=>` | `<=>` | Cosine | `ComputeCosine(a, b)` |
| `<#>` | `<#>` | InnerProduct | `ComputeNegativeInnerProduct(a, b)` |

```sql
-- pgvector 写法
SELECT id, embedding <=> '[0.1, 0.2, 0.3]' AS distance
FROM items ORDER BY distance LIMIT 5;

-- SonnetDB 写法 —— 完全一致
SELECT id, embedding <=> '[0.1, 0.2, 0.3]' AS distance
FROM items ORDER BY distance LIMIT 5;
```

### 类型定义差异

pgvector 通过扩展 `CREATE EXTENSION vector` 引入 `vector(n)` 类型。SonnetDB 则将 VECTOR 作为内建类型：

```sql
-- pgvector
CREATE EXTENSION vector;
CREATE TABLE items (id SERIAL, embedding vector(384));

-- SonnetDB
CREATE MEASUREMENT items (
    content TAG,
    embedding FIELD VECTOR(384)
);
```

SonnetDB 使用 measurement（时序表）模型，包含隐式的 `time` 时间戳列和 TAG 标签体系，这与关系型表的语义有所不同。

### knn() 表值函数——SonnetDB 的独特优势

pgvector 只能通过 `ORDER BY + LIMIT` 做向量搜索，而 SonnetDB 提供了更简洁的 `knn()` 表值函数，自动返回 `distance` 列，且可配合 `WHERE` 条件过滤：

```sql
-- pgvector 风格
SELECT id, content FROM items
ORDER BY embedding <=> '[0.1, 0.2, 0.3]' LIMIT 5;

-- SonnetDB knn() 风格
SELECT id, content, distance
FROM knn(items, embedding, [0.1, 0.2, 0.3], 5, 'cosine')
WHERE distance < 0.4;
```

`knn()` 会自动利用 HNSW 索引加速，无需修改查询语法。

### 索引差异

pgvector 需要手动指定索引类型和距离运算符（如 `vector_cosine_ops`）。SonnetDB 则自动为 VECTOR 列构建 HNSW 索引：

```sql
-- pgvector: 手动创建 IVFFlat 索引
CREATE INDEX ON items USING ivfflat (embedding vector_cosine_ops)
    WITH (lists = 100);

-- SonnetDB: 自动构建 HNSW 索引，零配置
-- 索引参数通过 DDL 中的 WITH 子句指定
CREATE MEASUREMENT items (
    content TAG,
    embedding FIELD VECTOR(384)
) WITH (vector_index = 'hnsw(m=16, ef=128)');
```

### INSERT 语法对比

```sql
-- pgvector 插入
INSERT INTO items (embedding) VALUES ('[0.1, 0.2, 0.3]'::vector);

-- SonnetDB 插入
INSERT INTO items (content, embedding, time)
VALUES ('hello', '[0.1, 0.2, 0.3]', 1776477601000);
```

### 迁移步骤

1. 从 PostgreSQL 将向量数据导出为 CSV
2. 在 SonnetDB 中创建 Measurement，将 `vector(n)` 替换为 `VECTOR(n)`
3. 使用 `BULK INSERT` 导入数据
4. 系统自动构建 HNSW 索引，无需手动触发
5. 验证 `knn()` 查询结果与 pgvector 一致

通过运算符兼容层，现有 pgvector 应用可以平滑迁移到 SonnetDB，同时获得自动 HNSW 索引、时序写入优化和内置 AI Copilot 等额外能力。
