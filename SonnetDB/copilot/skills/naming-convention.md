---
name: naming-convention
description: SonnetDB measurement 名、tag 名、field 名的命名规范与中英文翻译指南。当用户描述业务场景、用中文说"我想建一张表存XX数据"或给出字段描述时，将其翻译为符合规范的 snake_case 英文名称，并给出完整的 CREATE MEASUREMENT 语句。
triggers:
  - 命名
  - 起名
  - 翻译
  - 建表
  - 表名
  - 字段名
  - measurement 名
  - 怎么命名
  - 叫什么名字
  - 中文转英文
  - snake_case
  - 前缀
  - 命名规范
  - 名称规则
  - 我想建
  - 我要建
  - 创建一个表
  - 存储XX数据
  - 命名建议
requires_tools:
  - list_measurements
  - describe_measurement
  - query_sql
---

# Measurement / Tag / Field 命名规范

当用户描述业务需求时，按本规范将中文或模糊描述翻译为合规的 SonnetDB 名称，并输出完整的 `CREATE MEASUREMENT` 语句。

---

## 1. 总体规则（必须遵守）

| 规则 | 说明 | 示例 |
|------|------|------|
| **全小写 snake_case** | 单词间用下划线，禁止大写、驼峰、连字符 | `cpu_usage` ✅ `cpuUsage` ❌ `CPU-Usage` ❌ |
| **纯 ASCII 英文** | 禁止中文、拼音缩写（除非极通用缩写） | `temperature` ✅ `wendu` ❌ `温度` ❌ |
| **不超过 64 字符** | 过长的名称截断或缩写 | `http_request_duration_ms` ✅ |
| **不以数字开头** | 数字开头会导致解析歧义 | `cpu_usage` ✅ `1st_value` ❌ |
| **不使用保留字** | 避免 `time`、`value`、`select`、`from`、`where` 等 SQL 关键字作为列名 | `metric_value` ✅ `value` ⚠️ |
| **不含空格或特殊字符** | 仅允许字母、数字、下划线 | `error_rate` ✅ `error rate` ❌ `error-rate` ❌ |

---

## 2. Measurement 名命名规范

### 格式

```
<领域>_<对象>_<粒度（可选）>
```

### 常用领域前缀

| 领域 | 前缀 | 示例 |
|------|------|------|
| 基础设施 / 主机 | `host_` / `node_` | `host_cpu`、`node_memory` |
| 网络 | `net_` | `net_interface`、`net_latency` |
| 应用服务 | `app_` / `svc_` | `app_request`、`svc_error` |
| 数据库 | `db_` | `db_query`、`db_connection` |
| 消息队列 | `mq_` | `mq_consumer`、`mq_topic` |
| IoT / 传感器 | `sensor_` / `device_` | `sensor_temperature`、`device_power` |
| 业务指标 | `biz_` | `biz_order`、`biz_payment` |
| 用户行为 | `user_` | `user_event`、`user_session` |
| 日志 / 事件 | `log_` / `event_` | `log_error`、`event_deploy` |
| 预测 / 分析 | `forecast_` / `analytics_` | `forecast_sales`、`analytics_funnel` |
| 向量 / AI | `embedding_` / `vector_` | `embedding_document`、`vector_image` |
| 系统内部 | `sys_` | `sys_gc`、`sys_thread` |

### 粒度后缀（可选）

| 粒度 | 后缀 | 说明 |
|------|------|------|
| 原始采样 | （无后缀） | `host_cpu` |
| 分钟聚合 | `_1m` | `host_cpu_1m` |
| 小时聚合 | `_1h` | `host_cpu_1h` |
| 天聚合 | `_1d` | `host_cpu_1d` |

### 翻译示例

