---
name: sql-commenting
description: 为用户编写 SonnetDB SQL 时的注释规范：注释语法、何时必须加注释、各类 SQL 语句的注释模板（CREATE MEASUREMENT / INSERT / SELECT / DELETE / 控制面）。Copilot 生成任何 SQL 时都应遵循本规范。
triggers:
  - 注释
  - comment
  - 解释 sql
  - sql 说明
  - 加注释
  - 写注释
  - 代码注释
  - 文档注释
  - 帮我写 sql
  - 生成 sql
  - 给我一段 sql
  - 怎么写
  - 帮我建表
  - 帮我查询
requires_tools:
  - query_sql
  - describe_measurement
  - list_measurements
---

# SQL 注释规范

Copilot 为用户生成任何 SonnetDB SQL 时，**必须**按本规范添加注释，让 SQL 自解释，降低误操作风险。

---

## 1. 注释语法

SonnetDB 支持两种标准 SQL 注释语法：

```sql
-- 单行注释：从双横线到行尾
/* 块注释：可跨多行 */
```

**使用原则：**

| 场景 | 推荐语法 |
|------|----------|
| 列定义说明、行尾补充 | `--` 单行注释 |
| SQL 文件头部说明块 | `/* */` 块注释 |
| 临时屏蔽某段 SQL | `/* */` 块注释 |
| 多行逻辑说明 | `/* */` 块注释 |

---

## 2. 必须加注释的场景

以下情况 **不加注释视为不完整输出**：

| 场景 | 原因 |
|------|------|
| `CREATE MEASUREMENT` 每一列 | 说明列的业务含义、单位、取值范围 |
| `DELETE` 语句 | 明确删除范围，防止误操作 |
| `DROP` 语句 | 说明操作目的和不可逆风险 |
| 时间戳字面量（如 `1713676800000`） | 标注对应的人类可读时间 |
| `now() - Xd/h/m` 表达式 | 标注实际含义（"最近7天"） |
| `GRANT` / `REVOKE` 控制面语句 | 说明授权对象和权限范围 |
| 复杂聚合查询 | 说明每个聚合列的业务含义 |
| `knn(...)` 向量查询 | 说明查询向量来源和距离度量含义 |

---

## 3. 各类 SQL 注释模板

### 3.1 CREATE MEASUREMENT

```sql
/*
 * Measurement: host_resource
 * 用途：记录服务器的 CPU 和内存实时采样数据
 * 写入频率：每 10 秒一次
 * 保留策略：原始数据保留 30 天，聚合数据保留 1 年
 * 创建时间：2026-04-23
 */
CREATE MEASUREMENT host_resource (
    host            TAG,        -- 主机名，如 "server-01"
    datacenter      TAG,        -- 数据中心，如 "cn-hz-1"
    env             TAG,        -- 环境：prod / staging / dev

    cpu_pct         FIELD FLOAT,  -- CPU 使用率（0~100%）
    mem_used_bytes  FIELD INT,    -- 已用内存（字节）
    mem_total_bytes FIELD INT,    -- 总内存（字节），通常固定值
    mem_pct         FIELD FLOAT,  -- 内存使用率（0~100%），= used/total*100
    disk_pct        FIELD FLOAT   -- 磁盘使用率（0~100%）
    -- time 列自动存在，无需声明，Unix 毫秒整数
);
```

### 3.2 INSERT

```sql
-- 写入一条 CPU 采样数据
-- time: 2024-04-21 08:00:00 UTC = 1713686400000 ms
INSERT INTO host_resource (
    time,           -- Unix 毫秒时间戳
    host,           -- 目标主机
    datacenter,     -- 所在数据中心
    env,            -- 运行环境
    cpu_pct,        -- 本次采样 CPU 使用率
    mem_used_bytes, -- 本次采样已用内存
    mem_total_bytes -- 总内存（固定）
)
VALUES (
    1713686400000,  -- 2024-04-21 08:00:00 UTC
    'server-01',
    'cn-hz-1',
    'prod',
    72.5,           -- CPU 使用率 72.5%
    12884901888,    -- 已用内存 12 GB
    17179869184     -- 总内存 16 GB
);
```

### 3.3 SELECT — 基础查询

