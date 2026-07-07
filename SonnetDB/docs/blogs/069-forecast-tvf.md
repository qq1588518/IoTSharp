## 时序预测函数：forecast() 线性与 Holt-Winters 方法

时序预测是数据分析和决策支持中的核心需求。无论是预测服务器负载、商品销量，还是工业设备的能耗趋势，准确的预测都能为资源配置和业务规划提供有力的依据。SonnetDB 内置的 `forecast()` 函数支持线性和 Holt-Winters 两种预测方法，并提供了灵活的 `horizon` 参数和季节性配置，开发者可以通过简单的 SQL 查询完成复杂的预测分析。

### 线性预测方法

线性预测（Linear）是最基础的预测方法，适用于具有明显线性趋势的时间序列数据。它通过最小二乘法对历史数据进行线性回归拟合，并基于拟合的趋势线向外推预测值。当数据呈现稳定的上升或下降趋势时，线性方法简单高效，计算开销也很小。

```sql
-- 使用线性方法预测未来 24 小时的数据
SELECT 
    forecast(
        temperature, 
        time, 
        'linear',
        horizon => 24
    ) AS forecast_result
FROM sensor_data
WHERE device_id = 'sensor-01'
    AND time >= now() - INTERVAL '7 days';
```

`forecast()` 返回的 JSON 对象包含 `forecast`（预测值数组）、`lower_bound` 和 `upper_bound`（置信区间）、`method`（使用的方法）以及 `params`（模型参数）等字段，方便开发者直接在前端可视化中使用。

### Holt-Winters 方法

对于同时具有趋势和季节性的时间序列数据，Holt-Winters 指数平滑方法更为适用。它通过三个平滑方程（水平、趋势、季节性）来捕捉数据的多种模式。SonnetDB 的 Holt-Winters 实现支持自动的季节周期检测，也允许用户通过 `seasonal_period` 参数手动指定周期长度。此外，用户可以通过 `alpha`、`beta`、`gamma` 三个平滑参数来控制模型对不同分量的敏感度。

```sql
-- 使用 Holt-Winters 方法进行带季节性的预测
SELECT 
    forecast(
        cpu_usage, 
        time, 
        'holt-winters',
        horizon => 48,
        seasonal_period => 24,
        alpha => 0.3,
        beta => 0.1,
        gamma => 0.1
    ) AS forecast_result
FROM server_metrics
WHERE host = 'web-server-01'
    AND time >= now() - INTERVAL '30 days';
```

### 预测结果的应用

`forecast()` 的预测结果可以直接与原始数据进行对比分析。例如，结合 `unnest` 操作，可以将预测结果展开为行，与原始观测值进行对比。更常见的应用是将预测结果用于异常检测——当实际值显著偏离预测值的置信区间时，触发告警。

```sql
-- 检测实际值是否超出预测置信区间
WITH forecast_data AS (
    SELECT forecast(value, time, 'holt-winters', 
                   horizon => 12, seasonal_period => 24) AS f
    FROM metrics WHERE time >= now() - INTERVAL '7 days'
)
SELECT time, value,
    (f->>'upper_bound')::jsonb AS upper,
    (f->>'lower_bound')::jsonb AS lower
FROM raw_data, forecast_data
WHERE value > ((f->>'upper_bound')::jsonb->>(time::text))::float;
```

通过 `horizon` 参数控制预测步长，结合不同的预测方法和季节性配置，SonnetDB 的 `forecast()` 函数可以灵活应对从简单趋势预测到复杂季节性预测的各类场景，为企业决策提供数据驱动的洞察。