| 用户描述 | 推荐 Measurement 名 |
|----------|---------------------|
| 服务器 CPU 使用率 | `host_cpu` |
| 网络接口流量 | `net_interface` |
| HTTP 请求日志 | `app_request` |
| 温湿度传感器数据 | `sensor_climate` |
| 订单支付记录 | `biz_payment` |
| 用户登录事件 | `user_login_event` |
| 数据库慢查询 | `db_slow_query` |
| 商品库存变化 | `biz_inventory` |
| 车辆 GPS 轨迹 | `device_gps` |
| 文档嵌入向量 | `embedding_document` |
| 工厂设备振动 | `sensor_vibration` |
| API 错误统计 | `svc_error` |
| 内存使用情况 | `host_memory` |
| 消息队列消费延迟 | `mq_consumer_lag` |
| 销售额预测 | `forecast_revenue` |

---

## 3. Tag 名命名规范

Tag 是维度列，用于 WHERE 过滤和 series 身份标识。

### 格式

```
<维度概念>（简洁，通常 1~2 个单词）
```

### 常用 Tag 名参考

| 业务概念 | 推荐 Tag 名 | 禁止写法 |
|----------|-------------|----------|
| 主机名 / 服务器 | `host` | `hostname`（可接受但略长）、`主机` |
| 地区 / 区域 | `region` | `area`（模糊）、`地区` |
| 可用区 | `zone` | `az`（可接受缩写） |
| 环境 | `env` | `environment`（过长）、`环境` |
| 服务名 | `service` | `svc`（可接受）、`服务` |
| 实例 ID | `instance` | `inst`（可接受）、`实例` |
| 集群 | `cluster` | `clus`（不推荐）、`集群` |
| 数据中心 | `datacenter` | `dc`（可接受）、`数据中心` |
| 应用名 | `app` | `application`（过长）、`应用` |
| 版本 | `version` | `ver`（可接受）、`版本` |
| 设备 ID | `device_id` | `deviceId`（驼峰）、`设备` |
| 传感器类型 | `sensor_type` | `type`（过于通用）、`类型` |
| 状态 | `status` | `state`（可接受）、`状态` |
| 协议 | `protocol` | `proto`（可接受）、`协议` |
| 方法 | `method` | `meth`（不推荐）、`方法` |
| 路径 | `path` | `url`（语义不同）、`路径` |
| 错误码 | `error_code` | `errCode`（驼峰）、`错误码` |
| 操作系统 | `os` | `operating_system`（过长）、`系统` |
| 数据库名 | `database` | `db`（可接受）、`数据库` |
| 表名 | `table_name` | `table`（保留字风险）、`表` |
| 用户 ID | `user_id` | `userId`（驼峰）、`用户` |
| 租户 | `tenant` | `org`（可接受）、`租户` |
| 产品 | `product` | `prod`（可接受）、`产品` |
| 类别 | `category` | `cat`（可接受）、`类别` |

### Tag 命名禁区

```
❌ trace_id / request_id / session_id   → 高基数，改为 FIELD STRING
❌ timestamp / created_at / updated_at  → 时间信息用 time 列
❌ value / data / info                  → 过于通用，无意义
❌ id（单独使用）                        → 通常高基数，用 device_id / user_id 等
```

---

## 4. Field 名命名规范

Field 是度量列，存储实际数值或低过滤频率的字符串。

### 格式

```
<指标名>_<单位（可选）>
```

### 单位后缀约定

| 单位 | 后缀 | 示例 |
|------|------|------|
| 毫秒 | `_ms` | `latency_ms`、`duration_ms` |
| 秒 | `_sec` / `_s` | `uptime_sec` |
| 字节 | `_bytes` | `memory_bytes`、`disk_bytes` |
| 千字节 | `_kb` | `transfer_kb` |
| 兆字节 | `_mb` | `heap_mb` |
| 百分比（0~100） | `_pct` | `cpu_pct`、`disk_pct` |
| 比率（0~1） | `_ratio` | `error_ratio`、`cache_hit_ratio` |
| 每秒 | `_per_sec` / `_ps` | `requests_per_sec`、`bytes_ps` |
| 次数 / 计数 | `_count` / `_total` | `error_count`、`request_total` |
| 温度（摄氏） | `_celsius` / `_c` | `temp_celsius` |
| 电压（伏） | `_volt` | `voltage_volt` |
| 安培 | `_amp` | `current_amp` |
| 瓦特 | `_watt` | `power_watt` |
| 转速 | `_rpm` | `fan_rpm` |
| 帕斯卡 | `_pa` | `pressure_pa` |