```sql
-- 查询 server-01 最近 1 小时的 CPU 使用率
-- 按时间倒序，取最新 100 条原始采样
SELECT
    time,       -- 采样时间（Unix 毫秒）
    host,       -- 主机名
    cpu_pct     -- CPU 使用率（%）
FROM host_resource
WHERE host = 'server-01'          -- 只看 server-01
  AND time >= now() - 1h          -- 最近 1 小时
ORDER BY time DESC
LIMIT 100;                        -- 最多返回 100 条，避免大结果集
```

### 3.4 SELECT — 时间桶聚合

```sql
-- 统计 server-01 过去 24 小时的 CPU 使用情况，按 5 分钟聚合
-- 用于绘制趋势图，桶大小 5m 适合 24h 时间跨度
-- 当前版本不会自动返回 bucket 列，结果只包含聚合值
SELECT
    avg(cpu_pct)   AS avg_cpu,         -- 平均 CPU（趋势线）
    max(cpu_pct)   AS peak_cpu,        -- 峰值 CPU（告警参考）
    min(cpu_pct)   AS idle_cpu,        -- 最低 CPU（空闲基线）
    count(*)       AS sample_count     -- 采样点数（验证数据完整性）
FROM host_resource
WHERE host = 'server-01'               -- 过滤主机
  AND time >= now() - 24h              -- 最近 24 小时
GROUP BY time(5m);                     -- 按 5 分钟时间桶聚合
```

### 3.5 SELECT — 多 tag 对比

```sql
-- 对比 prod 环境所有主机的 CPU 峰值（最近 1 小时）
-- 注意：SonnetDB 不支持 GROUP BY tag，需分别查询各主机
-- 此查询返回每台主机各自的时间序列，由应用层聚合对比

-- server-01
SELECT time, host, cpu_pct FROM host_resource
WHERE host = 'server-01' AND env = 'prod' AND time >= now() - 1h;

-- server-02
SELECT time, host, cpu_pct FROM host_resource
WHERE host = 'server-02' AND env = 'prod' AND time >= now() - 1h;
```

### 3.6 DELETE

```sql
/*
 * ⚠️  危险操作：删除历史数据（不可逆）
 * 目的：清理 server-01 的 30 天前的原始采样数据（保留策略执行）
 * 影响范围：host=server-01，2024-03-01 之前的所有数据
 * 执行前请确认：
 *   1. 已用 SELECT count(*) 估算影响行数
 *   2. 已在业务低峰期执行
 *   3. 聚合数据（host_resource_1h）已单独保留
 */
DELETE FROM host_resource
WHERE host = 'server-01'
  AND time < 1709251200000;  -- 2024-03-01 00:00:00 UTC，30天前
```

### 3.7 DROP MEASUREMENT

```sql
/*
 * ⚠️  极危险操作：删除整个 Measurement（完全不可逆）
 * 目的：清理已废弃的旧版 measurement（已迁移到 host_resource）
 * 影响：删除 server_metrics 的所有历史数据和 schema 定义
 * 审批：已获得 DBA 审批，工单号 #2024-042
 */
DROP MEASUREMENT server_metrics;
```

### 3.8 向量 KNN 查询

```sql
/*
 * 语义搜索：在文档知识库中查找与用户问题最相关的 5 篇文档
 * 查询向量：由 nomic-embed-text 模型对用户问题生成（1536 维）
 * 距离度量：cosine（余弦距离），值越小越相关，< 0.3 为高度相关
 * 过滤条件：只搜索中文文档（lang='zh'），最近 90 天内摄入的
 */
SELECT *
FROM knn(
    embedding_document,   -- 目标 measurement
    embedding,            -- VECTOR(1536) 列
    [0.023, -0.145, ...], -- 查询向量（由嵌入模型生成）
    5,                    -- 返回最相似的 5 条
    'cosine'              -- 余弦距离（语义搜索推荐）
)
WHERE lang = 'zh'             -- 只搜中文文档
  AND time >= now() - 90d;   -- 只搜近 90 天摄入的文档
-- 返回列：time, distance（越小越相关）, source, lang, title, ...
```

### 3.9 控制面 SQL

