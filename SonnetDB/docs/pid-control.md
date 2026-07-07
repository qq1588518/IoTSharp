---
layout: default
title: PID 控制律内置函数
permalink: /pid-control/
---

# PID 控制律内置函数

SonnetDB 在 PR #54 中提供了三组与 PID 控制相关的能力，全部以纯 C# 实现、零外部依赖、可在嵌入式或 Server 模式下使用：

| 能力 | 形态 | API / SQL |
|------|------|-----------|
| 行级控制律 | 窗口函数 | `pid_series(field, setpoint, kp, ki, kd)` |
| 桶级控制律 | 聚合函数 | `pid(field, setpoint, kp, ki, kd)` |
| 阶跃响应辨识 + 自动整定 | 聚合函数 / 库 API | `pid_estimate(field, method, step_magnitude, initial_fraction, final_fraction, imc_lambda)` 或 `PidParameterEstimator.Estimate(...)` |

控制律统一为离散时间步进形式：

$$u(t_i) = K_p \cdot e_i + K_i \sum_{j=1}^{i} e_j \cdot \Delta t_j + K_d \cdot \dfrac{e_i - e_{i-1}}{\Delta t_i}$$

其中 $e_i = \text{setpoint} - \text{pv}_i$，$\Delta t_i$ 取相邻两行时间戳之差并归一化为秒。
**首行**没有上一行参考，只输出比例项 $u_0 = K_p \cdot e_0$；当相邻两行 $\Delta t \le 0$ 时跳过 I/D 项更新。

---

## 1. 行级窗口形态：`pid_series`

