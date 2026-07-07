---
name: schema-design
description: SonnetDB measurement 的 tag/field 选择、命名约定与高基数风险规避。
triggers:
  - schema
  - 建表
  - tag
  - field
  - 命名
  - cardinality
  - 高基数
requires_tools:
  - describe_measurement
  - list_measurements
---

# 建模 / Schema 设计指南

适用：用户问“这个字段做 tag 还是 field”、“怎么建表”、“担心高基数”。

## 关键决策

| 列 | 类型 | 何时用 |
| ---- | ---- | ---- |
| **tag** | string | 维度，用于 `WHERE` 等值过滤、`GROUP BY` |
| **field** | float / int / bool / string | 度量值或低过滤需求的元数据 |
| **vector** | VECTOR(N) | 嵌入向量（PR #61 起） |

## 推荐做法

1. 维度（host, region, sensor_id）→ tag。
2. 数值采样（temperature, qps, latency_ms）→ field（float/int）。
3. 偶尔聚合的字符串（status, error_code）：基数 < 10000 → tag；否则 field。
4. 命名：snake_case，避免大写、空格、保留字。
5. 时间列固定为 `time`，毫秒精度 `Int64`。

## 高基数红线

- 单 measurement 唯一 tag 组合数 > 1,000,000 → 建议拆 measurement，按业务前缀。
- 把 trace_id / request_id 当 tag 是高基数地雷，请改成 field。
- 想加新 tag 时，先 `query_sql SELECT count(distinct(...))` 做评估。

## 检查清单

- [ ] 是否每个 measurement 都有时间过滤索引？
- [ ] tag 数 ≤ 8（推荐）
- [ ] 没有 NULLable tag（用空字符串占位）
- [ ] 写入前 batch 已按 tag 排序（提升压缩率）
