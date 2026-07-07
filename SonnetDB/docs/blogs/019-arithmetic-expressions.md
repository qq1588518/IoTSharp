## 算术表达式：在投影列中灵活计算数据

在时序数据分析中，原始数据往往需要经过计算才能产生有意义的指标。SonnetDB 支持在 SELECT 投影列中使用完整的算术表达式，包括四则运算、一元运算和括号组合，让你在查询时就完成数据变换。

### 基本四则运算

SonnetDB 支持加（`+`）、减（`-`）、乘（`*`）、除（`/`）四种基本运算符，可以在 SELECT 子句中直接对字段进行计算：

```sql
-- 将温度从摄氏度转换为华氏度
SELECT ts, sensor_id,
       temperature * 9.0 / 5.0 + 32.0 AS temp_fahrenheit
FROM sensor_data
WHERE sensor_id = 'sensor-001'
  AND ts >= '2025-06-01'
ORDER BY ts;
```

```sql
-- 计算能耗：功率（kW）乘以时间间隔（小时）得到 kWh
SELECT ts, sensor_id,
       power_kw * 0.5 AS energy_kwh
FROM power_meter
WHERE ts >= '2025-06-01T00:00:00Z'
  AND ts < '2025-06-02T00:00:00Z'
ORDER BY ts;
```

### 单位换算应用

单位换算是时序查询中常见的场景。通过算术表达式，可以在查询时直接将数据转换为目标单位：

```sql
-- 将内存使用量从字节转换为 GB
SELECT ts, host_id,
       mem_used_bytes / 1073741824.0 AS mem_used_gb,
       mem_total_bytes / 1073741824.0 AS mem_total_gb,
       (mem_used_bytes * 100.0) / mem_total_bytes AS mem_usage_pct
FROM system_metrics
WHERE host_id = 'web-server-01'
  AND ts >= '2025-06-01'
ORDER BY ts;
```

注意，这里 `mem_used_bytes * 100.0` 使用了浮点数乘法，确保结果是一个浮点数而不是整数除法。

### 一元负号运算

SonnetDB 支持一元负号（`-`）对数值取反。这在处理方向性数据或偏差值时非常有用：

```sql
-- 查询加速度计的原始数据，并将 Z 轴取反以校正安装方向
SELECT ts, sensor_id,
       x_accel,
       y_accel,
       -z_accel AS z_accel_corrected
FROM accelerometer_data
WHERE device_id = 'imu-01'
  AND ts >= '2025-06-01'
ORDER BY ts;
```

```sql
-- 计算净流量：将负值（流出）累加取反为正值
SELECT ts, meter_id,
       -flow_rate AS outflow_rate
FROM water_meter
WHERE flow_rate < 0
  AND ts >= '2025-06-01'
ORDER BY ts;
```

### 复杂组合表达式

运算符可以自由组合，并用括号控制优先级：

```sql
-- 计算电压和电流的乘积得到功率，并附加偏移校准
SELECT ts, meter_id,
       voltage * current AS raw_power,
       voltage * current * 1.02 + 0.5 AS calibrated_power
FROM electrical_meter
WHERE ts >= '2025-06-01T00:00:00Z'
  AND ts < '2025-06-02T00:00:00Z'
ORDER BY ts;
```

```sql
-- 计算体感温度（简化版）：温度 + 0.33 * 湿度 - 0.7 * 风速 - 4.0
SELECT ts, sensor_id,
       temperature + 0.33 * humidity - 0.7 * wind_speed - 4.0 AS feels_like
FROM weather_station
WHERE station_id = 'bj-101'
  AND ts >= '2025-06-01'
ORDER BY ts;
```

### 在过滤条件中使用计算

算术表达式也可以在 WHERE 子句中使用，实现条件过滤：

```sql
-- 筛选功率大于阈值（电压 * 电流 > 5000）的记录
SELECT ts, meter_id, voltage, current,
       voltage * current AS power
FROM electrical_meter
WHERE voltage * current > 5000
  AND ts >= '2025-06-01'
ORDER BY ts;
```

### 在 ORDER BY 中使用计算

排序时也可以使用表达式，这在排名计算中特别有用：

```sql
-- 按偏差绝对值排序，找出异常最大的设备
SELECT ts, device_id, temperature,
       temperature - 25.0 AS deviation
FROM temperature_sensors
WHERE ts >= '2025-06-01'
ORDER BY ABS(temperature - 25.0) DESC
LIMIT 10;
```

### 使用建议

1. **避免整数除法陷阱**：在 `DOUBLE` 类型的字段计算时，确保至少一个操作数是浮点数（如 `value * 100.0` 而不是 `value * 100`），以避免整数截断。
2. **使用别名**：始终为计算结果使用 `AS` 别名，让输出列名清晰可读。
3. **计算在查询端而非应用端**：将算术运算放在 SQL 中而不是在应用代码中处理，可以减少数据传输量和应用计算负担。

通过灵活运用算术表达式，你可以在 SonnetDB 的查询结果中直接获得经过计算的指标，大幅简化应用层的数据处理逻辑。
