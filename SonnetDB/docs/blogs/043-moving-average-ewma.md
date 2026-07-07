## SonnetDB 平滑函数解析：moving_average() 与 ewma() 降噪技术

原始时序数据往往包含随机噪声，直接使用会干扰趋势判断和异常检测。SonnetDB 提供了 `moving_average()` 和 `ewma()` 两种平滑函数，帮助用户从杂乱的数据中提取稳定的信号。

### moving_average() 简单滑动平均

`moving_average(field, n)` 计算最近 N 个有效数据点的算术平均值。窗口从前 N 个点填满后开始输出有效值，前 N-1 行输出 `null`。缺失值不参与窗口分母的计算：

```sql
SELECT time, cpu_usage,
       moving_average(cpu_usage, 5) AS smooth_5
FROM cpu
WHERE host = 'web-01'
ORDER BY time;
```

上述查询对 CPU 使用率做 5 点滑动平均，有效消除了秒级的瞬时抖动，展示出更平稳的 CPU 负载趋势。窗口大小 N 的选择需要权衡：太小的窗口平滑效果有限，太大的窗口则会引入明显的滞后。

### ewma() 指数加权移动平均

`ewma(field, alpha)` 使用指数加权移动平均（Exponentially Weighted Moving Average）进行平滑。其核心公式为 `s_t = alpha * x_t + (1 - alpha) * s_{t-1}`，其中 `alpha` 是平滑因子，取值范围 `(0, 1]`。`alpha` 越接近 1，当前值的权重越大，平滑程度越低；`alpha` 越接近 0，历史值的权重越大，曲线越平滑。

```sql
SELECT time, response_time_ms,
       ewma(response_time_ms, 0.3) AS ewma_smooth
FROM api_metrics
WHERE endpoint = '/api/orders'
ORDER BY time;
```

`ewma` 相比滑动平均的优势在于：它对每个新数据点只需要保存一个状态值（上一平滑结果），内存开销极低，且对近期数据赋予更高权重，反应更灵敏。

### 平滑函数对比

| 特性 | moving_average(n) | ewma(alpha) |
|------|-------------------|-------------|
| 权重分配 | 窗口内等权 | 指数衰减 |
| 参数含义 | 窗口大小 n | 平滑因子 alpha |
| 内存需求 | O(n) | O(1) |
| 滞后特性 | 固定窗口长度的滞后 | 可调衰减速度 |
| 适用场景 | 短周期平滑、可视化 | 实时监控、资源受限环境 |

### 组合使用示例

以下 SQL 展示了在同一查询中同时使用两种平滑方法，对比效果：

```sql
SELECT time, temperature,
       moving_average(temperature, 10) AS ma_10,
       ewma(temperature, 0.15) AS ewma_015
FROM hvac
WHERE zone = 'building-a'
  AND time > NOW() - 6h;
```

`moving_average` 适合对固定时间窗口做等权平均，而 `ewma` 则更适合需要实时响应且对历史数据权重递减的场景。在实际生产环境中，推荐先使用 `ewma` 做流式平滑，再配合 `anomaly` 函数进行异常检测，效果显著。
