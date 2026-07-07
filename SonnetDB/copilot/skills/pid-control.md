---
name: pid-control
description: SonnetDB 内置 PID 控制函数完整指南：pid_series（行级窗口）、pid（桶级聚合）、pid_estimate（阶跃响应自动整定）的使用方法、参数调优、端到端控制回路设计。
triggers:
  - pid
  - pid_series
  - pid_estimate
  - 控制
  - 整定
  - tuning
  - kp
  - ki
  - kd
  - setpoint
  - 过程控制
  - 闭环
  - 控制器
  - 阶跃响应
  - 自动整定
  - 比例积分微分
  - 执行器
  - 过程变量
  - pv
  - imc
  - ziegler
  - cohen
requires_tools:
  - query_sql
  - describe_measurement
  - list_measurements
---

# PID 控制函数完整指南

SonnetDB 在 PR #54 中内置了三组 PID 相关能力，纯 C# 实现、零外部依赖，可直接在 SQL 中完成控制律计算、历史回放和参数自动整定。

---

## 1. 三个函数概览

| 函数 | 形态 | 典型用途 |
|------|------|----------|
| `pid_series(field, setpoint, kp, ki, kd)` | 窗口函数（行级） | 对历史数据逐行计算控制量，用于回测和实时控制 |
| `pid(field, setpoint, kp, ki, kd)` | 聚合函数（桶级） | 每个时间桶输出最终控制量，用于监控仪表盘 |
| `pid_estimate(field, method, ...)` | 聚合函数 | 从阶跃响应数据自动整定 Kp/Ki/Kd，输出 JSON |

**控制律公式（离散时间步进）：**

$$u(t_i) = K_p \cdot e_i + K_i \sum_{j=1}^{i} e_j \cdot \Delta t_j + K_d \cdot \frac{e_i - e_{i-1}}{\Delta t_i}$$

- $e_i = \text{setpoint} - \text{pv}_i$（误差）
- 首行只输出比例项 $u_0 = K_p \cdot e_0$
- 相邻两行 $\Delta t \le 0$ 时跳过 I/D 项

---

## 2. pid_series — 行级窗口函数

每行输出一个控制量，不改变行数，适合**实时控制回路**和**历史回测**。

### 语法

```sql
SELECT time, pid_series(<field>, <setpoint>, <kp>, <ki>, <kd>) AS <alias>
FROM <measurement>
WHERE <tag_filter> AND <time_filter>
ORDER BY time ASC;   -- 必须时间正序
```

### 示例：反应釜温度控制

```sql
-- 计算反应釜 r1 的阀门控制量
-- setpoint = 75.0°C，Kp=0.6，Ki=0.1，Kd=0.05
SELECT
    time,
    temperature                                          AS pv,       -- 过程变量（实测温度）
    75.0                                                 AS setpoint, -- 目标温度
    pid_series(temperature, 75.0, 0.6, 0.1, 0.05)      AS valve     -- 控制输出（阀门开度）
FROM reactor
WHERE device = 'r1'
  AND time >= now() - 1h   -- 最近 1 小时
ORDER BY time ASC;          -- 时间正序（控制律要求）
```

### 语义细节

```
✅ 按 series 独立维护控制器状态（不同 device 互不干扰）
✅ field 为 NULL 时输出 NULL，控制器状态保持不变
✅ 时间戳严格递增（乱序数据会导致 Δt ≤ 0，跳过 I/D 项）
⚠️  跨查询不保留状态（每次 SELECT 重新初始化控制器）
```

### C# 控制回写示例

```csharp
// 查询控制量
var result = (SelectExecutionResult)SqlExecutor.Execute(db,
    "SELECT time, pid_series(temperature, 75.0, 0.6, 0.1, 0.05) AS valve " +
    "FROM reactor WHERE device = 'r1' ORDER BY time ASC");

// 将控制量写入执行器 measurement
foreach (var row in result.Rows)
{
    SqlExecutor.Execute(db,
        $"INSERT INTO actuator (time, device, valve_pct) " +
        $"VALUES ({row.time}, 'r1', {row.valve})");
}
```

