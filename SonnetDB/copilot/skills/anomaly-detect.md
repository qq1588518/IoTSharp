---
name: anomaly-detect
description: 使用 SonnetDB 内置 anomaly() 窗口函数检测时序异常点：zscore / mad / iqr 三种算法的选择时机、阈值调参、多指标联合检测、告警集成模式。
triggers:
  - anomaly
  - 异常
  - 异常检测
  - 离群点
  - outlier
  - zscore
  - z-score
  - mad
  - iqr
  - 阈值
  - 告警
  - 毛刺
  - 突刺
  - 异常值
  - 检测
  - 超标
requires_tools:
  - query_sql
  - describe_measurement
  - list_measurements
---

# 异常检测指南

SonnetDB 内置 `anomaly()` 窗口函数，可在 SQL 中直接对时序数据做统计异常检测，无需外部工具。

---

## 1. 函数签名

```sql
anomaly(<field>, '<method>', <threshold>)
```

| 参数 | 类型 | 说明 |
| --- | --- | --- |
| `field` | 列名 | 数值型 FIELD 列（FLOAT 或 INT） |
| `method` | 字符串 | `'zscore'` / `'mad'` / `'iqr'` |
| `threshold` | 浮点数 | 触发阈值（含义随 method 不同） |

**返回值：** `BOOL`，`true` 表示该行是异常点。

---

## 2. 三种算法对比

| 算法 | 参数值 | 阈值含义 | 适用场景 | 对离群点鲁棒性 |
| --- | --- | --- | --- | --- |
| Z-Score | `'zscore'` | 标准差倍数（常用 2.0~3.0） | 近似正态分布，无极端离群点 | ❌ 弱（离群点影响均值/方差） |
| MAD | `'mad'` | 中位数绝对偏差倍数（常用 2.0~3.5） | **推荐默认**，对离群点鲁棒 | ✅ 强 |
| IQR | `'iqr'` | IQR 倍数，Tukey k（常用 1.5~3.0） | 偏态分布、箱线图风格 | ✅ 中等 |

### 算法选择决策树

```text
数据是否近似正态分布？
├─ 是，且没有极端离群点 → zscore（threshold=2.5）
└─ 否，或有极端离群点
   ├─ 数据偏态明显（如响应时间、金额）→ iqr（threshold=1.5）
   └─ 其他情况 → mad（threshold=3.0）  ← 推荐默认
```

---

## 3. 示例

### 3.1 基础异常检测

```sql
-- 检测 server-01 最近 1 小时 CPU 使用率的异常点
-- 使用 MAD 方法（推荐），阈值 3.0 倍中位数绝对偏差
SELECT
    time,
    cpu_pct,
    anomaly(cpu_pct, 'mad', 3.0) AS is_anomaly   -- true = 异常点
FROM host_cpu
WHERE host = 'server-01'
  AND time >= now() - 1h
ORDER BY time ASC;
```

### 3.2 只返回异常行

```sql
-- 筛选出所有异常点（WHERE 中使用 anomaly 结果）
-- 注意：anomaly() 是窗口函数，需要先在子查询中计算，再过滤
-- SonnetDB 当前版本支持直接在 WHERE 中使用窗口函数结果：
SELECT time, cpu_pct
FROM host_cpu
WHERE host = 'server-01'
  AND time >= now() - 24h
  AND anomaly(cpu_pct, 'mad', 3.0) = true
ORDER BY time ASC;
```

### 3.3 多指标联合异常检测

```sql
-- 同时检测 CPU 和内存的异常，任一异常即标记
SELECT
    time,
    cpu_pct,
    mem_pct,
    anomaly(cpu_pct, 'mad', 3.0)  AS cpu_anomaly,
    anomaly(mem_pct, 'mad', 3.0)  AS mem_anomaly
FROM host_resource
WHERE host = 'server-01'
  AND time >= now() - 6h
ORDER BY time ASC;
```

### 3.4 按时间段统计异常率（建议两步处理）

```sql
-- 第一步：先输出异常明细
-- 当前版本建议由应用层按 5 分钟、1 小时等窗口二次汇总
SELECT
    time,
    anomaly(cpu_pct, 'mad', 3.0) AS is_anomaly
FROM host_cpu
WHERE host = 'server-01'
  AND time >= now() - 24h
ORDER BY time ASC;
```

