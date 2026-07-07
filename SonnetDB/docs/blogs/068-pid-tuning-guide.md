## PID 参数调节完全指南：Kp/Ki/Kd 的作用与整定技巧

PID 控制器的三个参数 Kp、Ki、Kd 各自扮演着不同的角色，理解它们对系统响应的影响是进行有效调参的基础。本文将以通俗易懂的方式，解释每个参数的物理意义，提供实用的整定规则，并介绍如何利用 SonnetDB 进行高效的调参实践。

### Kp（比例增益）的作用

比例项是最基础的控制分量，它的输出与当前误差成正比。增大 Kp 可以加快系统的响应速度，减小稳态误差，但过大的 Kp 会导致系统振荡甚至不稳定。Kp 偏小则会使系统响应迟钝，误差消除缓慢。一个经验法则是：在 Ki 和 Kd 都为零的情况下，逐渐增大 Kp 直到系统开始持续振荡，然后将 Kp 减半作为初始值。在 SonnetDB 中，可以通过 `pid_series()` 快速观察不同 Kp 值下的响应差异。

```sql
-- 对比不同 Kp 值下的控制输出
SELECT 
    time, temperature,
    pid_series(100.0, temperature, time, 1.0, 0, 0) AS kp_1,
    pid_series(100.0, temperature, time, 2.0, 0, 0) AS kp_2,
    pid_series(100.0, temperature, time, 5.0, 0, 0) AS kp_5
FROM furnace_data
WHERE time >= now() - INTERVAL '1 hour';
```

### Ki（积分增益）的作用

积分项负责消除稳态误差。当系统存在持续偏差时，积分项会随时间逐渐累积，推动输出向消除误差的方向调整。Ki 过大会导致积分饱和（windup）和严重的超调，Ki 过小则使稳态误差消除过慢。积分项的引入还降低了系统的稳定性，因此增加 Ki 时通常需要适当降低 Kp。对于大多数工业过程，Ki 的初始值可以设为 Kp 的 1/10 到 1/5。

### Kd（微分增益）的作用

微分项根据误差的变化率进行预测性调节，起到阻尼作用。Kd 可以抑制超调、提高系统稳定性，但过大的 Kd 会对噪声敏感，导致控制器输出剧烈波动。值得注意的是，Kd 对缓慢变化的稳态误差没有帮助。在 SonnetDB 中，微分项的强度可以独立调节，方便工程师找到最佳的阻尼效果。

```sql
-- 观察 Kd 对超调的抑制效果
SELECT 
    time, temperature,
    pid_series(100.0, temperature, time, 2.0, 0.3, 0) AS without_kd,
    pid_series(100.0, temperature, time, 2.0, 0.3, 0.5) AS with_kd
FROM furnace_data
WHERE time >= now() - INTERVAL '1 hour';
```

### 抗积分饱和（Anti-Windup）

在实际应用中，控制器输出往往会受到执行机构的物理限制（如阀门开度只能在 0~100% 之间）。当输出达到限幅值时，积分项仍在累积，导致"积分饱和"现象——设定值回调后控制器无法及时响应。SonnetDB 的 PID 函数内置了抗积分饱和机制，当输出达到限幅值时自动暂停积分累积。在使用时，建议始终对 PID 输出进行限幅处理：

```sql
-- 带抗积分饱和的 PID 控制
SELECT 
    time, temperature,
    GREATEST(0, LEAST(100, 
        pid_series(100.0, temperature, time, 2.0, 0.3, 0.5)
    )) AS clamped_output
FROM furnace_data;
```

掌握这些参数的作用和调节技巧后，工程师可以显著缩短 PID 调参周期，提升控制系统的性能。SonnetDB 让这一过程变得透明、可重复、可量化。
