---
name: troubleshoot-slow-query
description: 排查 SonnetDB 慢查询的标准流程：拿计划、看 segment 命中、确认 tag/时间过滤。
triggers:
  - 慢查询
  - slow query
  - 性能
  - explain
  - timeout
  - 优化
requires_tools:
  - query_sql
  - list_measurements
  - describe_measurement
---

# 排查慢查询

当用户说“查询很慢 / 超时 / CPU 飙高 / 一条 SQL 跑不动”时使用本技能。

## 流程

1. 用 `query_sql` 拿到原始 SQL，复制一份当前条件。
2. 检查 **时间过滤**：是否带 `WHERE time >= ... AND time < ...`？没有就先加。
3. 检查 **tag 等值**：把高基数 tag（如 host, device_id）放在 `WHERE` 中等值过滤，避免全表扫描。
4. `describe_measurement` 看一下 schema：是不是把高基数维度建成 `field` 而不是 `tag`？
5. 看返回行数：如果几十万行说明缺聚合，加 `GROUP BY time(...)`。
6. 客户端计时与服务端 `?explain=true` 对比，定位是网络、解码还是执行慢。

## 经验法则

| 现象 | 定位 |
| ---- | ---- |
| WHERE 没有 time 过滤 | 全 segment 扫描 |
| 大量 `LIKE '%xx%'` 在 string field | 改成 tag 或预聚合 |
| 返回 > 10w 行 | 缺聚合或 `LIMIT` |
| segment 数 > 100 | 触发了过多 flush，调大 MemTable |

## 进一步

- 启用 `metrics` HTTP endpoint，看 `query_latency_p99`。
- `docs_search query="performance"` 拉相关章节。
