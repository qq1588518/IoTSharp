---
name: observability
description: SonnetDB 可观测性指南：/healthz 健康检查、/metrics Prometheus 指标解读、慢查询监控、SSE 事件流、Copilot 指标含义。对应 Milestone 17（OTel）。
triggers:
  - healthz
  - metrics
  - prometheus
  - 监控
  - 可观测性
  - observability
  - 慢查询
  - 延迟
  - latency
  - p99
  - 吞吐量
  - throughput
  - 健康检查
  - health
  - sse
  - 事件流
  - otel
  - opentelemetry
  - 指标
  - dashboard
  - grafana
requires_tools:
  - query_sql
  - list_measurements
---

# 可观测性与运行时监控指南

SonnetDB 提供 `/healthz` 健康检查、`/metrics` Prometheus 指标端点和 `/v1/events` SSE 事件流，支持与 Prometheus + Grafana 集成。Milestone 17 将引入完整 OpenTelemetry 支持。

---

## 1. 健康检查（/healthz）

### 基础健康检查

```bash
GET http://127.0.0.1:5080/healthz
```

**正常响应（HTTP 200）：**
```json
{
  "status": "ok",
  "databases": ["metrics", "telemetry", "__copilot__"],
  "uptime": "2h34m12s"
}
```

**降级响应（HTTP 200，但有警告）：**
```json
{
  "status": "degraded",
  "databases": ["metrics", "telemetry"],
  "errors": ["__copilot__: catalog load failed"]
}
```

**不健康响应（HTTP 503）：**
```json
{
  "status": "unhealthy",
  "errors": ["storage: disk full"]
}
```

### 在 Kubernetes 中使用

```yaml
livenessProbe:
  httpGet:
    path: /healthz
    port: 5080
  initialDelaySeconds: 10
  periodSeconds: 30

readinessProbe:
  httpGet:
    path: /healthz
    port: 5080
  initialDelaySeconds: 5
  periodSeconds: 10
```

---

## 2. Prometheus 指标（/metrics）

```bash
GET http://127.0.0.1:5080/metrics
```

返回标准 Prometheus 文本格式。

### 写入指标

| 指标名 | 类型 | 说明 |
|--------|------|------|
| `sndb_write_points_total` | Counter | 累计写入数据点数 |
| `sndb_write_errors_total` | Counter | 累计写入错误数 |
| `sndb_memtable_points` | Gauge | 当前 MemTable 中的数据点数 |
| `sndb_flush_duration_seconds` | Histogram | Flush 耗时分布 |
| `sndb_flush_total` | Counter | 累计 Flush 次数 |
| `bulk_ingest_throughput_pps` | Gauge | 批量写入吞吐量（points/sec） |

### 查询指标

| 指标名 | 类型 | 说明 |
|--------|------|------|
| `sndb_query_duration_seconds` | Histogram | 查询耗时分布（含 p50/p95/p99） |
| `sndb_query_total` | Counter | 累计查询次数 |
| `sndb_query_errors_total` | Counter | 累计查询错误数 |
| `query_latency_p99` | Gauge | 查询 P99 延迟（毫秒） |
| `sndb_segment_scan_total` | Counter | 累计 Segment 扫描次数 |

### 存储指标

| 指标名 | 类型 | 说明 |
|--------|------|------|
| `sndb_segment_count` | Gauge | 当前 Segment 文件数量（按数据库） |
| `sndb_wal_size_bytes` | Gauge | WAL 文件总大小 |
| `sndb_storage_bytes` | Gauge | 数据库总存储大小 |
| `sndb_compaction_duration_seconds` | Histogram | Compaction 耗时 |
| `sndb_compaction_total` | Counter | 累计 Compaction 次数 |

### Copilot 指标

| 指标名 | 类型 | 说明 |
|--------|------|------|
| `copilot_requests_total` | Counter | Copilot 请求总数 |
| `copilot_tool_calls_total` | Counter | MCP 工具调用次数（按工具名） |
| `copilot_knowledge_chunks` | Gauge | 知识库文档分块数 |
| `copilot_skill_loads_total` | Counter | 技能加载次数（按技能名） |
| `copilot_llm_tokens_total` | Counter | LLM Token 消耗（输入/输出） |

### 认证指标

| 指标名 | 类型 | 说明 |
|--------|------|------|
| `sndb_auth_success_total` | Counter | 认证成功次数 |
| `sndb_auth_failure_total` | Counter | 认证失败次数（401/403） |
| `sndb_active_tokens` | Gauge | 当前活跃 Token 数量 |

---

## 3. 慢查询监控

### 通过 ?explain=true 分析单个查询

```bash
POST /v1/db/metrics/sql?explain=true
Authorization: Bearer <token>
Content-Type: application/json

{"sql": "SELECT avg(usage) FROM cpu WHERE host = 'server-01' AND time >= now() - 1h GROUP BY time(1m)"}
```

**响应示例：**
```json
{
  "plan": {
    "measurement": "cpu",
    "seriesCount": 1,
    "segmentsScanned": 3,
    "memtableRows": 1024,
    "timeRange": {"from": 1713673200000, "to": 1713676800000},
    "tagFilters": [{"host": "server-01"}]
  },
  "timing": {
    "parseMs": 0.5,
    "planMs": 1.2,
    "executeMs": 45.3,
    "totalMs": 47.0
  }
}
```

**关键指标解读：**

