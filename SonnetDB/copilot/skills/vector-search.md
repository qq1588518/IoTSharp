---
name: vector-search
description: SonnetDB 向量搜索完整指南：VECTOR(N) 列设计、KNN 查询语法、距离度量选择、嵌入生成、与 Copilot 知识库集成、性能调优。
triggers:
  - vector
  - 向量
  - knn
  - embedding
  - 嵌入
  - 相似搜索
  - 语义搜索
  - cosine
  - l2
  - euclidean
  - inner_product
  - VECTOR(N)
  - 向量索引
  - 知识库
  - RAG
requires_tools:
  - query_sql
  - describe_measurement
  - list_measurements
  - docs_search
---

# SonnetDB 向量搜索指南

SonnetDB 从 Milestone 13（PR #58~#62）起原生支持向量类型与 KNN 相似搜索，可直接在时序数据库中构建 RAG 知识库、语义搜索、推荐系统。

---

## 1. Schema 设计

### 声明向量列

```sql
CREATE MEASUREMENT documents (
    source     TAG,           -- 来源分类（wiki/manual/log）
    lang       TAG,           -- 语言标签
    title      FIELD STRING,  -- 文档标题
    chunk_id   FIELD INT,     -- 分块序号
    embedding  FIELD VECTOR(1536)  -- OpenAI text-embedding-3-small 维度
);
```

**常见嵌入维度：**

| 模型 | 维度 | 推荐度量 |
|------|------|----------|
| OpenAI text-embedding-3-small | 1536 | cosine |
| OpenAI text-embedding-3-large | 3072 | cosine |
| OpenAI text-embedding-ada-002 | 1536 | cosine |
| Ollama nomic-embed-text | 768 | cosine |
| Ollama mxbai-embed-large | 1024 | cosine |
| BGE-M3 | 1024 | cosine |
| Sentence-BERT | 384 | cosine |

**设计原则：**
- 向量列用 `FIELD VECTOR(N)`，N 必须与嵌入模型输出维度一致
- 用 TAG 列存储分类维度（source、lang、category），便于过滤后再 KNN
- 用 FIELD STRING 存储原始文本（title、content_preview），避免重查原始库
- `time` 列自动存在，可用于时间范围过滤（如只搜索近 30 天的文档）

---

## 2. 写入向量数据

### SQL INSERT

```sql
INSERT INTO documents (time, source, lang, title, chunk_id, embedding)
VALUES (
    1713676800000,
    'wiki',
    'zh',
    'SonnetDB 架构概述',
    0,
    [0.023, -0.145, 0.872, ...]   -- 1536 维浮点数组
);
```

### HTTP 批量写入（推荐大批量）

```bash
POST /v1/db/{db}/measurements/documents/json
Content-Type: application/json
Authorization: Bearer <token>

[
  {
    "time": 1713676800000,
    "tags": {"source": "wiki", "lang": "zh"},
    "fields": {
      "title": "SonnetDB 架构概述",
      "chunk_id": 0,
      "embedding": [0.023, -0.145, 0.872, ...]
    }
  }
]
```

### ADO.NET（C#）

```csharp
using var cmd = new SndbCommand(connection);
cmd.CommandText = @"
    INSERT INTO documents (time, source, lang, title, chunk_id, embedding)
    VALUES (@time, @source, @lang, @title, @chunk_id, @embedding)";
cmd.Parameters.AddWithValue("@time", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
cmd.Parameters.AddWithValue("@source", "wiki");
cmd.Parameters.AddWithValue("@lang", "zh");
cmd.Parameters.AddWithValue("@title", "SonnetDB 架构概述");
cmd.Parameters.AddWithValue("@chunk_id", 0);
cmd.Parameters.AddWithValue("@embedding", new float[] { 0.023f, -0.145f, 0.872f, ... });
cmd.ExecuteNonQuery();
```

---

## 3. KNN 查询语法

### 基础语法

```sql
SELECT * FROM knn(measurement, column, query_vector, k [, metric])
[WHERE tag_condition [AND time_condition]]
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `measurement` | 标识符 | measurement 名称 |
| `column` | 标识符 | VECTOR 类型的 FIELD 列名 |
| `query_vector` | `[f, ...]` | 查询向量字面量 |
| `k` | 整数 | 返回最近邻数量上限 |
| `metric` | 字符串 | 距离度量，默认 `'cosine'` |

### 示例

```sql
-- 最基础：找最相似的 5 篇文档
SELECT * FROM knn(documents, embedding, [0.023, -0.145, 0.872, ...], 5);

-- 指定余弦距离（语义搜索推荐）
SELECT * FROM knn(documents, embedding, [0.023, -0.145, 0.872, ...], 10, 'cosine');

-- 指定欧氏距离
SELECT * FROM knn(documents, embedding, [0.023, -0.145, 0.872, ...], 10, 'l2');

-- 带 tag 过滤（只搜中文文档）
SELECT * FROM knn(documents, embedding, [0.023, -0.145, 0.872, ...], 5)
WHERE lang = 'zh';

-- 带时间范围过滤（只搜近 30 天）
SELECT * FROM knn(documents, embedding, [0.023, -0.145, 0.872, ...], 5)
WHERE time >= now() - 30d;

