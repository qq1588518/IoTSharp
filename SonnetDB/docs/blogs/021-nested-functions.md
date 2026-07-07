## 函数嵌套调用：构建复杂的计算表达式

在时序数据分析中，单一标量函数往往不足以表达复杂的业务逻辑。SonnetDB 支持函数嵌套调用，允许你将多个标量函数组合成复合表达式，从而在查询中直接完成多步计算，无需在应用层做二次处理。

### 函数嵌套的基本模式

函数嵌套的核心思想是将一个函数的返回值作为另一个函数的输入参数。SonnetDB 的标量函数遵循标准的 SQL 函数调用语法，因此嵌套非常自然。以下是一个典型场景：计算 CPU 使用率的偏差绝对值并保留两位小数。

```sql
SELECT time, round(abs(usage - 0.5), 2) AS rounded_deviation
FROM cpu
WHERE host = 'server-01';
```

这条查询的内部执行顺序是：先计算 `usage - 0.5`，然后对其取绝对值 `abs(...)`，最后将结果四舍五入到两位小数 `round(..., 2)`。整个过程在数据库内完成，避免将原始数据拉取到应用层再计算。

### 实用组合模式

除了上面的绝对值取整模式，SonnetDB 还支持多种实用的函数组合：

```sql
SELECT
    -- 归一化后缩放：先归一化到 [0,1] 再映射到百分比
    round((usage - min_usage) / (max_usage - min_usage) * 100, 1) AS normalized_pct,
    -- 对数变换后取整：处理长尾分布数据
    round(log(cores + 1, 2), 0) AS log2_cores,
    -- 空值兜底后取绝对值：安全处理可能为 NULL 的差值
    abs(coalesce(temperature - setpoint, 0)) AS abs_error,
    -- 多层运算：开方再取整
    round(sqrt(usage * 100), 0) AS score
FROM cpu
WHERE host = 'server-01';
```

### 执行顺序注意事项

函数嵌套的执行顺序是从内向外逐层展开的。最内层的函数最先执行，其输出作为外层函数的输入。理解这一顺序对于编写正确的嵌套表达式至关重要。例如，`round(abs(usage - 0.5), 2)` 中，`usage - 0.5` 先执行，`abs()` 再执行，最后 `round()` 执行。

需要注意的是，如果内层函数返回了 `NULL`（例如 `coalesce` 也没有找到非空值时），外层函数会将 `NULL` 传递下去。大部分标量函数对 `NULL` 输入返回 `NULL`，因此在使用嵌套函数时，建议结合 `coalesce` 做空值保护。

### 与聚合函数组合

嵌套调用不仅限于标量函数之间，也可以与聚合函数配合使用：

```sql
SELECT
    round(avg(usage), 4) AS avg_usage,
    round(stddev(usage), 4) AS std_usage,
    round(abs(min(usage) - max(usage)), 2) AS range
FROM cpu
WHERE host = 'server-01';
```

函数嵌套让 SQL 查询的表达能力大幅提升，减少了应用层的计算负担，是 SonnetDB SQL 实用技巧中的重要一环。
