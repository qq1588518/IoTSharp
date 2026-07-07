---
name: influxdb-migration
description: InfluxDB 迁移到 SonnetDB 的完整指南：概念映射（Measurement/Tag/Field/Retention Policy）、Line Protocol 转换、Flux/InfluxQL 查询翻译对照表、迁移步骤。
triggers:
  - influxdb
  - influx
  - 迁移
  - migration
  - line protocol
  - flux
  - influxql
  - retention policy
  - bucket
  - organization
  - 从 influx
  - 转换
  - 兼容
  - 对比
requires_tools:
  - query_sql
  - list_measurements
  - describe_measurement
---

# InfluxDB → SonnetDB 迁移指南

SonnetDB 与 InfluxDB 共享核心概念（Measurement / Tag / Field），迁移成本低。本指南提供完整的概念映射和查询翻译对照。

---

## 1. 核心概念映射

| InfluxDB 概念 | SonnetDB 对应 | 说明 |
| --- | --- | --- |
| Measurement | Measurement | 完全对应，命名规范相同 |
| Tag | TAG 列 | 完全对应，字符串类型，用于过滤 |
| Field | FIELD 列 | 完全对应，支持 Float/Int/Bool/String |
| Timestamp | `time` 列 | InfluxDB 纳秒精度，SonnetDB 毫秒精度 |
| Retention Policy | 手动 DELETE | SonnetDB 无自动 RP，需定期执行 DELETE |
| Bucket（v2） | Database | 一个 Bucket ≈ 一个 Database |
| Organization（v2） | 服务器级别 | SonnetDB 无 Org 概念，用 Database 隔离 |
| Series | Series（内部概念） | `measurement + sorted(tags)` 相同 |
| Continuous Query | 应用层定时聚合 | SonnetDB 无 CQ，需外部调度 |
| Flux | SonnetDB SQL | 不同语言，见翻译对照表 |
| InfluxQL | SonnetDB SQL | 相似但有差异，见翻译对照表 |

---

## 2. Schema 转换

### InfluxDB Schema → SonnetDB CREATE MEASUREMENT

**InfluxDB（概念）：**
```
measurement: cpu
tags: host, region
fields: usage_idle (float), usage_user (float), usage_system (float)
```

**SonnetDB（SQL）：**
```sql
CREATE MEASUREMENT cpu (
    host          TAG,          -- InfluxDB tag → SonnetDB TAG
    region        TAG,          -- InfluxDB tag → SonnetDB TAG

    usage_idle    FIELD FLOAT,  -- InfluxDB field float → SonnetDB FIELD FLOAT
    usage_user    FIELD FLOAT,
    usage_system  FIELD FLOAT
    -- time 自动存在，无需声明
);
```

### 时间戳精度转换

```text
InfluxDB 默认纳秒精度：1713686400000000000
SonnetDB 毫秒精度：    1713686400000

转换公式：
  InfluxDB 纳秒 → SonnetDB 毫秒：timestamp_ns / 1_000_000
  InfluxDB 微秒 → SonnetDB 毫秒：timestamp_us / 1_000
  InfluxDB 秒   → SonnetDB 毫秒：timestamp_s  * 1_000
```

---

## 3. Line Protocol 转换

### InfluxDB Line Protocol 格式

```
<measurement>[,<tag_key>=<tag_value>...] <field_key>=<field_value>[,<field_key>=<field_value>...] [<timestamp>]
```

**示例：**
```
cpu,host=server-01,region=cn-hz usage_idle=28.5,usage_user=65.3 1713686400000000000
```

### 转换为 SonnetDB SQL

```sql
-- 单条转换
INSERT INTO cpu (time, host, region, usage_idle, usage_user)
VALUES (
    1713686400000,   -- 纳秒时间戳 / 1_000_000 → 毫秒
    'server-01',
    'cn-hz',
    28.5,
    65.3
);
```

### 批量 Line Protocol 导入

SonnetDB 支持直接接收 Line Protocol 格式：

```bash
# 通过 HTTP API 批量导入 Line Protocol
curl -X POST "http://127.0.0.1:5080/v1/db/metrics/measurements/cpu/lp" \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: text/plain" \
  --data-binary @data.lp

# data.lp 文件内容（SonnetDB 接受毫秒时间戳）：
# cpu,host=server-01,region=cn-hz usage_idle=28.5,usage_user=65.3 1713686400000
```

---

## 4. 查询语言翻译对照

### InfluxQL → SonnetDB SQL

| InfluxQL | SonnetDB SQL | 说明 |
| --- | --- | --- |
| `SHOW MEASUREMENTS` | `SHOW MEASUREMENTS` | 相同 |
| `SHOW FIELD KEYS FROM cpu` | `DESCRIBE MEASUREMENT cpu` | 类似 |
| `SHOW TAG KEYS FROM cpu` | `DESCRIBE MEASUREMENT cpu` | 类似 |
| `SHOW TAG VALUES FROM cpu WITH KEY = "host"` | `SELECT DISTINCT host FROM cpu LIMIT 100` | 近似 |
| `SELECT * FROM cpu WHERE time > now() - 1h` | `SELECT * FROM cpu WHERE time >= now() - 1h` | `>` 改 `>=` |
| `SELECT mean(usage_idle) FROM cpu WHERE time > now() - 1h GROUP BY time(5m)` | `SELECT avg(usage_idle) FROM cpu WHERE time >= now() - 1h GROUP BY time(5m)` | `mean` 改 `avg`，时间桶仍用 `GROUP BY time(...)` |
| `SELECT * FROM cpu WHERE host = 'server-01'` | `SELECT * FROM cpu WHERE host = 'server-01'` | 相同 |
| `DELETE FROM cpu WHERE time < now() - 30d` | `DELETE FROM cpu WHERE time < now() - 30d` | 相同 |
| `DROP MEASUREMENT cpu` | `DROP MEASUREMENT cpu` | 相同 |
| `CREATE DATABASE metrics` | `CREATE DATABASE metrics` | 相同（控制面 SQL） |