---

## 3. pid — 桶级聚合函数

与 `GROUP BY time(...)` 配合使用，每个时间桶输出**最后一行**的控制量，适合**监控仪表盘**和**趋势分析**。

### 语法

```sql
SELECT
    pid(<field>, <setpoint>, <kp>, <ki>, <kd>) AS <alias>
FROM <measurement>
WHERE <tag_filter> AND <time_filter>
GROUP BY time(<interval>);
```

### 示例：每分钟控制量监控

```sql
-- 每分钟聚合一次控制量，用于仪表盘展示
-- 注意：每个桶之间控制器重新初始化
SELECT
    avg(temperature)         AS avg_pv,   -- 桶内平均过程变量
    75.0                     AS setpoint, -- 目标值
    pid(temperature, 75.0, 0.6, 0.1, 0.05) AS valve_end  -- 桶末控制量
FROM reactor
WHERE device = 'r1'
  AND time >= now() - 1h
GROUP BY time(1m);
```

> ⚠️ **重要**：`pid()` 每个桶重新初始化控制器，跨桶持续控制请用 `pid_series()`。
> 当前版本不会自动返回 bucket 起始时间列，如需显示桶标签，请由应用层结合固定桶宽推导。

---

## 4. pid_estimate — 自动整定

从阶跃响应数据中自动识别 FOPDT 模型参数，输出 Kp/Ki/Kd 建议值（JSON 格式）。

### 函数签名

```sql
SELECT pid_estimate(
    <field>,           -- 过程变量 PV
    '<method>',        -- 整定规则：'zn' / 'cc' / 'imc'
    <step_magnitude>,  -- 阶跃幅度 Δu（NULL = 1.0）
    <initial_fraction>,-- 取首部样本求 y₀ 的比例（NULL = 0.1）
    <final_fraction>,  -- 取尾部样本求 y∞ 的比例（NULL = 0.1）
    <imc_lambda>       -- 仅 'imc' 生效，响应速度参数（NULL = θ）
) AS tuning
FROM <measurement>
WHERE <tag_filter>
  AND time BETWEEN <step_start> AND <step_end>;
```

### 三种整定规则

| 方法 | 参数值 | 适用场景 | 特点 |
|------|--------|----------|------|
| Ziegler-Nichols | `'zn'` | 快速响应，经典默认 | 超调较大（约 25%），响应快 |
| Cohen-Coon | `'cc'` | 短滞后、长惯性过程 | 比 ZN 超调小，适合大时延 |
| IMC（Skogestad SIMC） | `'imc'` | **推荐默认** | 通过 `imc_lambda` 调节响应速度与鲁棒性 |

### 示例：从阶跃响应自动整定

```sql
-- 第一步：记录阶跃实验数据
-- 在 2024-04-21 08:00 施加阶跃信号，观察到 2024-04-21 08:30 系统稳定

-- 第二步：用 pid_estimate 自动整定（推荐 IMC 方法）
SELECT pid_estimate(
    temperature,   -- 过程变量
    'imc',         -- IMC 整定规则（推荐）
    1.0,           -- 阶跃幅度（执行器变化量）
    0.1,           -- 用前 10% 数据估计初始值 y₀
    0.1,           -- 用后 10% 数据估计稳态值 y∞
    NULL           -- imc_lambda = NULL 时自动设为 θ（过程延迟）
) AS tuning
FROM reactor
WHERE device = 'r1'
  AND time BETWEEN 1713686400000   -- 2024-04-21 08:00:00 UTC（阶跃开始）
              AND 1713688200000;   -- 2024-04-21 08:30:00 UTC（稳定后）
```

**返回示例：**
```json
{"kp": 0.612, "ki": 0.089, "kd": 0.047}
```

### 用整定结果验证（回测）