```sql
-- ========================================
-- 控制面操作：走 POST /v1/sql，需要 admin token
-- ========================================

-- 创建只读用户 alice，用于监控系统接入
CREATE USER alice WITH PASSWORD 'Monitor@2026';

-- 授予 alice 对 metrics 数据库的只读权限
-- 注意：只读权限不能执行 INSERT / DELETE
GRANT READ ON DATABASE metrics TO alice;

-- 为 alice 颁发访问 token（只显示一次，立即保存）
ISSUE TOKEN FOR alice;

-- 验证授权结果
SHOW GRANTS FOR alice;
```

---

## 4. 注释质量标准

### 好注释 vs 坏注释

```sql
-- ❌ 坏注释：重复代码本身，没有增加信息
SELECT time, cpu_pct FROM host_resource; -- 从 host_resource 查询 time 和 cpu_pct

-- ✅ 好注释：解释"为什么"和"业务含义"
-- 获取最近原始采样点，用于实时大屏展示（刷新间隔 5s）
SELECT time, cpu_pct FROM host_resource
WHERE host = 'server-01'
  AND time >= now() - 5m;  -- 只取最近 5 分钟，避免大屏加载过多历史数据
```

```sql
-- ❌ 坏注释：时间戳没有说明
WHERE time >= 1709251200000

-- ✅ 好注释：时间戳标注人类可读时间
WHERE time >= 1709251200000  -- 2024-03-01 00:00:00 UTC
```

```sql
-- ❌ 坏注释：聚合列没有说明用途
SELECT avg(cpu_pct), max(cpu_pct), count(*)

-- ✅ 好注释：每列说明业务用途
SELECT
    avg(cpu_pct)  AS avg_cpu,    -- 平均值，用于趋势线
    max(cpu_pct)  AS peak_cpu,   -- 峰值，用于告警判断
    count(*)      AS samples     -- 采样数，验证数据完整性
```

### 注释长度原则

| 语句类型 | 注释量 |
|----------|--------|
| `CREATE MEASUREMENT` | 文件头块注释 + 每列行尾注释（必须） |
| `DELETE` / `DROP` | 块注释说明目的、范围、风险（必须） |
| `SELECT` 聚合查询 | 每个聚合列行尾注释 + 查询目的说明（必须） |
| `SELECT` 简单查询 | 查询目的单行注释（推荐） |
| `INSERT` | 时间戳和关键值行尾注释（推荐） |
| `GRANT` / `REVOKE` | 说明授权对象和业务理由（必须） |

---

## 5. 多语句脚本注释结构

当一次性提供多条 SQL 时，使用分节注释组织结构：

```sql
-- ============================================================
-- SonnetDB 初始化脚本：IoT 传感器监控系统
-- 创建时间：2026-04-23
-- 作者：SonnetDB Copilot
-- 数据库：iot_prod
-- ============================================================


-- ------------------------------------------------------------
-- 第一步：创建 Measurement（Schema 定义）
-- ------------------------------------------------------------

/*
 * Measurement: sensor_climate
 * 用途：记录工厂温湿度传感器的实时采样数据
 * 写入频率：每 30 秒一次
 */
CREATE MEASUREMENT sensor_climate (
    sensor_id    TAG,          -- 传感器编号，如 "S-A01"
    workshop     TAG,          -- 所在车间，如 "车间A"
    temp_celsius FIELD FLOAT,  -- 温度（摄氏度，精度 0.1）
    humidity_pct FIELD FLOAT,  -- 相对湿度（0~100%）
    pressure_pa  FIELD FLOAT   -- 大气压（帕斯卡）
);


-- ------------------------------------------------------------
-- 第二步：写入测试数据（验证 Schema 正确性）
-- ------------------------------------------------------------

-- 写入一条测试数据，time 省略则使用当前 UTC 毫秒
INSERT INTO sensor_climate (sensor_id, workshop, temp_celsius, humidity_pct, pressure_pa)
VALUES ('S-A01', '车间A', 23.5, 65.2, 101325.0);  -- 标准大气压约 101325 Pa


-- ------------------------------------------------------------
-- 第三步：验证写入结果
-- ------------------------------------------------------------

-- 查询刚写入的数据（最近 1 分钟）
SELECT time, sensor_id, workshop, temp_celsius, humidity_pct
FROM sensor_climate
WHERE sensor_id = 'S-A01'
  AND time >= now() - 1m   -- 最近 1 分钟
ORDER BY time DESC
LIMIT 5;
```