### 常用 Field 名参考

| 业务概念 | 推荐 Field 名 | 类型 |
|----------|---------------|------|
| CPU 使用率（%） | `usage_pct` | FLOAT |
| 内存使用（字节） | `used_bytes` | INT |
| 内存总量（字节） | `total_bytes` | INT |
| 请求延迟（毫秒） | `latency_ms` | FLOAT |
| 请求数 | `request_count` | INT |
| 错误数 | `error_count` | INT |
| 吞吐量（字节/秒） | `throughput_bytes_ps` | FLOAT |
| 温度（摄氏） | `temp_celsius` | FLOAT |
| 湿度（%） | `humidity_pct` | FLOAT |
| 电压（伏） | `voltage_volt` | FLOAT |
| 功率（瓦） | `power_watt` | FLOAT |
| 队列深度 | `queue_depth` | INT |
| 连接数 | `connection_count` | INT |
| 成功标志 | `is_success` | BOOL |
| 告警标志 | `is_alert` | BOOL |
| 错误信息 | `error_message` | STRING |
| 版本号 | `version_str` | STRING |
| 订单金额（分） | `amount_cents` | INT |
| 评分 | `score` | FLOAT |
| 预测值 | `predicted_value` | FLOAT |
| 置信区间下限 | `lower_bound` | FLOAT |
| 置信区间上限 | `upper_bound` | FLOAT |

---

## 5. 中文业务场景 → 完整建表语句翻译

### 翻译流程

```
1. 识别"对象"（什么东西的数据）→ Measurement 名
2. 识别"维度"（按什么分组/过滤）→ TAG 列
3. 识别"度量"（记录什么数值）→ FIELD 列
4. 判断是否需要向量列 → FIELD VECTOR(N)
5. 输出完整 CREATE MEASUREMENT 语句
```

### 翻译示例集

**示例 1：服务器监控**
> 用户：我想建一张表，存服务器的 CPU 和内存数据，按机器名和机房区分

```sql
CREATE MEASUREMENT host_resource (
    host       TAG,           -- 机器名
    datacenter TAG,           -- 机房
    cpu_pct    FIELD FLOAT,   -- CPU 使用率（0~100）
    mem_used_bytes  FIELD INT,     -- 已用内存（字节）
    mem_total_bytes FIELD INT,     -- 总内存（字节）
    mem_pct    FIELD FLOAT    -- 内存使用率（0~100）
);
```

**示例 2：IoT 温湿度传感器**
> 用户：工厂里有很多传感器，要记录温度、湿度、气压，传感器有编号和所在车间

```sql
CREATE MEASUREMENT sensor_climate (
    sensor_id  TAG,              -- 传感器编号
    workshop   TAG,              -- 车间名称
    temp_celsius    FIELD FLOAT, -- 温度（摄氏）
    humidity_pct    FIELD FLOAT, -- 湿度（0~100%）
    pressure_pa     FIELD FLOAT  -- 气压（帕斯卡）
);
```

**示例 3：HTTP 接口监控**
> 用户：我要记录每个 API 接口的请求量、错误数、响应时间，按服务和接口路径区分

```sql
CREATE MEASUREMENT svc_request (
    service        TAG,           -- 服务名
    method         TAG,           -- HTTP 方法（GET/POST）
    path           TAG,           -- 接口路径
    env            TAG,           -- 环境（prod/staging）
    request_count  FIELD INT,     -- 请求总数
    error_count    FIELD INT,     -- 错误数
    latency_p50_ms FIELD FLOAT,   -- P50 延迟（毫秒）
    latency_p99_ms FIELD FLOAT,   -- P99 延迟（毫秒）
    error_ratio    FIELD FLOAT    -- 错误率（0~1）
);
```

**示例 4：电商订单业务**
> 用户：记录每分钟的订单数、支付金额、退款数，按商品类目和渠道区分

