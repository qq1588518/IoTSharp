## PID 调参实战：阶跃响应分析与参数解读

在前一篇文章中，我们介绍了 `pid_estimate()` 三种整定方法的理论基础。本文将继续深入实战层面，讲解如何通过阶跃响应分析获取系统特征参数，以及如何解读调参结果 JSON 中的各项指标，帮助工程师在实际项目中更好地应用 SonnetDB 的 PID 功能。

### 阶跃响应实验设计

阶跃响应法是最常用的系统辨识方法。操作步骤非常简单：在系统稳定运行后，突然改变控制器的输出（例如将阀门开度从 30% 阶跃到 50%），然后记录过程变量的变化曲线。通过分析这条曲线，可以提取出 FOPDT（一阶加纯滞后）模型的三个关键参数。在 SonnetDB 中，可以轻松地通过 SQL 查询来收集阶跃响应数据。

```sql
-- 收集阶跃响应数据
SELECT 
    time, 
    valve_position AS controller_output,
    temperature AS process_variable
FROM reactor_data
WHERE reactor_id = 'R-101'
    AND time BETWEEN '2025-06-01 10:00:00' AND '2025-06-01 11:00:00'
ORDER BY time;
```

### 从响应曲线提取 FOPDT 参数

要从阶跃响应曲线中提取 FOPDT 参数，需要确定三个值：过程增益 K（稳态变化量与阶跃幅值之比）、时间常数 T（达到 63.2% 稳态变化所需的时间）和纯滞后时间 L（从阶跃开始到过程变量首次明显变化的时间）。工程中常用切线法来估算这些参数。一旦获得了 K、T、L 值，就可以直接输入 `pid_estimate()` 进行计算。

```sql
-- 根据实测的 K=3.2, T=45s, L=3.5s 进行参数估算
SELECT 
    pid_estimate('ziegler-nichols', 
        json_object('K', 3.2, 'T', 45.0, 'L', 3.5)
    ) AS zn_params,
    pid_estimate('cohen-coon', 
        json_object('K', 3.2, 'T', 45.0, 'L', 3.5)
    ) AS cc_params,
    pid_estimate('imc', 
        json_object('K', 3.2, 'T', 45.0, 'L', 3.5),
        json_object('lambda', 15.0)
    ) AS imc_params;
```

### 调参结果 JSON 解读

`pid_estimate()` 返回的 JSON 结果包含了完整的参数信息。以下是一个典型的输出示例：

```json
{
  "Kp": 2.85,
  "Ki": 0.19,
  "Kd": 0.42,
  "method": "ziegler-nichols",
  "K": 3.2,
  "T": 45.0,
  "L": 3.5
}
```

解读这些参数的关键在于理解每个值的物理含义。Kp=2.85 意味着每 1°C 的误差会产生 2.85% 的调节输出；Ki=0.19 表示积分项每秒会累积 0.19% 的额外调节量；Kd=0.42 则根据误差变化率产生阻尼作用。将估算结果代入 `pid_series()` 或 `pid()` 函数进行验证，观察实际控制效果，再根据需要进行微调。

### 反复迭代的调参流程

实际的 PID 调参是一个迭代过程。建议的工作流程是：(1) 执行阶跃响应实验并采集数据，(2) 使用 `pid_estimate()` 获取初始参数，(3) 将参数代入 `pid_series()` 进行仿真验证，(4) 对比不同方法的估算结果，(5) 选择控制效果最好的组合在实际系统上进行测试。每次测试都可以通过 SQL 查询记录下控制效果，形成调参日志用于后续分析。