应用层可再按固定时间窗口统计：

- `total_samples`：窗口内总采样点数
- `anomaly_count`：窗口内 `is_anomaly = true` 的行数
- `anomaly_rate`：`anomaly_count / total_samples`

### 3.5 与 forecast() 联动（预测带异常检测）

```sql
-- 步骤 1：查看历史数据中的统计异常（实时监测用）
SELECT
    time,
    cpu_pct                          AS actual,
    anomaly(cpu_pct, 'mad', 2.5)    AS stat_anomaly   -- 统计异常
FROM host_cpu
WHERE host = 'server-01'
  AND time >= now() - 2h;

-- 步骤 2：查看预测值（应用层对比，判断是否超出预测区间）
SELECT time, value AS forecast, lower, upper
FROM forecast(host_cpu, cpu_pct, 12, 'holt_winters', 24)
WHERE host = 'server-01';
-- 若 actual > upper 或 actual < lower，则为预测带异常
```

---

## 4. 阈值调参指南

### Z-Score 阈值

| 阈值 | 含义 | 适用场景 |
| --- | --- | --- |
| 2.0 | 约 4.6% 的点被标记 | 敏感告警，误报较多 |
| 2.5 | 约 1.2% 的点被标记 | **推荐**，平衡敏感度和误报 |
| 3.0 | 约 0.3% 的点被标记 | 保守告警，只抓极端异常 |

### MAD 阈值

| 阈值 | 适用场景 |
| --- | --- |
| 2.0 | 敏感检测，适合关键指标 |
| 3.0 | **推荐默认**，适合大多数场景 |
| 3.5 | 保守检测，适合噪声较大的传感器 |

### IQR 阈值（Tukey k）

| 阈值 | 适用场景 |
| --- | --- |
| 1.5 | 标准箱线图，适合探索性分析 |
| 2.0 | 适合生产告警 |
| 3.0 | 只检测极端离群点 |

---

## 5. 告警集成模式

### 模式 A：应用层轮询（简单）

```csharp
// 每分钟查询一次，检查是否有新异常
var sql = @"
    SELECT time, cpu_pct
    FROM host_cpu
    WHERE host = 'server-01'
      AND time >= now() - 2m
      AND anomaly(cpu_pct, 'mad', 3.0) = true
    ORDER BY time DESC
    LIMIT 10";

var anomalies = await connection.QueryAsync(sql);
if (anomalies.Any())
{
    await alertService.SendAsync($"检测到 {anomalies.Count()} 个 CPU 异常点");
}
```

### 模式 B：异常事件写入专用 Measurement

```sql
-- 将异常点写入 anomaly_event measurement（应用层执行）
-- INSERT INTO anomaly_event (time, host, metric, value, method, threshold)
-- VALUES (<time>, 'server-01', 'cpu_pct', <value>, 'mad', 3.0)

-- 查询最近 24 小时的异常事件
SELECT time, host, metric, value
FROM anomaly_event
WHERE host = 'server-01'
  AND time >= now() - 24h
ORDER BY time DESC;
```

### 模式 C：SSE 事件流实时推送

```bash
# 订阅 SonnetDB SSE 事件流，监听 slow_query 等系统事件
GET /v1/events
Authorization: Bearer <token>
Accept: text/event-stream
# 应用层结合 anomaly() 查询结果，自行推送告警
```

---

## 6. 常见问题

**Q: anomaly() 返回全部 false，没有检测到任何异常？**
A: 检查数据是否足够（至少 10 个点），以及阈值是否过高。先用 `zscore` + 阈值 2.0 测试，再调整。

**Q: anomaly() 标记了太多点（误报率高）？**
A: 提高阈值（如从 2.5 → 3.0），或换用 `mad` 方法（对离群点更鲁棒，不会被少量极端值拉偏基线）。

**Q: 传感器有固定的日周期波动，anomaly() 把正常的峰值也标记了？**
A: 统计方法不感知周期性。建议先用 `forecast(holt_winters)` 建立预测带，再判断实际值是否超出预测区间，而非直接用 `anomaly()`。

**Q: 多个 series 的数据混在一起，anomaly() 会跨 series 计算吗？**
A: `anomaly()` 是窗口函数，在当前查询结果集范围内计算统计量。建议在 WHERE 中加 tag 过滤，确保每次只分析单个 series。