```sql
-- 将 pid_estimate 输出的参数代入 pid_series 做历史回测
-- 验证控制效果是否满足要求
SELECT
    time,
    temperature                                        AS pv,
    75.0                                               AS setpoint,
    pid_series(temperature, 75.0, 0.612, 0.089, 0.047) AS valve  -- 使用整定参数
FROM reactor
WHERE device = 'r1'
  AND time >= 1713688200000   -- 从稳定后开始验证
  AND time <= now()
ORDER BY time ASC;
```

---

## 5. C# 库 API（嵌入式模式）

```csharp
using SonnetDB.Analytics;

// 准备采样数据
var samples = new PidSample[]
{
    new(timestampMs: 1713686400000, value: 20.0),
    new(timestampMs: 1713686410000, value: 22.5),
    // ...
};

// 自动整定
var parameters = PidParameterEstimator.Estimate(samples, new PidEstimationOptions
{
    Method = PidTuningMethod.Imc,       // 推荐 IMC
    StepMagnitude = 1.0,
    InitialFraction = 0.1,
    FinalFraction = 0.1,
    ImcLambda = null,                   // 自动设为 θ
});

Console.WriteLine($"Kp={parameters.Kp:F3}  Ki={parameters.Ki:F3}  Kd={parameters.Kd:F3}");
```

---

## 6. 端到端工作流

```
1. 采集开环阶跃响应
   └─ 施加阶跃信号，INSERT 过程变量数据到 measurement

2. 自动整定参数
   └─ pid_estimate(field, 'imc', ...) → 得到 Kp/Ki/Kd

3. 历史回测验证
   └─ pid_series(field, setpoint, Kp, Ki, Kd) → 验证控制效果

4. 上线实时控制
   └─ 应用层定时查询 pid_series → 写入执行器 measurement

5. 监控仪表盘
   └─ pid(...) GROUP BY time(1m) → 每分钟控制量趋势图
```

---

## 7. 参数调优指南

### Kp（比例增益）

```
Kp 太大 → 高频震荡，系统不稳定
Kp 太小 → 响应慢，稳态误差大
调整方法：从 pid_estimate 结果的 50% 开始，逐步增大
```

### Ki（积分增益）

```
Ki 引入 90° 相位滞后，过大易振荡
Ki = 0 → 有稳态误差（适合允许偏差的场景）
Ki 过大 → 积分饱和（anti-windup 问题）
调整方法：先置 0 调好 Kp，再逐步增大 Ki
```

### Kd（微分增益）

```
Kd 对噪声极敏感，传感器噪声大时慎用
Kd 过大 → 控制量剧烈抖动
建议：先用 anomaly(field, 'mad', 2.0) 过滤噪声后再计算微分
调整方法：通常设为 Kp × τ / 4（τ 为时间常数）
```

### IMC Lambda 调节

```
imc_lambda 小 → 响应快，鲁棒性差（对模型误差敏感）
imc_lambda 大 → 响应慢，鲁棒性好（推荐生产环境）
经验值：imc_lambda = 1~3 × θ（θ 为过程延迟）
```

---

## 8. 常见问题

**Q: pid_series 输出的控制量是什么单位？**  
A: 与 setpoint 和 field 的单位相关，是无量纲的控制增量。需要在应用层映射到执行器的实际范围（如 0~100% 阀门开度）。

**Q: 控制量出现积分饱和（一直在上限或下限）？**  
A: 减小 Ki，或在应用层对控制量做限幅（clamp），并在限幅时暂停积分累积（anti-windup）。

**Q: pid_estimate 返回的参数导致震荡？**  
A: 先将 Kp 减半，Ki 减半，Kd 置 0，逐步调整。或换用 `'cc'` 方法（Cohen-Coon 超调更小）。

**Q: 多个设备共用一套参数合适吗？**  
A: 不建议。不同设备的时间常数和延迟不同，应分别整定。`pid_series` 按 series 隔离，不同 device 的控制器状态互不干扰。
