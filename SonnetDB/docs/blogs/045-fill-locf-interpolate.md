## SonnetDB 缺失值处理三剑客：fill() / locf() / interpolate()

现实世界中的时序数据往往不完整——网络抖动导致采集点丢失、传感器故障产生空值、数据源暂未就绪造成缺口。SonnetDB 提供了三种缺失值处理函数，帮助用户在不同场景下合理填充数据缺口。

### fill() 常数填充

`fill(field, value)` 是最直接的缺失值处理方式：遇到空值时，用指定的常数替换：

```sql
SELECT time, temperature,
       fill(temperature, 0) AS temp_filled
FROM sensors
WHERE device_id = 'sensor-12'
ORDER BY time;
```

常数填充适合那些**缺失本身也有意义**的场景，例如计数器的缺失可以合理视为 0。但需注意，对温度这类连续值用 0 填充可能引入显著偏差。

### locf() 向前填充

`locf(field)`（Last Observation Carried Forward）使用最近一个非空值填充后续的空值。这是时序数据处理中最常用的填充策略之一：

```sql
SELECT time, stock_price,
       locf(stock_price) AS filled_price
FROM market_data
WHERE symbol = 'AAPL'
ORDER BY time;
```

`locf` 的默认行为是：如果缺失值出现在首段（还没有任何非空观测），则输出 `null`。这避免了在没有基线时盲目填充。

### interpolate() 线性插值

`interpolate(field)` 在相邻两个非空采样点之间进行线性插值。对于位置 `i` 的缺失值，其插值结果为 `v0 + (t_i - t0) / (t1 - t0) * (v1 - v0)`：

```sql
SELECT time, cpu_temperature,
       interpolate(cpu_temperature) AS temp_interp
FROM server_metrics
WHERE host = 'db-01'
ORDER BY time;
```

线性插值的上下游均需要有效的锚点值。首段缺失（左侧无锚点）或末段缺失（右侧无锚点）均输出 `null`。

### 三种方法的对比与选择

| 方法 | 公式 | 适用场景 | 注意事项 |
|------|------|---------|---------|
| `fill(value)` | 固定常数 | 计数器归零、状态位缺失 | 可能引入偏差 |
| `locf()` | 最近值复制 | 缓慢变化量、离散状态 | 首段缺失仍为 null |
| `interpolate()` | 时间加权线性 | 连续变化量（温度、压力） | 首末段无法插值 |

### 组合使用示例

以下 SQL 展示了在实际分析管道中组合使用三种方法：

```sql
SELECT time,
       cpu_usage,
       fill(cpu_usage, 0) AS fill_zero,
       locf(cpu_usage) AS locf_filled,
       interpolate(cpu_usage) AS interp_filled
FROM cpu
WHERE host = 'web-02'
  AND time > NOW() - 1h;
```

在实际的数据预处理流水线中，推荐先尝试 `interpolate()` 处理连续值字段，对离散状态字段使用 `locf()`，仅在对缺失值的业务含义有明确预期时使用 `fill()`。选择合适的填充策略对后续的聚合分析和异常检测至关重要。
