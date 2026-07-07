## PID 控制算法入门：pid_series() 流式计算

PID（比例-积分-微分）控制器是工业自动化领域最经典、应用最广泛的控制算法。从温度控制到电机调速，从压力调节到流量管理，PID 控制器无处不在。SonnetDB 突破了传统数据库的功能边界，将 PID 控制算法以 SQL 函数的形式引入时序数据库，使得开发者可以直接在数据存储层完成控制逻辑的计算。本文将重点介绍 `pid_series()` 函数——一款用于流式 PID 计算的强大工具。

### PID 控制原理简述

PID 控制器的输出由三个分量组成：**比例项（Kp）** 直接响应当前误差，误差越大，调整力度越大；**积分项（Ki）** 累积过去的所有误差，消除稳态偏差；**微分项（Kd）** 预测误差的变化趋势，抑制超调和振荡。三个分量加权求和后，得到控制器的输出值。SonnetDB 的 `pid_series()` 函数正是基于这一经典算法实现，接受设定值（setpoint）、过程值（process variable）和时间序列，返回实时的 PID 输出。

```sql
-- 简单的流式 PID 控制
SELECT 
    time,
    temperature AS pv,
    pid_series(100.0, temperature, time, 
               2.0, 0.5, 0.1) AS control_output
FROM furnace_data
WHERE device_id = 'furnace-03'
ORDER BY time;
```

### 参数释义与调优

`pid_series(setpoint, pv, time, Kp, Ki, Kd)` 的参数含义清晰：`setpoint` 是目标值（如加热炉的目标温度 100°C），`pv` 是当前实际测量值（过程变量），`time` 是时间戳列，`Kp`、`Ki`、`Kd` 分别是比例、积分、微分系数。在温度控制场景中，如果 Kp 设为 2.0，当温度偏差为 5°C 时，比例项将贡献 10% 的输出功率。

```sql
-- 多设备 PID 对比分析
SELECT 
    time,
    device_id,
    temperature AS pv,
    pid_series(100.0, temperature, time, 
               CASE device_id 
                   WHEN 'furnace-01' THEN 1.5
                   WHEN 'furnace-02' THEN 2.0
                   ELSE 1.8 
               END, 
               0.3, 0.05) AS output
FROM furnace_data
WHERE device_id IN ('furnace-01', 'furnace-02')
ORDER BY time;
```

### 工业化应用示例

在实际的工业控制场景中，`pid_series()` 可以用于构建数据驱动的控制回路。例如，在化工反应釜的温度控制中，传感器每秒钟采集一次温度数据，`pid_series()` 实时计算出阀门开度的调节量。工程师可以通过简单的 SQL 查询，在数据库层面完成从数据采集、PID 计算到控制指令生成的全流程。

```sql
-- 反应釜温度 PID 控制（含输出限幅）
SELECT 
    time,
    temperature,
    GREATEST(0, LEAST(100, 
        pid_series(150.0, temperature, time, 2.5, 0.8, 0.05)
    )) AS valve_open_pct
FROM reactor_data
WHERE reactor_id = 'R-101';
```

`pid_series()` 的流式计算特性使其特别适合实时数据处理管道，配合 SonnetDB 的其他时序函数，能够构建出功能完善的数据驱动控制系统。
