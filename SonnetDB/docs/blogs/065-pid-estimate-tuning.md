## PID 参数整定估计：三种经典方法

PID 控制器的性能很大程度上取决于 Kp、Ki、Kd 三个参数的设置。手工调参往往耗时耗力，尤其是对于缺乏控制理论背景的开发者而言。SonnetDB 的 `pid_estimate()` 函数提供了三种经典的参数整定方法——IMC（内部模型控制）、Ziegler-Nichols 和 Cohen-Coon，帮助用户根据系统的阶跃响应数据自动估算最优的 PID 参数。

### IMC 方法

IMC（Internal Model Control）方法基于系统的过程模型进行整定，通过一个可调参数 lambda 来控制闭环响应的快慢。该方法对建模误差具有较强的鲁棒性，适合处理有较大不确定性的工业过程。在 `pid_estimate()` 中使用 IMC 方法时，需要提供系统的一阶加纯滞后（FOPDT）模型参数：过程增益 K、时间常数 T 和纯滞后时间 L。

```sql
-- 使用 IMC 方法估算 PID 参数
SELECT 
    pid_estimate(
        'imc', 
        json_object(
            'K', 2.5,      -- 过程增益
            'T', 60.0,     -- 时间常数（秒）
            'L', 5.0       -- 纯滞后时间（秒）
        ),
        json_object(
            'lambda', 15.0  -- 控制器调节参数
        )
    ) AS estimated_pid;
```

### Ziegler-Nichols 方法

Ziegler-Nichols 是最经典的 PID 整定方法之一，分为两种变体：阶跃响应法（基于过程响应曲线）和频域法（基于临界增益和临界周期）。该方法简单实用，适用于大多数工业过程，但得到的参数往往需要进一步微调以减少超调量。SonnetDB 的实现支持基于 S 型响应曲线的参数估算。

```sql
-- 使用 Ziegler-Nichols 方法基于阶跃响应估算
SELECT 
    pid_estimate(
        'ziegler-nichols',
        json_object(
            'K', 2.5,
            'T', 60.0,
            'L', 5.0
        )
    ) AS estimated_pid;
```

### Cohen-Coon 方法

Cohen-Coon 方法是对 Ziegler-Nichols 的改进，特别针对自衡过程（self-regulating processes）进行了优化。它在处理大滞后系统时表现更好，能够提供更准确的参数估计，尤其适合那些对稳态精度要求较高的工业控制场景。该方法生成的 PID 参数通常比 Ziegler-Nichols 更为保守，超调量更小。

```sql
-- 使用 Cohen-Coon 方法估算
SELECT 
    pid_estimate(
        'cohen-coon',
        json_object(
            'K', 2.5,
            'T', 60.0,
            'L', 5.0
        )
    ) AS estimated_pid;
```

### 结果解释与实战建议

`pid_estimate()` 返回一个包含 `Kp`、`Ki`、`Kd` 三个参数的 JSON 对象。实际应用中，建议从 IMC 方法的估算结果开始，因为其 lambda 参数提供了直观的"调节速度"控制——较小的 lambda 值使控制器响应更快（但超调更大），较大的 lambda 值使响应更平滑。无论使用哪种方法，都应该在实际系统上验证控制效果并进行必要的微调。