| 字段 | 正常值 | 警告阈值 | 说明 |
|------|--------|----------|------|
| `segmentsScanned` | < 20 | > 100 | Segment 过多，考虑 Compaction |
| `executeMs` | < 100ms | > 1000ms | 执行时间过长 |
| `seriesCount` | < 1000 | > 10000 | Series 数量过多，高基数问题 |

### 通过 Prometheus 监控慢查询

```promql
# P99 查询延迟
histogram_quantile(0.99, rate(sndb_query_duration_seconds_bucket[5m]))

# 查询错误率
rate(sndb_query_errors_total[5m]) / rate(sndb_query_total[5m])

# 写入吞吐量
rate(sndb_write_points_total[1m])

# MemTable 积压
sndb_memtable_points > 100000
```

### 通过 SonnetDB 自身查询监控数据

如果 SonnetDB 将自身指标写入数据库（Milestone 17 后）：

```sql
-- 查看最近 1 小时的 P99 查询延迟
SELECT max(value) AS p99_ms
FROM sndb_metrics
WHERE metric = 'query_latency_p99'
  AND time >= now() - 1h
GROUP BY time(1m);

-- 查看写入吞吐量趋势
SELECT avg(value) AS pps
FROM sndb_metrics
WHERE metric = 'bulk_ingest_throughput_pps'
  AND time >= now() - 24h
GROUP BY time(5m);
```

---

## 4. SSE 事件流（/v1/events）

SSE（Server-Sent Events）提供实时事件推送，用于监控写入、查询、系统事件。

```bash
GET http://127.0.0.1:5080/v1/events
Authorization: Bearer <token>
Accept: text/event-stream
```

**事件类型：**

| 事件类型 | 触发时机 | 数据格式 |
|----------|----------|----------|
| `flush` | MemTable flush 完成 | `{"db": "metrics", "rows": 50000, "durationMs": 120}` |
| `compaction` | Segment compaction 完成 | `{"db": "metrics", "segmentsBefore": 15, "segmentsAfter": 3}` |
| `slow_query` | 查询超过阈值（默认 1s） | `{"sql": "...", "durationMs": 1234, "db": "metrics"}` |
| `auth_failure` | 认证失败 | `{"ip": "...", "reason": "invalid_token"}` |
| `db_created` | 数据库创建 | `{"db": "new_db"}` |
| `db_dropped` | 数据库删除 | `{"db": "old_db"}` |

**JavaScript 客户端示例：**
```javascript
const es = new EventSource('/v1/events', {
  headers: { 'Authorization': 'Bearer tok_xxx' }
});

es.addEventListener('slow_query', (e) => {
  const data = JSON.parse(e.data);
  console.warn(`慢查询 ${data.durationMs}ms: ${data.sql}`);
});

es.addEventListener('flush', (e) => {
  const data = JSON.parse(e.data);
  console.log(`Flush 完成: ${data.db}, ${data.rows} 行, ${data.durationMs}ms`);
});
```

---

## 5. Grafana Dashboard 配置

### Prometheus 数据源配置

```yaml
# prometheus.yml
scrape_configs:
  - job_name: 'sonnetdb'
    static_configs:
      - targets: ['127.0.0.1:5080']
    metrics_path: '/metrics'
    scrape_interval: 15s
```

### 推荐 Dashboard 面板

**写入监控：**
```promql
# 写入速率（points/sec）
rate(sndb_write_points_total[1m])

# Flush 频率
rate(sndb_flush_total[5m])

# MemTable 水位
sndb_memtable_points
```

**查询监控：**
```promql
# 查询 P99 延迟
histogram_quantile(0.99, rate(sndb_query_duration_seconds_bucket[5m]))

# 查询 QPS
rate(sndb_query_total[1m])

# 错误率
rate(sndb_query_errors_total[5m])
```

**存储监控：**
```promql
# 存储总大小
sndb_storage_bytes

# Segment 数量（按数据库）
sndb_segment_count

# WAL 大小
sndb_wal_size_bytes
```

---

## 6. 常见监控告警规则

```yaml
# alerting_rules.yml
groups:
  - name: sonnetdb
    rules:
      - alert: SonnetDBHighQueryLatency
        expr: histogram_quantile(0.99, rate(sndb_query_duration_seconds_bucket[5m])) > 1
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "SonnetDB P99 查询延迟超过 1 秒"

      - alert: SonnetDBHighSegmentCount
        expr: sndb_segment_count > 100
        for: 10m
        labels:
          severity: warning
        annotations:
          summary: "Segment 数量过多，可能影响查询性能"

      - alert: SonnetDBHighMemTable
        expr: sndb_memtable_points > 500000
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "MemTable 积压过多，flush 可能滞后"

      - alert: SonnetDBDown
        expr: up{job="sonnetdb"} == 0
        for: 1m
        labels:
          severity: critical
        annotations:
          summary: "SonnetDB 服务不可用"
```

---

## 7. OpenTelemetry（Milestone 17）

Milestone 17（PR #89~#98）将引入完整 OTel 支持：

| 功能 | 状态 |
|------|------|
| OTel Metrics 导出 | 📋 规划中 |
| OTel Traces（查询链路追踪） | 📋 规划中 |
| OTel Logs | 📋 规划中 |
| OTLP 推送到 Collector | 📋 规划中 |

**预期配置（Milestone 17 后）：**
```json
{
  "Observability": {
    "OtlpEndpoint": "http://otel-collector:4317",
    "ServiceName": "sonnetdb",
    "MetricsInterval": 15
  }
}
```
