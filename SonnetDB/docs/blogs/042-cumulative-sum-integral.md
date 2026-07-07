## SonnetDB 累计与积分函数：cumulative_sum() 运行总和与 integral() 梯形面积

在许多时序分析场景中，我们不仅关心瞬时值，更关心值的累计效果。SonnetDB 提供了 `cumulative_sum()` 和 `integral()` 两个窗口函数，分别用于计算运行总和与梯形法积分面积。

### cumulative_sum() 运行累计和

`cumulative_sum(field)` 从分组内的第一行开始，逐行累加字段值，输出当前行及之前所有行的总和。缺失值不会重置累计和，而是继续输出最近的累计值：

```sql
SELECT time, messages_received,
       cumulative_sum(messages_received) AS total_received
FROM queue_metrics
WHERE queue = 'orders'
ORDER BY time;
```

这个函数特别适合计算累计增长量、用户注册总数、销售额累计等场景。配合 `time` 分组可以轻松得到每日累计值：

```sql
SELECT time, cumulative_sum(revenue) AS running_revenue
FROM finance
WHERE project = 'alpha'
  AND time >= '2025-01-01' AND time < '2025-02-01';
```

### integral() 梯形法积分

`integral(field [, unit])` 采用梯形法（Trapezoidal Rule）计算相邻两点之间的曲线下面积并累加。对于每对相邻点 `(t0, v0)` 和 `(t1, v1)`，其贡献的面积为 `0.5 * (v0 + v1) * (t1 - t0)`。默认时间单位为 1 秒，可通过 duration 参数自定义：

```sql
SELECT time, power_kw,
       integral(power_kw, 1h) AS energy_kwh
FROM power_meter
WHERE device = 'meter-03';
```

上述查询将功率（千瓦）对时间积分，得到千瓦时（kWh）——即消耗的电能。时间单位参数 `1h` 使得积分结果直接以小时为单位，无需额外换算。

### 积分与累计和的选择建议

两者虽然都是累加操作，但适用场景截然不同：

- **累计和**适合**等间距采样**的值累计，例如每天的订单数累计总和，不关心时间间隔
- **积分**适合**变间距采样**且值代表**速率/密度**的场景，例如功率对时间积分得到能量

### 完整示例

以下 SQL 展示了如何配合使用两个函数来分析电池充放电数据：

```sql
SELECT time,
       current_a,
       cumulative_sum(current_a) AS sum_charge,
       integral(current_a, 1s) AS total_charge
FROM battery
WHERE battery_id = 'bat-07'
  AND time BETWEEN 1713600000000 AND 1713686400000;
```

第一列输出瞬时电流，第二列反映电流的简单累计（适合等间隔点），第三列通过梯形积分得到准确的总电荷量（库仑）。通过合理选择累计方式，用户可以更加准确地从时序数据中提取有物理意义的汇总指标。