-- 组合过滤
SELECT * FROM knn(documents, embedding, [0.023, -0.145, 0.872, ...], 10, 'cosine')
WHERE source = 'wiki'
  AND lang = 'zh'
  AND time >= now() - 90d;
```

### 返回列

| 列名 | 说明 |
|------|------|
| `time` | 数据点时间戳（Unix 毫秒） |
| `distance` | 与查询向量的距离值 |
| `<tag列>` | 所有 TAG 列 |
| `<field列>` | 所有 FIELD 列（向量列以 `float[]` 返回） |

---

## 4. 距离度量选择

| 度量 | 参数值 | 值域 | 适用场景 |
|------|--------|------|----------|
| 余弦距离 | `'cosine'`（默认） | [0, 2] | 语义相似度、NLP、文档检索 |
| 欧氏距离 | `'l2'` / `'euclidean'` | [0, ∞) | 图像特征、几何距离 |
| 负内积 | `'inner_product'` / `'dot'` / `'ip'` | (-∞, ∞) | 已归一化向量、推荐系统 |

**选择建议：**
- 文本语义搜索 → `'cosine'`（对向量长度不敏感）
- 图像/音频特征 → `'l2'`（绝对距离有意义）
- 已做 L2 归一化的向量 → `'inner_product'`（等价于 cosine，更快）

**距离值解读（cosine）：**
- `distance ≈ 0.0`：几乎完全相同
- `distance ≈ 0.1~0.3`：高度相关
- `distance ≈ 0.5~1.0`：中等相关
- `distance > 1.5`：基本不相关

---

## 5. 与 Copilot 知识库集成

SonnetDB Copilot 的知识库本身就存储在 `__copilot__` 数据库的向量 measurement 中：

```sql
-- 查看知识库 measurement
SHOW MEASUREMENTS;  -- 在 __copilot__ 数据库中

-- 手动搜索知识库（调试用）
SELECT * FROM knn(docs, embedding, [查询向量], 5, 'cosine')
WHERE time >= now() - 365d;
```

**RAG 流程（在应用中）：**
1. 用户输入问题 → 调用嵌入模型生成查询向量
2. `knn(documents, embedding, query_vec, 10)` 检索相关文档
3. 将检索结果拼入 LLM prompt → 生成回答

**C# RAG 示例：**
```csharp
// 1. 生成查询向量
float[] queryVec = await embeddingModel.EmbedAsync(userQuestion);

// 2. KNN 检索
string vecLiteral = "[" + string.Join(",", queryVec) + "]";
string sql = $"SELECT title, content FROM knn(documents, embedding, {vecLiteral}, 10, 'cosine') WHERE source = 'manual'";
var results = await connection.QueryAsync(sql);

// 3. 构建 RAG prompt
string context = string.Join("\n\n", results.Select(r => r.content));
string prompt = $"基于以下文档回答问题：\n{context}\n\n问题：{userQuestion}";
```

---

## 6. 性能与限制

### 当前实现（Milestone 13）

| 特性 | 状态 |
|------|------|
| 搜索算法 | Brute-force 顺序扫描 |
| 并行化 | `Parallel.ForEach` 段级并行 |
| 时间窗剪枝 | WHERE time 条件可跳过不相关 segment |
| HNSW 索引 | 可选 sidecar `.SDBVIDX` 文件（PR #61） |
| SIMD 加速 | TensorPrimitives（后续规划） |

### 性能调优建议

```sql
-- ✅ 好：用 tag 过滤缩小候选集
SELECT * FROM knn(documents, embedding, [...], 10)
WHERE source = 'manual'          -- 先过滤 tag
  AND time >= now() - 30d;       -- 再限制时间范围

-- ❌ 差：全库扫描
SELECT * FROM knn(documents, embedding, [...], 100);

-- ✅ 合理的 k 值：通常 5~20，避免 k > 100
SELECT * FROM knn(documents, embedding, [...], 10);
```

### 当前限制

| 限制 | 说明 |
|------|------|
| 仅支持 `SELECT *` | 不支持具名列投影（后续版本支持） |
| k 为整数字面量 | 不支持参数绑定 `@k`（后续版本支持） |
| 每个 measurement 一个向量列 | 多向量列需拆分 measurement |
| 向量维度固定 | 同一 measurement 内所有向量维度必须相同 |

---

## 7. 常见问题

**Q: KNN 查询返回 distance 为 NaN？**  
A: 查询向量全为 0 时 cosine 距离无意义。确保嵌入模型返回非零向量。

**Q: 为什么 KNN 结果数量少于 k？**  
A: measurement 中实际数据行数少于 k，或 WHERE 过滤后候选集不足 k 行，属正常现象。

**Q: 向量维度不匹配报错？**  
A: 查询向量维度必须与 `FIELD VECTOR(N)` 的 N 完全一致。用 `DESCRIBE MEASUREMENT` 确认维度。

**Q: 如何批量更新向量（如重新嵌入）？**  
A: SonnetDB 无 UPDATE，需先 DELETE 旧数据再 INSERT 新数据，或用新 time 戳写入新版本并在查询时限制时间范围。