```sql
CREATE MEASUREMENT biz_order (
    category       TAG,           -- 商品类目
    channel        TAG,           -- 销售渠道（app/web/mini）
    order_count    FIELD INT,     -- 订单数
    paid_count     FIELD INT,     -- 支付成功数
    refund_count   FIELD INT,     -- 退款数
    gmv_cents      FIELD INT,     -- 成交金额（分，避免浮点精度问题）
    refund_cents   FIELD INT      -- 退款金额（分）
);
```

**示例 5：用户行为事件**
> 用户：记录用户点击、浏览、购买等行为，需要知道是哪个用户、哪个页面、什么设备

```sql
CREATE MEASUREMENT user_event (
    event_type     TAG,           -- 事件类型（click/view/purchase）
    page           TAG,           -- 页面名称
    device_type    TAG,           -- 设备类型（ios/android/web）
    -- user_id 不做 TAG（高基数），改为 FIELD
    user_id        FIELD STRING,  -- 用户 ID（高基数，不做 tag）
    session_id     FIELD STRING,  -- 会话 ID（高基数，不做 tag）
    duration_ms    FIELD INT,     -- 停留时长（毫秒）
    is_logged_in   FIELD BOOL     -- 是否已登录
);
```

**示例 6：文档知识库（向量）**
> 用户：我要存文档的嵌入向量，用来做语义搜索，文档有来源和语言标签

```sql
CREATE MEASUREMENT embedding_document (
    source         TAG,              -- 文档来源（wiki/manual/faq）
    lang           TAG,              -- 语言（zh/en）
    title          FIELD STRING,     -- 文档标题
    content_preview FIELD STRING,    -- 内容摘要（前200字）
    chunk_index    FIELD INT,        -- 分块序号
    embedding      FIELD VECTOR(1536) -- 嵌入向量（text-embedding-3-small）
);
```

**示例 7：设备 GPS 轨迹**
> 用户：记录车辆的 GPS 位置，有车牌号、车型，要存经纬度和速度

```sql
CREATE MEASUREMENT device_gps (
    vehicle_id     TAG,           -- 车辆 ID / 车牌号
    vehicle_type   TAG,           -- 车型（truck/van/car）
    longitude      FIELD FLOAT,   -- 经度
    latitude       FIELD FLOAT,   -- 纬度
    speed_kmh      FIELD FLOAT,   -- 速度（千米/小时）
    heading_deg    FIELD FLOAT,   -- 航向角（0~360度）
    altitude_m     FIELD FLOAT    -- 海拔（米）
);
```

---

## 6. 命名冲突与保留字

### SonnetDB 保留字（不能用作列名）

```
time, select, from, where, and, or, not, insert, into, values,
create, drop, delete, show, describe, measurement, tag, field,
group, by, order, limit, offset, having, as, null, true, false,
count, sum, avg, min, max, first, last, now, knn
```

**冲突处理：**
```sql
-- ❌ 冲突：value 是保留字
CREATE MEASUREMENT sensor (value FIELD FLOAT);

-- ✅ 加前缀或后缀区分
CREATE MEASUREMENT sensor (sensor_value FIELD FLOAT);
CREATE MEASUREMENT sensor (reading FIELD FLOAT);
```

### 与已有 Measurement 名冲突

```sql
-- 创建前先检查
SHOW MEASUREMENTS;

-- 如果已存在同名 measurement，先确认 schema 是否兼容
DESCRIBE MEASUREMENT host_cpu;
```

---

## 7. 快速命名检查清单

在输出 `CREATE MEASUREMENT` 之前，逐项确认：

```
□ Measurement 名：小写 snake_case，有领域前缀，无保留字
□ Tag 列：维度概念，基数 < 100万，无 ID 类高基数字段
□ Field 列：度量值，有单位后缀（_ms/_bytes/_pct/_count 等）
□ 金额类字段：用整数（分/厘）而非浮点，避免精度问题
□ 布尔标志：以 is_ 或 has_ 开头（is_success / has_error）
□ 高基数字段（user_id/trace_id）：放 FIELD STRING，不做 TAG
□ 时间列：不声明（自动存在），不要创建 created_at/timestamp 列
□ 向量列：维度与嵌入模型匹配（1536/768/1024 等）
```
