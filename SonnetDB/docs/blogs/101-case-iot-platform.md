## 案例：IoT 平台如何用 SonnetDB 管理百万设备实时数据

### 背景

某工业 IoT 平台服务商需要为其 SaaS 产品提供设备数据存储与分析能力。平台接入设备涵盖工厂传感器、楼宇控制器、园区摄像头等多种类型，峰值接入设备数超过 100 万台，每台设备每分钟上报 5-10 个指标。

### 挑战

- **写入吞吐**：高峰期需承载 50 万点/秒的持续写入，且写入延迟需控制在 10ms 以内
- **多租户隔离**：数百个企业客户的数据需要严格隔离，权限管理必须细粒度
- **查询多样性**：从实时仪表盘（秒级聚合）到历史分析（月级趋势）跨度巨大
- **边缘部署**：部分客户要求数据不出厂区，需要在工厂边缘网关上运行嵌入式版本

### 解决方案

该团队选择 SonnetDB 作为时序存储引擎，原因有三：嵌入式模式可以无缝运行在资源受限的边缘网关上；服务端模式满足云端集中部署；MIT 许可证无商业障碍。

**数据模型设计**：每个企业客户对应一个独立数据库（Database），每类设备对应一个 Measurement，设备 ID 作为 TAG，各传感器指标作为 FIELD。

```sql
-- 为工厂 A 客户创建数据库
CREATE DATABASE factory_a;

-- 设备数据表：支持地理位置与嵌入向量
CREATE MEASUREMENT device_metrics (
    device_id   TAG,
    device_type TAG,
    location    GEOPOINT,
    temperature FIELD FLOAT,
    humidity    FIELD FLOAT,
    vibration   FIELD FLOAT,
    status      FIELD INT
);
```

**批量写入**：平台 SDK 使用 Line Protocol 端点批量提交，单次请求携带 1000 条记录，配合 `?flush=async` 模式，最大化写入吞吐。

```bash
# 批量 Line Protocol 写入
POST /lp/factory_a?flush=async
Content-Type: text/plain

device_metrics,device_id=DEV001,device_type=sensor temperature=23.5,humidity=65.2 1714000000000
device_metrics,device_id=DEV002,device_type=sensor temperature=24.1,humidity=63.8 1714000000000
# ... 1000 条
```

**实时仪表盘查询**：

```sql
-- 过去 5 分钟各设备类型的平均温度
SELECT device_type,
       avg(temperature)   AS avg_temp,
       max(temperature)   AS peak_temp,
       count(*)           AS sample_count
FROM device_metrics
WHERE time > NOW() - INTERVAL '5m'
GROUP BY device_type;
```

**历史趋势与预测**：

```sql
-- 过去 30 天每日温度均值，并预测未来 7 天
SELECT *
FROM forecast(
    SELECT avg(temperature) AS temp
    FROM device_metrics
    WHERE device_id = 'DEV001'
      AND time > NOW() - INTERVAL '30d'
    GROUP BY time(1d),
    7,
    'holt_winters'
);
```

**异常告警**：当设备指标偏离正常范围时自动触发告警逻辑。

```sql
-- 检测振动异常（MAD 方法）
SELECT time, device_id, vibration,
       anomaly(vibration, 'mad', 3.5) AS is_anomaly
FROM device_metrics
WHERE device_type = 'motor'
  AND time > NOW() - INTERVAL '1h';
```

**地理围栏**：对于有位置信息的移动设备，在 SQL 中直接完成地理过滤。

```sql
-- 查找 5km 内所有在线设备
SELECT device_id, lat(location) AS lat, lon(location) AS lng,
       last(status) AS current_status
FROM device_metrics
WHERE ST_DWithin(location, POINT(39.9042, 116.4074), 5000)
  AND time > NOW() - INTERVAL '5m'
GROUP BY device_id;
```

**多租户权限**：为每个企业客户创建只读 Token，确保数据隔离。

```sql
-- 为工厂 A 客户颁发只读 Token
ISSUE TOKEN 'client_factory_a' FOR DATABASE factory_a WITH ROLE readonly EXPIRE IN 365d;
```

### 边缘嵌入式部署

对于不允许数据出厂的客户，SonnetDB 以嵌入式模式运行在工厂边缘服务器上：

```csharp
// 边缘网关程序
using var db = Tsdb.Open("./edge_data");
var executor = db.GetExecutor();

// 直接写入，无网络开销
await executor.ExecuteAsync(
    "INSERT INTO device_metrics (time, device_id, temperature) VALUES (@t, @id, @v)",
    new { t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), id = "DEV001", v = 23.5 }
);
```

边缘侧定期将数据同步到云端，形成"边云协同"架构。

### 实施效果

| 指标 | 上线前 | 上线后 |
| --- | --- | --- |
| 写入峰值 | 10 万点/秒 | 80 万点/秒 |
| P99 写入延迟 | 120ms | 8ms |
| 历史查询（1 个月） | 45s | 1.2s |
| 部署包大小 | 512MB | 38MB（AOT） |
| 每月存储成本 | ¥18,000 | ¥4,200 |

SonnetDB 嵌入式优先的架构让同一套代码既能在边缘网关运行，又能在云端集群部署，大幅降低了平台团队的维护复杂度。
