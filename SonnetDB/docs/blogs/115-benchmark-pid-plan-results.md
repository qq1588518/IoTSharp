## PID 基准通稿：控制律、时间桶控制与自动整定的 SQL 性能

PID 是 SonnetDB 面向工业控制场景的特色函数族。与传统数据库只存储过程变量不同，SonnetDB 可以直接在 SQL 中执行控制律回测、时间桶控制输出和阶跃响应自动整定。

### 通稿

本轮 PID 基准使用 50,000 个反应器阶跃响应点，对 `pid_series`、`pid` 和 `pid_estimate` 做端到端 SQL 性能测试。它模拟三类真实工作流：历史回测逐点生成控制量、仪表盘按分钟观察控制输出、离线根据阶跃响应估算 Kp/Ki/Kd。

PID 函数是 SonnetDB 的内置能力。SQLite、InfluxDB、TDengine、IoTDB、TimescaleDB 默认没有等价内置 PID 语义，因此本篇先报告 SonnetDB 自身能力；横向对比需另行定义“客户端计算”或“UDF/扩展函数”方案，否则语义并不公平。

### 对比方案

| 项 | 方案 |
| --- | --- |
| 数据模型 | `reactor(device TAG, temperature FIELD FLOAT)` |
| 数据规模 | 50,000 点，每 1,000 ms 1 点 |
| 数据形态 | 稳态 baseline + 一阶惯性阶跃响应 + 微小扰动 |
| 控制律 | `pid_series(temperature, 75.0, 0.6, 0.1, 0.05)` |
| 时间桶 | `pid(temperature, 75.0, 0.6, 0.1, 0.05) GROUP BY time(60000ms)` |
| 自动整定 | `pid_estimate(temperature, 'zn'/'imc', 1.0, 0.1, 0.1, ...)` |

运行命令：

```powershell
dotnet run -c Release --project tests/SonnetDB.Benchmarks -- --filter *Pid*
```

### 对比结果

| 方法 | 平均耗时 | 分配 | 输出 | 备注 |
| --- | ---: | ---: | ---: | --- |
| SonnetDB `pid_series(50k)` | 25.27 ms | 26.16 MB | 50k 行 | 逐点控制输出 |
| SonnetDB `pid(50k, 60000ms buckets)` | 10.72 ms | 11.35 MB | ~834 桶 | 桶内最后控制量 |
| SonnetDB `pid_estimate ZN(50k)` | 11.03 ms | 13.06 MB | 1 行 JSON | Ziegler-Nichols |
| SonnetDB `pid_estimate IMC(50k)` | 11.01 ms | 13.06 MB | 1 行 JSON | IMC/SIMC |

### 结论口径

PID 报告重点是“数据库内完成控制分析闭环”，而不是把其他数据库缺失的内置函数强行算作性能落后。若后续要做横向性能对比，应分成两类：数据库内 UDF 方案和客户端批量读取后计算方案。
