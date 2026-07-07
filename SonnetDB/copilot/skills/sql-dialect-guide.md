---
name: sql-dialect-guide
description: SonnetDB SQL 方言完整指南：正确语法、常见方言污染纠正、不支持特性列表。当用户写 SQL 或问"怎么写 XX 查询"时触发。
triggers:
  - sql
  - 语法
  - 方言
  - create measurement
  - create table
  - insert
  - select
  - group by time
  - date_trunc
  - join
  - 子查询
  - update
  - 怎么写
  - 如何查询
  - sql 错误
  - syntax error
requires_tools:
  - query_sql
  - describe_measurement
  - list_measurements
---

# SonnetDB SQL 方言指南

SonnetDB 是时序数据库，SQL 方言基于标准 SQL 裁剪并扩展，**严禁混入 MySQL / PostgreSQL / InfluxQL / SQLite 方言**。

---

## 1. 数据定义（DDL）

### 创建 Measurement（≠ CREATE TABLE）

```sql
CREATE MEASUREMENT cpu (
    host    TAG,
    region  TAG STRING,
    usage   FIELD FLOAT,
    count   FIELD INT,
    ok      FIELD BOOL,
    label   FIELD STRING,
    vec     FIELD VECTOR(1536)
);
```

| 列类型 | 关键字 | 说明 |
|--------|--------|------|
| 维度列 | `TAG` 或 `TAG STRING` | 用于 WHERE/GROUP BY，字符串类型，不可 NULL |
| 度量列 | `FIELD FLOAT/INT/BOOL/STRING` | 实际数值 |
| 向量列 | `FIELD VECTOR(N)` | N 维浮点向量，用于 KNN |
| 时间列 | 自动存在，**不声明** | `time`，Unix 毫秒整数 |

**❌ 错误写法（方言污染）：**
```sql
-- 错误：MySQL/PostgreSQL 方言
CREATE TABLE cpu (id INT PRIMARY KEY, ts TIMESTAMP, usage FLOAT);
-- 错误：InfluxQL 概念
CREATE RETENTION POLICY ...
```

### 删除 Measurement

```sql
DROP MEASUREMENT cpu;
```

### 查看 Schema

```sql
SHOW MEASUREMENTS;          -- 列出所有 measurement
SHOW TABLES;                -- 同上（别名）
DESCRIBE MEASUREMENT cpu;   -- 查看列定义
DESC cpu;                   -- 简写
```

---

## 2. 数据写入（DML）

### INSERT

```sql
-- 完整写法（time 为 Unix 毫秒）
INSERT INTO cpu (time, host, region, usage, count, ok, label)
VALUES (1713676800000, 'server-01', 'cn-hz', 0.71, 10, TRUE, 'ok');

-- 省略 time（自动用当前 UTC 毫秒）
INSERT INTO cpu (host, usage) VALUES ('server-01', 0.85);

-- 写入向量
INSERT INTO documents (source, title, embedding)
VALUES ('wiki', 'SonnetDB 简介', [0.1, 0.2, 0.3, ...]);
```

**❌ 不支持：**
```sql
UPDATE cpu SET usage = 0.9 WHERE host = 'server-01';  -- 无 UPDATE
INSERT OR REPLACE INTO ...;                            -- SQLite 方言
UPSERT INTO ...;                                       -- 不支持
```

### DELETE（需要时间范围）

```sql
DELETE FROM cpu
WHERE host = 'server-01'
  AND time >= 1713676800000
  AND time <= 1713763200000;
```

---

## 3. 数据查询（DQL）

### 基础 SELECT

```sql
SELECT time, host, usage
FROM cpu
WHERE host = 'server-01'
  AND time >= now() - 1h
ORDER BY time DESC
LIMIT 100;
```

### 时间函数

| 函数 | 说明 |
|------|------|
| `now()` | 当前 UTC 时间（毫秒） |
| `now() - 1h` | 1小时前（支持 s/m/h/d） |
| `now() - 7d` | 7天前 |

**❌ 不支持：**
```sql
WHERE time > '2024-01-01'          -- 不支持字符串时间
WHERE time > CURRENT_TIMESTAMP     -- PostgreSQL 方言
WHERE ts > ...                     -- 列名必须是 time
```

### 聚合查询

```sql
SELECT
    avg(usage)   AS avg_usage,
    max(usage)   AS peak,
    count(*)     AS samples
FROM cpu
WHERE host = 'server-01'
  AND time >= now() - 1h
GROUP BY time(1m);
```

**支持的聚合函数：** `count` / `sum` / `min` / `max` / `avg` / `first` / `last`

当前版本的 `GROUP BY time(...)` 结果只返回聚合列，不会自动带出 bucket 起始时间列。

**时间桶写法：**
```sql
GROUP BY time(1m)
GROUP BY time(30s)
GROUP BY time(1h)
GROUP BY time(1000ms)
```

**❌ 不支持：**
```sql
GROUP BY date_trunc('minute', time)   -- PostgreSQL 方言
GROUP BY host                          -- 不支持按 tag GROUP BY
SELECT host, avg(usage) FROM cpu GROUP BY host  -- 错误
SELECT time_bucket(time, '1m') AS bucket, avg(usage) FROM cpu GROUP BY bucket
```

### 分页

```sql
-- 方式一
SELECT * FROM cpu LIMIT 10 OFFSET 20;

-- 方式二（SQL Server 风格）
SELECT * FROM cpu
ORDER BY time
OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY;
```