`pid_series` 是 [Tier 3 窗口函数](/sql-reference/#tier-3-窗口函数) 的一员，对每一行输出对应的控制量 $u(t)$，**不**改变行数。
适用于回测、控制律可视化，以及把控制量直接写回执行器表（控制回写）。

```sql
SELECT time, pid_series(temperature, 75.0, 0.6, 0.1, 0.05) AS valve
FROM reactor
WHERE device = 'r1' AND time > now() - 1m;
```

### 控制回写示例

> 当前 SQL 前端尚未支持 `INSERT … SELECT` 直接写回，
> 嵌入式或 Server 客户端可分两步完成：先 `SELECT pid_series(...)` 拿到结果，再用客户端循环 `INSERT INTO actuator (...) VALUES (...)`。

```csharp
using var db = Tsdb.Open(new TsdbOptions { RootDirectory = "./data" });

// 1) 获取 PID 控制律输出
var result = (SelectExecutionResult)SqlExecutor.Execute(db,
    "SELECT time, pid_series(temperature, 75.0, 0.6, 0.1, 0.05) AS valve " +
    "FROM reactor WHERE device = 'r1'");

// 2) 写回 actuator 表
var sb = new StringBuilder("INSERT INTO actuator (time, device, valve) VALUES ");
for (int i = 0; i < result.Rows.Count; i++)
{
    if (i > 0) sb.Append(", ");
    sb.Append('(').Append(result.Rows[i][0])
      .Append(", 'r1', ")
      .Append(((double)result.Rows[i][1]!).ToString(CultureInfo.InvariantCulture))
      .Append(')');
}
SqlExecutor.Execute(db, sb.ToString());
```

### 语义细节

- **空值传播**：当某行的 `field` 为 `NULL`（外连接空洞），该行输出 `NULL`，控制器内部状态保持不变，下一非空行继续推进。
- **同 series 隔离**：函数按 series 独立维护 `PidController` 实例，不同 `host` / `device` 的状态互不污染。
- **时间方向**：要求时间戳严格递增；当存在重复时间戳时，仅输出比例项以避免发散。

---

## 2. 桶级聚合形态：`pid`

`pid` 与 `avg` / `p95` / `histogram` 等扩展聚合一样，可与 `GROUP BY time(...)` 组合，
桶内逐行推进控制器，桶结束时输出**最后一行**的控制量。适合做粗粒度仪表盘或定时下发场景。

```sql
SELECT pid(temperature, 75.0, 0.6, 0.1, 0.05) AS valve
FROM reactor
WHERE device = 'r1' AND time > now() - 1h
GROUP BY time(1m);
```

每个 1 分钟桶之间，控制器**重新初始化**（每桶都是独立 `PidAccumulator`）；如果需要跨桶持续控制，请改用 `pid_series` 后再做时间下采样。

---

## 3. 阶跃响应自动整定

### 3.1 SQL 聚合形态：`pid_estimate`

`pid_estimate` 是一个聚合函数，对结果集中的全部 (time, value) 样本调用
Sundaresan & Krishnaswamy 35%/85% 两点法识别 FOPDT 模型，再按指定整定规则计算
`Kp / Ki / Kd`，输出一行 JSON 字符串 `{"kp":..,"ki":..,"kd":..}`。

```sql
SELECT pid_estimate(temperature, 'imc', 1.0, 0.1, 0.1, NULL) AS tuning
FROM reactor
WHERE device = 'r1'
  AND time BETWEEN 1700000000000 AND 1700001800000;
```

参数：

| 位置 | 参数 | 类型 | 含义 |
|------|------|------|------|
| 1 | `field` | FIELD 列 | 过程变量 PV |
| 2 | `method` | 字符串字面量或 NULL | `'zn'` / `'cc'` / `'imc'`；NULL 默认 `'zn'` |
| 3 | `step_magnitude` | 数值字面量或 NULL | 阶跃幅度 Δu；NULL 表示假定 Δu = 1.0 |
| 4 | `initial_fraction` | (0,0.5) 数值或 NULL | 取首部样本求 y₀ 的比例，默认 0.1 |
| 5 | `final_fraction` | (0,0.5) 数值或 NULL | 取尾部样本求 y∞ 的比例，默认 0.1 |
| 6 | `imc_lambda` | 数值或 NULL | 仅 `'imc'` 生效；NULL 时 λ = θ |

> **建议工作流**：先在客户端解析 JSON，把 `Kp/Ki/Kd` 作为常量带回到 `pid_series` / `pid` 控制律，
> 实现「整定 → 回测 → 上线」闭环。

### 3.2 库 API：`PidParameterEstimator.Estimate`

嵌入式场景如果想跳过 SQL，直接拿到强类型 `PidParameters` 记录，仍可使用库 API：

```csharp
using SonnetDB.Query.Functions.Control;

// 1) 从历史阶跃响应数据查询样本
var rows = ((SelectExecutionResult)SqlExecutor.Execute(db,
    "SELECT time, temperature FROM reactor WHERE device='r1' " +
    "AND time BETWEEN 1700000000000 AND 1700001800000")).Rows;

var samples = rows
    .Select(r => ((long)r[0]!, (double)r[1]!))
    .ToArray();

// 2) 选择整定规则并辨识参数
var p = PidParameterEstimator.Estimate(samples, new PidEstimationOptions
{
    Method = PidTuningMethod.Imc,            // ZieglerNichols / CohenCoon / Imc
    StepMagnitude = 1.0,                     // 阶跃幅度（控制量变化）
    InitialFraction = 0.1,                   // 取首部 10% 求 y₀
    FinalFraction = 0.1,                     // 取尾部 10% 求 y∞
    ImcLambda = null,                        // 默认 λ = θ；增大可提高鲁棒性
});

Console.WriteLine($"Kp={p.Kp:F3}  Ki={p.Ki:F3}  Kd={p.Kd:F3}");

// 3) 把整定结果带回 SQL 控制律
var sql = $"SELECT pid_series(temperature, 75.0, " +
          $"{p.Kp.ToString(CultureInfo.InvariantCulture)}, " +
          $"{p.Ki.ToString(CultureInfo.InvariantCulture)}, " +
          $"{p.Kd.ToString(CultureInfo.InvariantCulture)}) FROM reactor";
```

### 三种整定规则

| `PidTuningMethod` | 适用场景 |
|-------------------|----------|
| `ZieglerNichols` | 快速响应，调节器经典默认；超调较大 |
| `CohenCoon` | 短滞后、长惯性过程；比 ZN 更强鲁棒性 |
| `Imc`（Skogestad SIMC） | 推荐默认；通过 `ImcLambda` 在响应速度与鲁棒性之间调节 |

辨识失败常见原因：

- 数据点少于 10 个 → `ArgumentException`
- 阶跃幅度过小（首尾稳态差异 ≈ 0）→ `InvalidOperationException`
- 数据未覆盖 35% 或 85% 稳态点 → `InvalidOperationException`

---

## 4. 端到端工作流

1. **采集**：把过程变量（如反应器温度）写入 measurement，使用控制器/PLC 周期性 `INSERT`。
2. **离线整定**：抽取一段阶跃响应数据，调用 `pid_estimate(...)` SQL 聚合或 `PidParameterEstimator.Estimate(...)` 库 API 得到 `Kp / Ki / Kd`。
3. **回测**：用 `SELECT pid_series(...)` 在历史数据上预演控制律，验证响应是否合预期。
4. **上线**：把 `pid_series(...)` 输出按时序写入 actuator 表；下游执行器订阅该表（参考 [SSE 推送]({{ '/getting-started/' | relative_url }})）即可获得实时控制量。
5. **监控**：用 `SELECT pid(temperature, ...) FROM reactor GROUP BY time(1m)` 监控控制律在每分钟末的输出，便于仪表盘报警。

---

## 参考实现

- 控制器内核：[`src/SonnetDB/Query/Functions/Control/PidController.cs`](https://github.com/IoTSharp/SonnetDB/blob/main/src/SonnetDB/Query/Functions/Control/PidController.cs)
- 行级窗口函数：[`PidSeriesFunction.cs`](https://github.com/IoTSharp/SonnetDB/blob/main/src/SonnetDB/Query/Functions/Control/PidSeriesFunction.cs)
- 聚合函数：[`PidAggregateFunction.cs`](https://github.com/IoTSharp/SonnetDB/blob/main/src/SonnetDB/Query/Functions/Control/PidAggregateFunction.cs)
- 自动整定 SQL：[`PidEstimateFunction.cs`](https://github.com/IoTSharp/SonnetDB/blob/main/src/SonnetDB/Query/Functions/Control/PidEstimateFunction.cs)
- 自动整定库 API：[`PidParameterEstimator.cs`](https://github.com/IoTSharp/SonnetDB/blob/main/src/SonnetDB/Query/Functions/Control/PidParameterEstimator.cs)

更多 SQL 函数参考请见 [SQL Reference]({{ '/sql-reference/' | relative_url }})。
