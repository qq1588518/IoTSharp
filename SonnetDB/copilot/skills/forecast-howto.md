---
name: forecast-howto
description: 使用 SonnetDB 内置 forecast() 表值函数做短期预测与置信区间估计，以及 anomaly() / changepoint() 联动做"预测带异常检测"。
triggers:
  - forecast
  - 预测
  - holt-winters
  - holt_winters
  - 趋势预测
  - 线性预测
  - 外推
  - 置信区间
  - 时序预测
  - 短期预测
requires_tools:
  - query_sql
  - describe_measurement
---

# 时序预测快速入门

SonnetDB 内置 `forecast()` 表值函数（TVF），支持线性外推和 Holt-Winters 指数平滑，零依赖，直接在 SQL 中调用。

---

## 1. 函数签名

```sql
SELECT *
FROM forecast(<measurement>, <field>, <horizon>, '<algo>' [, <season>])
WHERE <tag_filter>;
```

| 参数 | 类型 | 说明 |
| --- | --- | --- |
| `measurement` | 标识符 | 待预测的 measurement 名 |
| `field` | 标识符 | 数值型 FIELD 列（FLOAT 或 INT） |
| `horizon` | 整数 | 向后外推步数（步长 = 历史平均采样间隔） |
| `algo` | 字符串 | `'linear'` 或 `'holt_winters'`/`'hw'` |
| `season` | 整数（可选） | 季节周期（采样点数），仅 holt_winters 使用；省略或 0 退化为 Holt 双重平滑 |

**返回列：**

| 列 | 说明 |
| --- | --- |
| `time` | 预测时间点（从历史末尾向后递增） |
| `value` | 预测均值 |
| `lower` | 95% 置信区间下界 |
| `upper` | 95% 置信区间上界 |
| `<tag列>` | 原始 tag 维度列 |

---

## 2. 算法选择

| 场景 | 推荐算法 | 原因 |
| --- | --- | --- |
| 单调上升/下降趋势，无周期性 | `'linear'` | 最小二乘拟合，简单可靠 |
| 有日/周/月季节性波动 | `'holt_winters'` | 三次指数平滑，捕捉趋势+季节 |
| 数据量少（< 20 点）或快速基线 | `'linear'` | Holt-Winters 需要 ≥ 2 个完整季节 |

---

## 3. 示例

### 线性外推（未来 5 步）

```sql
-- 预测 web-01 的 CPU 使用率未来 5 个采样点
SELECT time, value, lower, upper
FROM forecast(host_cpu, cpu_pct, 5, 'linear')
WHERE host = 'web-01';
```

### Holt-Winters 季节预测

```sql
-- 预测温度传感器未来 24 步（季节周期 = 24，即一天 24 小时）
-- 历史数据需要至少 2 × 24 = 48 个采样点
SELECT *
FROM forecast(sensor_climate, temp_celsius, 24, 'holt_winters', 24)
WHERE sensor_id = 'S-A01';
```

### season 参数参考

| 采样间隔 | 日周期 season | 周周期 season |
| --- | --- | --- |
| 1 分钟 | 1440 | 10080 |
| 5 分钟 | 288 | 2016 |
| 1 小时 | 24 | 168 |
| 1 天 | — | 7 |

---

## 4. 数据准备要求

```text
✅ Holt-Winters 至少需要 2 × season 个历史采样点
✅ 时间戳严格递增，field 列为 FLOAT 或 INT
⚠️  horizon 不要超过历史长度的 1/3（误差快速放大）
⚠️  数据稀疏时先做聚合降采样，再传入 forecast
```

---

## 5. 反模式

```sql
-- ❌ 数据太少就用 holt_winters
SELECT * FROM forecast(cpu, usage, 24, 'holt_winters', 24)
WHERE host = 'new-server';  -- 新服务器只有 10 条数据，改用 'linear'

-- ❌ horizon 设置过大（超过历史点数的 1/3）
SELECT * FROM forecast(cpu, usage, 1000, 'linear') WHERE host = 'web-01';
```

---

## 6. 与其他函数的关系

| 函数 | 形态 | 用途 |
| --- | --- | --- |
| `forecast()` | TVF（FROM 子句） | 向未来外推预测值 |
| `anomaly()` | 窗口函数（SELECT 列） | 检测历史数据中的异常点 |
| `changepoint()` | 窗口函数（SELECT 列） | 检测历史数据中的趋势突变 |

> 详见技能 **`anomaly-detect`**、**`changepoint-detect`**。
