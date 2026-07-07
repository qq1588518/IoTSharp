## 深入理解 SonnetDB 的差分类窗口函数：derivative / non_negative_derivative / rate / irate

在时序数据监控场景中，原始计数器值往往本身并不直观——我们真正关心的是变化速率。SonnetDB 提供了四组差分类窗口函数，帮助用户从原始数据中提取有意义的趋势信息。

### derivative() 与 non_negative_derivative()

`derivative(field [, unit])` 计算当前行与上一行之间的数值差，并除以时间间隔，得到单位时间内的变化率。默认时间单位为 1 秒，可通过 duration 字面量自定义：

```sql
SELECT time, derivative(cpu_usage, 1s) AS rate_per_sec
FROM cpu
WHERE host = 'server-01';
```

`non_negative_derivative(field [, unit])` 在 `derivative` 的基础上增加了非负约束：当差值出现负数时（例如计数器重置），输出 `null` 而非负值，这在监控计数器场景中尤为实用。

### rate() 与 irate()

SonnetDB 的 `rate(field [, unit])` 在行级窗口语义下等价于 `non_negative_derivative`——它计算每相邻两点的变化率，忽略计数器重置导致的负值。这在 PromQL 迁移场景中非常自然。

`irate(field [, unit])` 在行级窗口内同样基于相邻两点计算，与 `rate` 行为一致。PromQL 中 `irate` 取区间内最后两个样本，而行级窗口天然就是逐点计算，因此两者在当前实现中等价。

### 核心差异对比

| 函数 | 时间归一化 | 负值处理 | 适用场景 |
|------|-----------|---------|---------|
| `difference(field)` | 否（直接差值） | 保留负值 | 简单增量计算 |
| `increase(field)` | 否 | 负值输出 null | 计数器增量监控 |
| `derivative(field)` | 是（默认/秒） | 保留负值 | 通用变化率 |
| `non_negative_derivative(field)` | 是 | 负值输出 null | CPU/网络监控 |
| `rate(field)` | 是 | 负值输出 null | PromQL 兼容 |
| `irate(field)` | 是 | 负值输出 null | PromQL 即时速率 |

### 实用示例

以下 SQL 展示了如何通过 `rate` 结合时间单位参数计算网络流量的每秒速率：

```sql
SELECT time, rate(network_bytes, 1s) AS bytes_per_sec
FROM network
WHERE host = 'gateway-01'
  AND time > NOW() - 1h;
```

使用 `derivative` 观察温度变化趋势，负值表示降温：

```sql
SELECT time, derivative(temperature, 1s) AS temp_change_per_sec
FROM sensors
WHERE device = 'thermo-05';
```

SonnetDB 的差分类窗口函数为监控和可观测性场景提供了基础但强大的分析能力，能够轻松实现从 PromQL 到 SQL 的平滑迁移。
