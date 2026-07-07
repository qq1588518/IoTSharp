## HTTP API 参考：SonnetDB 全部 REST 端点详解

SonnetDB 提供了完整的 RESTful HTTP API，分为数据面（Data-Plane）、控制面（Control-Plane）、MCP 接口和可观测性四大类。本文将逐一介绍各端点及其用法，帮助您通过 HTTP 协议与 SonnetDB 交互。

### 数据面 API（Data-Plane）

数据面 API 负责数据的写入和查询，是所有 API 中最常使用的部分。

```bash
# SQL 查询端点：执行任意 SQL 语句
POST /api/v1/sql
Authorization: Bearer sndb_abc123
Content-Type: application/json

{
    "sql": "SELECT time, temperature FROM sensor_data WHERE device_id = 's01' LIMIT 10"
}

# 批量写入端点：高效摄入批量数据
POST /api/v1/write
Authorization: Bearer sndb_abc123
Content-Type: application/json

{
    "measurement": "sensor_data",
    "points": [
        {"time": 1713676800000, "tags": {"device_id": "s01"}, "fields": {"temperature": 23.5, "humidity": 65.2}},
        {"time": 1713676801000, "tags": {"device_id": "s01"}, "fields": {"temperature": 23.7, "humidity": 64.8}}
    ]
}

# Line Protocol 写入（InfluxDB 兼容格式）
POST /api/v1/ingest
Authorization: Bearer sndb_abc123
Content-Type: text/plain

sensor_data,device_id=s01 temperature=23.5,humidity=65.2 1713676800000
sensor_data,device_id=s01 temperature=23.7,humidity=64.8 1713676801000

# 批量查询端点
POST /api/v1/sql/batch
{
    "statements": [
        "SELECT count(*) FROM sensor_data",
        "SELECT * FROM sensor_data LIMIT 5"
    ]
}
```

### 控制面 API（Control-Plane）

控制面 API 用于管理数据库对象、用户权限和系统配置。

```bash
# 创建测量表
POST /api/v1/sql
{
    "sql": "CREATE MEASUREMENT sensor_data (device_id TAG, temperature FIELD DOUBLE)"
}

# 列出所有测量表
GET /api/v1/measurements

# 查看测量表结构
GET /api/v1/measurements/sensor_data/schema

# 创建 Token
POST /api/v1/tokens
{
    "name": "readonly-token",
    "permissions": ["read"],
    "database": "mydb"
}

# 列出所有 Token
GET /api/v1/tokens

# 删除 Token
DELETE /api/v1/tokens/{token_id}
```

### MCP API

MCP（Model Context Protocol）接口用于 AI 集成，支持大语言模型与数据库的自然交互。

```bash
# MCP 工具列表
GET /api/v1/mcp/tools

# 执行 MCP 工具
POST /api/v1/mcp/execute
{
    "tool": "query",
    "arguments": {
        "sql": "SELECT * FROM sensor_data LIMIT 5"
    }
}

# 获取 MCP 资源
GET /api/v1/mcp/resources/schema/sensor_data
```

### 可观测性 API（Observability）

可观测性端点提供数据库运行状态的监控指标，适合集成到 Prometheus 等监控系统中。

```bash
# 数据库运行状态
GET /api/v1/status

# Prometheus 指标
GET /api/v1/metrics

# 慢查询日志
GET /api/v1/querylog?slow=true&limit=20

# 数据段统计
GET /api/v1/segments

# 数据库概览
GET /api/v1/database/stats
{
    "measurement_count": 12,
    "total_points": 5872341,
    "disk_usage_mb": 245.3,
    "segment_count": 89,
    "uptime_seconds": 86400
}
```

### 错误处理与状态码

SonnetDB HTTP API 使用标准的 HTTP 状态码表示请求结果：

| 状态码 | 含义 | 说明 |
|--------|------|------|
| 200 | OK | 请求成功 |
| 204 | No Content | 写入成功，无返回数据 |
| 400 | Bad Request | 请求参数错误 |
| 401 | Unauthorized | Token 无效或缺失 |
| 403 | Forbidden | 权限不足 |
| 404 | Not Found | 资源不存在 |
| 429 | Too Many Requests | 请求频率超限 |
| 500 | Server Error | 服务端内部错误 |

错误响应体格式如下：

```json
{
    "error": {
        "code": "INVALID_SQL",
        "message": "语法错误：第1行第10列附近",
        "details": "期望关键字 FROM，实际得到 TOKEN"
    }
}
```

所有 API 端点均支持 `Content-Type: application/json` 和 `application/x-msgpack` 两种序列化格式，后者在批量数据传输时具有更高的性能和更小的体积。建议在带宽受限或高吞吐场景中使用 MessagePack 格式。
