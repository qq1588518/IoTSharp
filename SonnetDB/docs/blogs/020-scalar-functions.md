## 标量函数：abs、round、sqrt、log 与 coalesce

标量函数是 SQL 查询中处理单个数据值的基本工具。SonnetDB 内置了丰富的数学和数据处理标量函数，让你在查询时就能完成数据清洗、转换和格式化。本文介绍五个最常用的标量函数。

### ABS：绝对值

`ABS(x)` 返回数值 `x` 的绝对值。在偏差分析、误差计算和异常检测中非常常见：

```sql
-- 计算温度与设定值的绝对偏差
SELECT ts, sensor_id, temperature,
       temperature - 25.0 AS deviation,
       ABS(temperature - 25.0) AS abs_deviation
FROM hvac_sensors
WHERE ts >= '2025-06-01'
ORDER BY abs_deviation DESC
LIMIT 20;
```

```sql
-- 计算流量的绝对变化量
SELECT ts, sensor_id,
       flow_rate,
       ABS(flow_rate - LAG(flow_rate) OVER (ORDER BY ts)) AS surge
FROM pipeline_sensors
WHERE ts >= '2025-06-01'
ORDER BY ts;
```

### ROUND：四舍五入

`ROUND(x, d)` 将数值 `x` 四舍五入到 `d` 位小数。当原始数据精度过高而展示不需要时，可以使用 ROUND 精简输出：

```sql
-- 将温度四舍五入到 1 位小数，湿度四舍五入到整数
SELECT ts, sensor_id,
       ROUND(temperature, 1) AS temp_rounded,
       ROUND(humidity, 0) AS humidity_rounded
FROM weather_data
WHERE station_id = 'bj-101'
  AND ts >= '2025-06-01'
ORDER BY ts
LIMIT 10;
```

```sql
-- 保留 2 位小数的精确值展示
SELECT ts, device_id,
       ROUND(voltage, 2) AS v,
       ROUND(current, 3) AS i,
       ROUND(voltage * current, 2) AS power
FROM power_monitor
WHERE ts >= '2025-06-01'
ORDER BY ts;
```

如果不指定 `d` 参数，`ROUND(x)` 默认四舍五入到整数。

### SQRT：平方根

`SQRT(x)` 返回 `x` 的平方根，适用于计算标准差、欧氏距离和物理量推导：

```sql
-- 计算电压和电流的有效值（RMS）
SELECT ts, meter_id,
       SQRT(voltage_squared) AS voltage_rms,
       SQRT(current_squared) AS current_rms
FROM electrical_meter
WHERE ts >= '2025-06-01'
ORDER BY ts;
```

```sql
-- 计算振动加速度的合成幅值
SELECT ts, sensor_id,
       SQRT(x_accel * x_accel + y_accel * y_accel + z_accel * z_accel) AS vibration_magnitude
FROM vibration_sensors
WHERE ts >= '2025-06-01'
ORDER BY ts;
```

注意：`SQRT` 的输入值不能为负数，否则会返回 NULL。

### LOG：自然对数与常用对数

`LOG(x)` 返回 `x` 的自然对数（以 e 为底）。`LOG(base, x)` 返回以 `base` 为底的对数。对数变换在压缩大数值范围和计算增长速率时非常有用：

```sql
-- 对网络延迟数据做对数变换，压缩异常值的视觉范围
SELECT ts, device_id,
       latency_ms,
       LOG(latency_ms) AS log_latency
FROM network_metrics
WHERE ts >= '2025-06-01'
  AND latency_ms > 0
ORDER BY ts;
```

```sql
-- 使用常用对数（以 10 为底）分析数据量级
SELECT ts, device_id,
       bytes_transferred,
       LOG(10, bytes_transferred) AS magnitude
FROM traffic_logs
WHERE bytes_transferred > 0
  AND ts >= '2025-06-01'
ORDER BY ts;
```

`LOG` 的参数必须大于 0，否则返回 NULL。

### COALESCE：空值处理

`COALESCE(val1, val2, ..., default)` 返回参数列表中第一个非 NULL 的值。当数据中存在空值时，COALESCE 是最常用的处理函数：

```sql
-- 如果 humidity 为空，使用备用传感器值；仍为空则使用默认值
SELECT ts, sensor_id, temperature,
       COALESCE(humidity, backup_humidity, 50.0) AS humidity_filled
FROM sensor_data
WHERE ts >= '2025-06-01'
ORDER BY ts;
```

```sql
-- 在计算中处理可能的 NULL 值，避免整个表达式结果为 NULL
SELECT ts, device_id,
       COALESCE(temperature, 0.0) AS temp,
       COALESCE(humidity, 0.0) AS hum,
       COALESCE(temperature, 0.0) * COALESCE(humidity, 0.0) AS product
FROM weather_station
WHERE ts >= '2025-06-01'
ORDER BY ts;
```

### 函数组合使用

标量函数可以相互嵌套组合，实现更复杂的变换：

```sql
-- 组合使用：计算并格式化振动指标
SELECT ts, sensor_id,
       ROUND(LOG(SQRT(x*x + y*y + z*z)), 2) AS vibration_score
FROM accelerometer
WHERE ts >= '2025-06-01'
ORDER BY ts;
```

```sql
-- 处理可能的负值或零值，确保计算安全
SELECT ts, meter_id,
       COALESCE(ROUND(LOG(ABS(power) + 1), 2), 0) AS log_power
FROM electrical_meter
WHERE ts >= '2025-06-01'
ORDER BY ts;
```

### 函数选择指南

| 函数 | 最佳用途 | 注意 |
|------|----------|------|
| ABS | 偏差分析、误差计算 | 无限制 |
| ROUND | 数据格式化、精度控制 | 第二个参数可选 |
| SQRT | RMS 计算、距离计算 | 输入必须 >= 0 |
| LOG | 量级压缩、非线性变换 | 输入必须 > 0 |
| COALESCE | 空值填充、默认值 | 参数数量可变 |

掌握这些标量函数，可以在 SonnetDB 的查询中完成大部分常见的数据预处理工作，减少应用层代码的复杂度。
