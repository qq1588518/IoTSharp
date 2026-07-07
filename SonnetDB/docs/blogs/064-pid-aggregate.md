## PID 聚合函数：pid() 与 GROUP BY 时间窗口

在工业控制场景中，并非所有 PID 计算都需要逐点输出。很多时候，工程师更关心的是在一个时间窗口内的整体控制效果——例如，过去 5 分钟的平均控制输出是多少？最大超调量是多少？SonnetDB 的 `pid()` 聚合函数正是为这种场景而设计，它允许用户通过 `GROUP BY time` 对时间窗口内的数据进行批量的 PID 计算。

### pid() 与 pid_series() 的区别

`pid_series()` 是标量函数，为每一行数据计算一个对应的 PID 输出值，适合实时流式控制场景。而 `pid()` 是聚合函数，它对整个分组内的数据执行完整的 PID 计算后，返回该窗口的聚合统计信息。两者在计算模型上本质相同（都是基于 Kp/Ki/Kd 的经典 PID 算法），但在使用场景和输出形式上有所区别。

```sql
-- 使用 pid() 聚合计算每小时的 PID 输出统计
SELECT 
    date_trunc('hour', time) AS hour,
    device_id,
    pid(100.0, temperature, time, 2.0, 0.5, 0.1) AS hourly_pid_stats
FROM furnace_data
WHERE time >= now() - INTERVAL '7 days'
GROUP BY hour, device_id;
```

`pid()` 函数的返回值是一个 JSON 对象，包含了丰富的诊断信息：`output`（最终输出值）、`p_term`（比例项贡献）、`i_term`（积分项贡献）、`d_term`（微分项贡献）、`error`（最终误差）、`integral`（积分累积值）等。这使得开发者可以在不用额外代码的情况下，深入分析每个时间窗口内的控制行为。

### 批量调优场景

在控制系统调试阶段，`pid()` 聚合函数的价值尤为突出。工程师可以快速对比不同参数配置下的控制效果，识别出最优的 Kp/Ki/Kd 组合。通过 `GROUP BY` 不同的参数组合和时间窗口，可以一次性完成大量的调优分析。

```sql
-- 对比不同 PID 参数的控制效果
SELECT 
    'Kp=2.0_Ki=0.5_Kd=0.1' AS param_set,
    pid(100.0, temperature, time, 2.0, 0.5, 0.1) AS result
FROM furnace_data
WHERE time >= now() - INTERVAL '1 hour'
UNION ALL
SELECT 
    'Kp=1.5_Ki=0.3_Kd=0.2' AS param_set,
    pid(100.0, temperature, time, 1.5, 0.3, 0.2) AS result
FROM furnace_data
WHERE time >= now() - INTERVAL '1 hour';
```

### 性能优势

与逐行调用 `pid_series()` 后再进行聚合相比，`pid()` 函数在内部对计算流程进行了优化，减少了重复计算和中间结果的产生，在处理大规模数据集时具有明显的性能优势。对于需要定期生成控制报告、进行批量性能分析的应用场景，`pid()` 是更高效的选择。结合 SonnetDB 的时序索引，即使对包含数亿条记录的数据集进行按小时聚合的 PID 计算，也能在秒级内完成。
