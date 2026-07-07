---
name: changepoint-detect
description: 使用 SonnetDB 内置 changepoint() 窗口函数检测时序数据的趋势突变点（CUSUM 算法）：参数调优、结果解读、与 anomaly() 的区别、告警集成。
triggers:
  - changepoint
  - 变点
  - 突变
  - 趋势变化
  - cusum
  - 漂移
  - drift
  - 分布变化
  - 均值漂移
  - 突变检测
  - 变点检测
  - 趋势突变
  - 基线变化
requires_tools:
  - query_sql
  - describe_measurement
---

# 变点检测指南

SonnetDB 内置 `changepoint()` 窗口函数，使用 CUSUM（累积和）算法检测时序数据中的**趋势突变点**——即数据均值发生持续性漂移的时刻，而非偶发毛刺。

---

## 1. anomaly() vs changepoint() 的区别

| 特性 | `anomaly()` | `changepoint()` |
| --- | --- | --- |
| 检测目标 | 单点偶发异常（毛刺） | 持续性均值漂移（趋势突变） |
| 典型场景 | 传感器噪声、偶发故障 | 系统升级后性能变化、设备老化 |
| 算法 | Z-Score / MAD / IQR | CUSUM（双边累积和） |
| 对噪声敏感度 | 高 | 低（累积效应过滤噪声） |
| 触发后复位 | 每行独立判断 | 触发后复位，继续监测下一段 |

**选择原则：**
- 想找"某一刻突然飙高又回落" → `anomaly()`
- 想找"从某时刻起，均值整体抬升/下降" → `changepoint()`

---

## 2. 函数签名

```sql
changepoint(<field>, '<method>', <threshold> [, <drift>])
```

| 参数 | 类型 | 说明 |
| --- | --- | --- |
| `field` | 列名 | 数值型 FIELD 列 |
| `method` | 字符串 | 目前仅支持 `'cusum'` |
| `threshold` | 浮点数 | 触发阈值 h（倍 σ₀），常用 3.0~5.0，越大越保守 |
| `drift` | 浮点数（可选） | 漂移容忍度 k（倍 σ₀），默认 0.5 |

**返回值：** `BOOL`，`true` 表示该行检测到突变点（累积和超过阈值后触发，并复位）。

### CUSUM 算法原理

```text
1. 用前 max(5, n/4) 个非空样本估计基线 μ₀、σ₀
2. 双边累积和：
   S⁺ᵢ = max(0, S⁺ᵢ₋₁ + (xᵢ - μ₀) - k·σ₀)   ← 检测上升漂移
   S⁻ᵢ = min(0, S⁻ᵢ₋₁ + (xᵢ - μ₀) + k·σ₀)   ← 检测下降漂移
3. S⁺ᵢ > h·σ₀ 或 S⁻ᵢ < -h·σ₀ 时输出 true，并复位
```

---

## 3. 示例

### 3.1 基础变点检测

```sql
-- 检测 server-01 CPU 使用率的趋势突变
-- threshold=4.0：需要累积偏差超过 4 倍标准差才触发（中等保守）
-- drift=0.5：允许 0.5 倍标准差的自然漂移
SELECT
    time,
    cpu_pct,
    changepoint(cpu_pct, 'cusum', 4.0, 0.5) AS shift_detected
FROM host_cpu
WHERE host = 'server-01'
  AND time >= now() - 24h
ORDER BY time ASC;
```

### 3.2 只返回突变点

```sql
-- 找出过去 7 天内所有发生趋势突变的时刻
SELECT time, cpu_pct
FROM host_cpu
WHERE host = 'server-01'
  AND time >= now() - 7d
  AND changepoint(cpu_pct, 'cusum', 4.0) = true
ORDER BY time ASC;
```

### 3.3 多设备并行检测

```sql
-- 分别检测每台服务器的变点（每个 series 独立计算 CUSUM）
-- server-01
SELECT time, 'server-01' AS host, cpu_pct,
       changepoint(cpu_pct, 'cusum', 4.0) AS shift
FROM host_cpu WHERE host = 'server-01' AND time >= now() - 7d;

-- server-02
SELECT time, 'server-02' AS host, cpu_pct,
       changepoint(cpu_pct, 'cusum', 4.0) AS shift
FROM host_cpu WHERE host = 'server-02' AND time >= now() - 7d;
```

