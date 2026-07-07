# Vector Search with SonnetDB

SonnetDB v1 为时序数据的向量列（`VECTOR(dim)`）提供 **brute-force KNN（K-最近邻）** 查询能力，
通过内置表值函数 `knn(...)` 以标准 SQL 语法完成端到端的嵌入向量检索。

---

## 目录

1. [Schema 设计](#schema-设计)
2. [写入向量数据](#写入向量数据)
3. [KNN 查询语法](#knn-查询语法)
4. [距离度量](#距离度量)
5. [返回结果结构](#返回结果结构)
6. [WHERE 子句过滤](#where-子句过滤)
7. [嵌入式 API 示例](#嵌入式-api-示例)
8. [限制与后续规划](#限制与后续规划)

---

## Schema 设计

在 `CREATE MEASUREMENT` 中声明 `FIELD VECTOR(dim)` 列，其中 `dim` 是向量维度（正整数）。

```sql
CREATE MEASUREMENT documents (
    source  TAG,
    title   FIELD STRING,
    embedding FIELD VECTOR(1536)
);
```

> 一个 Measurement 中可同时包含多个 `VECTOR` 列（各自独立存储）。

---

## 写入向量数据

使用标准 `INSERT INTO ... VALUES` 语句，向量字面量以 `[f1, f2, ..., fN]` 形式提供：

```sql
INSERT INTO documents (source, title, embedding, time) VALUES
    ('wiki', '量子计算简介', [0.12, -0.34, 0.57, ...], 1700000000000),
    ('wiki', '神经网络基础', [0.88, 0.23, -0.11, ...], 1700000001000),
    ('blog', '时序数据库选型', [0.03, 0.74, 0.16, ...], 1700000002000);
```

向量维度必须与 schema 中声明的 `dim` 精确匹配，否则写入时抛出错误。

---

## KNN 查询语法

```sql
SELECT *
FROM knn(measurement, column, query_vector, k [, metric])
[WHERE tag_condition [AND time_condition]]
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `measurement` | 标识符 | 目标 measurement 名称 |
| `column` | 标识符 | 用于检索的 `VECTOR` 类型 FIELD 列名 |
| `query_vector` | 向量字面量 `[f, ...]` | 与列声明维度相同的查询向量 |
| `k` | 正整数字面量 | 返回最近邻数量上限 |
| `metric` | 字符串字面量（可选） | 距离度量方式，默认 `'cosine'` |

### 示例

```sql
-- 默认余弦距离，最近 5 条
SELECT * FROM knn(documents, embedding, [0.12, -0.34, 0.57], 5);

-- L2（欧几里得）距离，最近 10 条
SELECT * FROM knn(documents, embedding, [0.12, -0.34, 0.57], 10, 'l2');

-- 带 tag 过滤：只在 source='wiki' 的序列中检索
SELECT * FROM knn(documents, embedding, [0.12, -0.34, 0.57], 5, 'cosine')
WHERE source = 'wiki';

-- 带时间范围过滤
SELECT * FROM knn(documents, embedding, [0.12, -0.34, 0.57], 5)
WHERE time >= 1700000000000 AND time < 1700000002000;
```

---

## 距离度量

| 参数值 | 含义 | 适用场景 |
|--------|------|---------|
| `'cosine'` / `'cosine_distance'` | 余弦距离 = 1 − 余弦相似度，值域 `[0, 2]` | 文本嵌入、归一化向量 |
| `'l2'` / `'l2_distance'` / `'euclidean'` | 欧几里得距离 = √Σ(aᵢ−bᵢ)² | 绝对距离度量 |
| `'inner_product'` / `'dot'` / `'ip'` | 负内积 = −(a·b)，越小内积越大 | 已归一化、希望最大化点积的场景 |

所有度量均满足「值越小 = 向量越相似」，结果按距离升序返回。

---

## 返回结果结构

`knn(...)` 返回如下固定列顺序：

| 列名 | 类型 | 说明 |
|------|------|------|
| `time` | `long`（毫秒） | 数据点时间戳 |
| `distance` | `double` | 与查询向量的距离 |
| `<tag1>`, `<tag2>`, ... | `string` | Measurement 中所有 TAG 列（按声明顺序） |
| `<field1>`, `<field2>`, ... | 各字段类型 | Measurement 中所有 FIELD 列（按声明顺序） |

> **注意**：向量 FIELD 列本身也会出现在结果中，以 `float[]` 形式返回。

---

## WHERE 子句过滤

`knn(...)` 的 `WHERE` 子句与普通 `SELECT` 完全一致，支持：

- **Tag 等值过滤**：`source = 'wiki'`
- **时间范围过滤**：`time >= T1 AND time < T2`（Unix 毫秒整数）

`WHERE` 同时影响「参与召回的序列」（tag 过滤）和「参与召回的时间窗」（time 过滤），
可有效缩减暴力扫描范围。

---

## 嵌入式 API 示例

以下示例展示如何通过 C# 嵌入式 API 完成「写入 → 检索」的完整流程：

```csharp
using SonnetDB.Engine;
using SonnetDB.Sql;
using SonnetDB.Sql.Execution;

// 打开数据库
using var db = Tsdb.Open(new TsdbOptions { RootDirectory = "/data/my-db" });

// 建表
SqlExecutor.Execute(db,
    "CREATE MEASUREMENT documents (source TAG, embedding FIELD VECTOR(4))");

// 写入向量
SqlExecutor.Execute(db,
    "INSERT INTO documents (source, embedding, time) VALUES " +
    "('news', [0.1, 0.2, 0.3, 0.4], 1700000000000), " +
    "('news', [0.9, 0.8, 0.7, 0.6], 1700000001000), " +
    "('blog', [0.5, 0.5, 0.5, 0.5], 1700000002000)");

// KNN 查询：查找余弦距离最近的 2 条
float[] queryEmbedding = [0.1f, 0.2f, 0.3f, 0.4f];
string queryVec = string.Join(", ", queryEmbedding);

var result = (SelectExecutionResult)SqlExecutor.Execute(db,
    $"SELECT * FROM knn(documents, embedding, [{queryVec}], 2)");

Console.WriteLine($"Columns: {string.Join(", ", result.Columns)}");
foreach (var row in result.Rows)
{
    long time = (long)row[0]!;
    double dist = (double)row[1]!;
    string source = (string)row[2]!;
    Console.WriteLine($"  time={time}, dist={dist:F4}, source={source}");
}
// 输出示例：
//   Columns: time, distance, source, embedding
//   time=1700000000000, dist=0.0000, source=news
//   time=1700000002000, dist=0.0093, source=blog
```

---

## 限制与后续规划

| 当前版本（PR #60）| 后续规划 |
|---|---|
| brute-force 顺扫（无 ANN 索引） | HNSW 段内索引（PR #6x） |
| 多序列并行扫描（`Parallel.ForEach`） | SIMD（`TensorPrimitives`）加速距离计算 |
| 仅支持 `SELECT *` 投影 | 支持具名列投影 |
| `k` 为正整数字面量 | 支持参数绑定 `@k` |
| 查询向量为字面量 `[...]` | 支持参数绑定 `@queryVec` |