### 向量 KNN 查询

```sql
-- 基础 KNN
SELECT * FROM knn(documents, embedding, [0.1, 0.2, ...], 5);

-- 带距离度量
SELECT * FROM knn(documents, embedding, [0.1, 0.2, ...], 10, 'cosine');

-- 带 tag 过滤
SELECT * FROM knn(documents, embedding, [0.1, 0.2, ...], 5)
WHERE source = 'wiki'
  AND time >= now() - 30d;
```

| 距离度量 | 参数值 | 适用场景 |
|----------|--------|----------|
| 余弦距离 | `'cosine'`（默认） | 语义相似度、NLP |
| 欧氏距离 | `'l2'` 或 `'euclidean'` | 图像、几何 |
| 负内积 | `'inner_product'`/`'dot'`/`'ip'` | 已归一化向量 |

---

## 4. WHERE 子句支持范围

| 条件类型 | 支持状态 |
|----------|----------|
| tag 等值：`host = 'server-01'` | ✅ 稳定 |
| time 范围：`time >= X AND time <= Y` | ✅ 稳定 |
| 多条件 AND | ✅ 稳定 |
| tag 不等式：`host != 'x'` | ⚠️ 不建议生产使用 |
| OR 条件 | ⚠️ 不建议生产使用 |
| field 条件过滤：`usage > 0.8` | ⚠️ 不建议生产使用 |
| LIKE | ⚠️ 避免 `LIKE '%xx%'`（全扫描） |

---

## 5. 完全不支持的特性

```
❌ JOIN（任何类型）
❌ 子查询（SELECT 内嵌 SELECT）
❌ CTE（WITH ... AS）
❌ 窗口函数 OVER (PARTITION BY ...)
❌ UPDATE / UPSERT
❌ CREATE INDEX / DROP INDEX
❌ UNION / INTERSECT / EXCEPT
❌ 事务（BEGIN / COMMIT / ROLLBACK）
❌ 按 tag 列 GROUP BY
❌ 字符串时间戳（必须用 Unix 毫秒整数或 now() 表达式）
```

---

## 6. 控制面 SQL（仅 admin，走 /v1/sql）

```sql
-- 数据库管理
CREATE DATABASE metrics;
DROP DATABASE metrics;
SHOW DATABASES;

-- 用户管理
CREATE USER alice WITH PASSWORD 'pa$$';
CREATE USER admin2 WITH PASSWORD 'secret' SUPERUSER;
ALTER USER alice WITH PASSWORD 'new-password';
DROP USER alice;

-- 授权管理
GRANT READ ON DATABASE metrics TO alice;
GRANT WRITE ON DATABASE metrics TO alice;
GRANT ADMIN ON DATABASE * TO admin2;
REVOKE ON DATABASE metrics FROM alice;

-- 查看状态
SHOW USERS;
SHOW GRANTS;
SHOW GRANTS FOR alice;
SHOW TOKENS;
ISSUE TOKEN FOR alice;
REVOKE TOKEN 'tok_abcdef';
```

> ⚠️ 控制面 SQL 走 `POST /v1/sql`，数据面 SQL 走 `POST /v1/db/{db}/sql`，两者端点不同。

---

## 7. 快速纠错对照表

| 用户写的（错误） | 正确的 SonnetDB 写法 |
|-----------------|---------------------|
| `CREATE TABLE cpu (...)` | `CREATE MEASUREMENT cpu (...)` |
| `ts TIMESTAMP` | `time`（自动，不声明） |
| `GROUP BY date_trunc('minute', time)` | `GROUP BY time(1m)` |
| `WHERE time > '2024-01-01'` | `WHERE time >= 1704067200000` |
| `UPDATE cpu SET usage = 0.9 WHERE ...` | 不支持，需重新 INSERT |
| `SELECT host, avg(usage) GROUP BY host` | 不支持按 tag GROUP BY |
| `JOIN measurements ON ...` | 不支持 JOIN |
| `SELECT * FROM cpu INNER JOIN ...` | 不支持 JOIN |

---

## 8. 注释语法

SonnetDB 支持标准 SQL 的两种注释语法，**Copilot 生成任何 SQL 时必须添加注释**：

```sql
-- 单行注释：解释当前行或下方语句的用途

/* 块注释：用于语句头部说明、
   多行解释、或临时屏蔽代码 */
```

**最低注释要求：**

```sql
-- ✅ CREATE MEASUREMENT：每列必须有行尾注释说明业务含义和单位
CREATE MEASUREMENT host_cpu (
    host     TAG,         -- 主机名，如 "server-01"
    env      TAG,         -- 环境：prod / staging / dev
    cpu_pct  FIELD FLOAT  -- CPU 使用率（0~100%）
    -- time 列自动存在，无需声明
);

-- ✅ 时间戳字面量：必须标注人类可读时间
WHERE time >= 1713686400000  -- 2024-04-21 08:00:00 UTC

-- ✅ DELETE / DROP：必须用块注释说明目的和风险
/* ⚠️ 不可逆操作：删除 server-01 的 30 天前数据（保留策略执行） */
DELETE FROM host_cpu WHERE host = 'server-01' AND time < 1709251200000;

-- ✅ 聚合列：每列说明业务用途
SELECT
    avg(cpu_pct) AS avg_cpu,   -- 平均值，用于趋势线
    max(cpu_pct) AS peak_cpu   -- 峰值，用于告警判断
```

> 详细注释模板和规范见技能 **`sql-commenting`**。
