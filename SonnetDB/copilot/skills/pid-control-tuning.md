---
name: pid-control-tuning
description: 使用 SonnetDB PID 控制器与外推函数做闭环参数整定的快速指南。
triggers:
  - pid
  - 控制
  - 整定
  - tuning
  - kp ki kd
  - setpoint
requires_tools:
  - query_sql
  - docs_search
---

# PID 控制整定

适用场景：用户想用 SonnetDB 内置 `pid_*` 函数做闭环控制（PR #50 系列），或不知道 Kp/Ki/Kd 该怎么试。

## 推荐流程

1. **采集开环响应**：把执行机构按阶跃信号驱动，用 `query_sql` 抓取 1～2 个时间常数的过程量曲线。
2. **估算时间常数 τ 与延迟 L**：`docs_search query="ziegler nichols"` 获取经验公式。
3. **初始 Kp/Ki/Kd**：使用 Ziegler-Nichols 表格作为起点，再按 0.5～1.5 的系数微调。
4. **在线观察**：`SELECT setpoint, pv, output FROM control_loop ORDER BY time DESC LIMIT 200` 看是否有过冲、震荡。
5. **抗积分饱和**：当 output 持续顶到上下限时，把积分项 reset，或加入 anti-windup。

## SonnetDB 内置函数

| 函数 | 用途 |
| ---- | ---- |
| `pid_step(measurement, sp, pv, kp, ki, kd)` | 单步增量式 PID |
| `pid_replay(measurement, ...)` | 历史回放，用于离线参数搜索 |

## 提示

- Kp 太大 → 高频震荡；Kp 太小 → 响应慢。
- Ki 引入相位滞后 90°，过大易振荡；为零会有稳态误差。
- Kd 对噪声敏感，必要时加一个一阶低通（`ema(...)`）。
