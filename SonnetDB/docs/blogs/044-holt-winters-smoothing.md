## SonnetDB 的 Holt-Winters 平滑：holt_winters() 双指数平滑与趋势提取

在时序数据分析中，识别和分离趋势成分是预测和异常检测的基础。SonnetDB 提供了 `holt_winters(field, alpha, beta)` 窗口函数，实现了 Holt 加法双指数平滑，能够从数据中同时提取**水平（level）**和**趋势（trend）**两个分量。

### 算法原理

Holt 双指数平滑在简单指数平滑的基础上增加了趋势分量的递推。其核心更新公式如下：

- 水平更新：`level_t = alpha * x_t + (1 - alpha) * (level_{t-1} + trend_{t-1})`
- 趋势更新：`trend_t = beta * (level_t - level_{t-1}) + (1 - beta) * trend_{t-1}`
- 拟合值：`fit_t = level_t + trend_t`

其中 `alpha` 控制水平分量的平滑速度，`beta` 控制趋势分量的平滑速度，两者取值范围均为 `(0, 1]`。首行直接输出 `x_t` 自身；第二行使用初始趋势 `x_t - x_{t-1}`；第三行起开始完整的递推。

### 参数调优指南

选择 `alpha` 和 `beta` 是 Holt-Winters 平滑的核心：

```sql
SELECT time, daily_active_users,
       holt_winters(daily_active_users, 0.5, 0.1) AS smooth_dau
FROM product_analytics
WHERE product = 'app-x'
ORDER BY time;
```

- `alpha = 0.5, beta = 0.1`：水平更新较快，趋势更新较慢，适合用户活跃度这种短期波动大但长期趋势稳定的数据
- `alpha = 0.2, beta = 0.3`：水平平滑力度大，趋势响应快，适合有明显上升/下降趋势的数据
- `alpha = 0.8, beta = 0.01`：几乎不对水平做平滑，但趋势几乎不变，适合跟踪噪声数据中的微弱趋势

### 趋势提取与可视化

Holt-Winters 的输出是 `level + trend`，即每行的拟合值。通过对比原始值与拟合值，可以直观地看到去噪后的趋势线：

```sql
SELECT time, page_views,
       holt_winters(page_views, 0.3, 0.2) AS trend
FROM web_analytics
WHERE site = 'homepage'
  AND time > NOW() - 7d;
```

在实际应用中，可以使用拟合值与实际值的残差来做异常检测：当 `|actual - fitted|` 超过阈值时触发告警。

### 与 TVF forecast() 的关系

SonnetDB 还提供了表值函数 `forecast(measurement, field, horizon, 'holt_winters')`，用于基于 Holt-Winters 算法预测未来的数据点。窗口函数 `holt_winters()` 侧重于对历史数据的**拟合**（平滑），而 TVF `forecast()` 侧重于向未来**外推**预测值。两者结合可以实现完整的"平滑→趋势识别→预测"分析流水线。
