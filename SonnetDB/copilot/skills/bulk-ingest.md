---
name: bulk-ingest
description: 大批量历史数据 / 回填导入的最佳实践（HTTP /v1/bulk + line protocol / parquet）。
triggers:
  - bulk
  - 批量导入
  - backfill
  - 回填
  - line protocol
  - parquet
  - 历史数据
requires_tools:
  - query_sql
  - list_measurements
---

# 批量导入与回填

适用：需要把几亿点历史数据导入 SonnetDB，或从 InfluxDB / Prometheus / CSV 回填。

## 推荐路径

1. **使用 `/v1/bulk/{db}/{measurement}` 接口**（PR #43），支持 line protocol 与 parquet。
2. 单批 5w～50w 行。Batch 太小 → 高 RTT 开销；太大 → MemTable flush 抖动。
3. **并发**：客户端并行 4～8 个 batch，按 tag 分桶避免乱序。
4. **时间顺序**：尽量保证 batch 内按 time 升序，否则会触发额外排序与 segment 拆分。
5. **回填历史数据时**：临时把 `MemTable` 大小调高（`SonnetDBServer:Storage:MemTableMaxPoints`），完成后改回。
6. **校验**：导入完成后 `SELECT count(*) FROM measurement WHERE time BETWEEN ...` 比较源与目标行数。

## 工具

- `sndb bulk-ingest` CLI（见 `cli-reference.md`）。
- 直接 `curl --data-binary @file.lp` 也可以。

## 反模式

- 单点 INSERT 导入大批量数据 → WAL 写满，吞吐很差。
- 不带 tag 的全表写 → 后续查询无法按维度裁剪，必须再 reorganise。
- 不打开 `--gzip` 上传大文件 → 网络是瓶颈。

## 监控

- `metrics` 中的 `bulk_ingest_throughput_pps`。
- `docs_search query="bulk ingest"` 获取详细 API 字段说明。
