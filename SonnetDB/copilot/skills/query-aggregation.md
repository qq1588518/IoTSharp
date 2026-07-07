---
name: query-aggregation
description: 编写时间窗口聚合查询（GROUP BY time(...)）的最佳实践与常见反模式。
triggers:
  - aggregation
  - group by
  - 聚合
  - downsample
  - sum / avg / max / count
requires_tools:
  - query_sql
  - describe_measurement
---

# 编写聚合查询

当用户要求“按 1 分钟统计平均温度”、“按小时聚合”、“downsample”、“group by time” 时，使用本技能。

## 核心模式

```sql
SELECT
  avg(temperature) AS avg_temp,
  max(temperature) AS max_temp,
  count(*) AS samples
FROM cpu
WHERE host = 'server-01'
  AND time >= now() - 1h
GROUP BY time(1m);
```

## 步骤

1. 调用 `describe_measurement` 确认列名与数据类型。
2. 用 `query_sql` 先做一次小窗口的 sample 查询（例如 `LIMIT 5`）确认结果结构。
3. 在 SELECT 中显式给聚合列起别名（`AS avg_temp`），让客户端字段名稳定。
4. 始终带 `WHERE time >= ...` 做时间裁剪；对裸 `SELECT *` 务必在 `query_sql` 上加 `maxRows`。
5. `GROUP BY time(...)` 的桶大小要参考查询区间：>30 天用 1h；>1 天用 5m；最近 1 小时用 5s/10s。
6. 当前版本只返回聚合列，不会自动带出 bucket 起始时间列。

## 反模式

- 不带时间过滤的全表聚合（容易扫描整个 segment）。
- 期望 `GROUP BY host`、`GROUP BY device_id` 这类按 tag 聚合：当前版本只支持 `GROUP BY time(...)`。
- 在客户端用 `SUM` 替代数据库聚合：会跨网络传送原始点。