### Flux → SonnetDB SQL

| Flux | SonnetDB SQL |
| --- | --- |
| `from(bucket:"metrics") \|> range(start:-1h) \|> filter(fn:(r)=>r._measurement=="cpu")` | `SELECT * FROM cpu WHERE time >= now() - 1h` |
| `\|> filter(fn:(r)=>r.host=="server-01")` | `AND host = 'server-01'` |
| `\|> aggregateWindow(every:5m, fn:mean)` | `SELECT avg(<field>) ... GROUP BY time(5m)` |
| `\|> mean()` | `avg(<field>)` |
| `\|> max()` | `max(<field>)` |
| `\|> min()` | `min(<field>)` |
| `\|> count()` | `count(*)` |
| `\|> first()` | `first(<field>)` |
| `\|> last()` | `last(<field>)` |
| `\|> limit(n:100)` | `LIMIT 100` |
| `\|> sort(columns:["_time"])` | `ORDER BY time ASC` |

---

## 5. Retention Policy 迁移

InfluxDB 的 Retention Policy 在 SonnetDB 中需要改为定期 DELETE：

**InfluxDB RP（自动）：**
```sql
-- InfluxDB：创建 30 天保留策略（自动过期）
CREATE RETENTION POLICY "30d" ON "metrics" DURATION 30d REPLICATION 1
```

**SonnetDB（手动定期执行）：**
```sql
-- SonnetDB：定期执行 DELETE（需外部调度，如 cron）
DELETE FROM cpu WHERE time < now() - 30d;
DELETE FROM memory WHERE time < now() - 30d;
-- 建议按 measurement 分批执行，避免单次操作过大
```

---

## 6. 迁移步骤

### 步骤 1：Schema 迁移

```bash
# 1. 从 InfluxDB 导出 schema 信息
influx schema list --bucket metrics

# 2. 在 SonnetDB 中创建对应的 measurement
# 参考上方 Schema 转换示例
```

### 步骤 2：历史数据迁移

```bash
# 方案 A：通过 Line Protocol 文件迁移
# 1. 从 InfluxDB 导出 Line Protocol
influx query 'from(bucket:"metrics") |> range(start:2024-01-01)' \
  --raw > data.lp

# 2. 时间戳转换（纳秒 → 毫秒）
# 用 Python/awk 处理 data.lp 中的时间戳

# 3. 导入到 SonnetDB
curl -X POST "http://127.0.0.1:5080/v1/db/metrics/measurements/cpu/lp" \
  -H "Authorization: Bearer <token>" \
  --data-binary @data_ms.lp
```

```bash
# 方案 B：通过 JSON 批量导入
# 1. 从 InfluxDB 查询数据，转换为 JSON 格式
# 2. 导入到 SonnetDB
curl -X POST "http://127.0.0.1:5080/v1/db/metrics/measurements/cpu/json" \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  --data-binary @data.json
```

### 步骤 3：查询层迁移

```text
1. 将 InfluxQL / Flux 查询翻译为 SonnetDB SQL（参考上方对照表）
2. 重点检查：
   - 时间过滤：> now() - 1h → >= now() - 1h
   - 聚合函数：mean(...) → avg(...)
   - 时间桶：仍然使用 GROUP BY time(5m)
   - 时间戳精度：确认应用层使用毫秒而非纳秒
3. 更新连接字符串：
   InfluxDB: http://localhost:8086
   SonnetDB: sonnetdb+http://localhost:5080/<database>
```

### 步骤 4：验证

```sql
-- 验证数据量是否一致
SELECT count(*) FROM cpu WHERE time >= now() - 30d;

-- 验证时间范围
SELECT time FROM cpu ORDER BY time ASC LIMIT 1;   -- 最早数据
SELECT time FROM cpu ORDER BY time DESC LIMIT 1;  -- 最新数据

-- 验证关键指标的统计值
SELECT avg(usage_idle), max(usage_idle), min(usage_idle)
FROM cpu WHERE time >= now() - 7d;
```

---

## 7. 不支持的 InfluxDB 特性

| InfluxDB 特性 | SonnetDB 状态 | 替代方案 |
| --- | --- | --- |
| 自动 Retention Policy | ❌ 不支持 | 定期 DELETE |
| Continuous Query | ❌ 不支持 | 外部调度（cron/定时任务） |
| Subscription | ❌ 不支持 | SSE 事件流（/v1/events） |
| Kapacitor 集成 | ❌ 不支持 | 应用层 + anomaly()/changepoint() |
| Flux 语言 | ❌ 不支持 | SonnetDB SQL |
| 多副本/集群 | ❌ 不支持（当前版本） | 单节点 + 备份策略 |
| 纳秒精度时间戳 | ❌ 不支持 | 毫秒精度（精度损失 1ms 以内） |