### 3.4 变点 + 异常联合分析

```sql
-- 同时标记偶发异常（anomaly）和趋势突变（changepoint）
-- 两者结合可区分"毛刺"和"系统性变化"
SELECT
    time,
    cpu_pct,
    anomaly(cpu_pct, 'mad', 3.0)        AS is_spike,      -- 偶发毛刺
    changepoint(cpu_pct, 'cusum', 4.0)  AS is_shift       -- 趋势突变
FROM host_cpu
WHERE host = 'server-01'
  AND time >= now() - 24h
ORDER BY time ASC;
-- 解读：
-- is_spike=true, is_shift=false → 偶发毛刺，可能是瞬时负载
-- is_spike=false, is_shift=true → 趋势突变，可能是版本发布或配置变更
-- 两者都 true → 严重异常，需立即排查
```

### 3.5 变点时间戳关联业务事件

```sql
-- 查找变点时刻，关联是否有部署/变更事件
-- 步骤 1：找出变点时刻
SELECT time AS shift_time, cpu_pct
FROM host_cpu
WHERE host = 'server-01'
  AND time >= now() - 30d
  AND changepoint(cpu_pct, 'cusum', 4.0) = true
ORDER BY time ASC;

-- 步骤 2：在应用层查询变点时刻前后 30 分钟的部署记录
-- SELECT * FROM deploy_event
-- WHERE time BETWEEN <shift_time - 30m> AND <shift_time + 30m>
```

---

## 4. 参数调优指南

### threshold（触发阈值 h）

| 值 | 灵敏度 | 适用场景 |
| --- | --- | --- |
| 2.0~3.0 | 高（容易触发） | 关键指标，不允许任何漂移 |
| 3.0~4.0 | 中（**推荐**） | 大多数生产监控场景 |
| 4.0~5.0 | 低（保守） | 噪声大的传感器，减少误报 |

### drift（漂移容忍度 k）

| 值 | 含义 | 适用场景 |
| --- | --- | --- |
| 0.25 | 对微小漂移敏感 | 精密控制场景 |
| 0.5 | **默认推荐** | 大多数场景 |
| 1.0 | 只检测显著漂移 | 噪声大、自然波动明显的数据 |

### 调参步骤

```text
1. 先用默认值（threshold=4.0, drift=0.5）运行
2. 如果误报太多（正常数据被标记）→ 增大 threshold 或 drift
3. 如果漏报太多（已知变点未检测到）→ 减小 threshold 或 drift
4. 参考：基线估计用前 max(5, n/4) 个点，数据量少时基线不准
```

---

## 5. 典型应用场景

| 场景 | 说明 | 推荐参数 |
| --- | --- | --- |
| 版本发布后性能回归检测 | 检测 P99 延迟是否在发布后持续升高 | threshold=3.5, drift=0.5 |
| 设备老化监测 | 检测振动/温度是否长期趋势上升 | threshold=4.0, drift=0.5 |
| 流量基线变化 | 检测 QPS 是否发生结构性变化 | threshold=4.0, drift=1.0 |
| 传感器漂移校准 | 检测传感器读数是否需要重新校准 | threshold=3.0, drift=0.25 |
| 能耗异常 | 检测设备能耗是否持续偏高 | threshold=3.5, drift=0.5 |

---

## 6. 常见问题

**Q: changepoint() 在数据开头就触发了？**
A: CUSUM 用前 max(5, n/4) 个点估计基线，数据量太少时基线不准。确保查询范围内有足够的历史数据（建议 ≥ 50 个点）。

**Q: 同一段时间内触发了多个变点？**
A: 正常现象。CUSUM 触发后复位，继续监测下一段。多个变点说明数据经历了多次均值漂移。

**Q: threshold 设多少合适？**
A: 从 4.0 开始，如果漏报已知的变点则减小，如果误报太多则增大。没有通用最优值，需根据数据特性调整。

**Q: changepoint() 能检测方差变化（不只是均值变化）吗？**
A: 当前 CUSUM 实现主要检测均值漂移。方差变化检测（如 BOCPD、PELT 算法）可通过 UDF 扩展实现。
