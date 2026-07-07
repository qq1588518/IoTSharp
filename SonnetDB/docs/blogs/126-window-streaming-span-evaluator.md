# 窗口函数执行器：从 object 数组走向 typed streaming

SonnetDB 的窗口函数越来越多：moving_average、ewma、holt_winters、running_sum、running_min、running_max，以及 PID、异常检测等高级函数。功能丰富之后，执行器的分配模型就变得重要。

## object?[] 的问题

旧接口 `IWindowEvaluator.Compute(long[], FieldValue?[]) -> object?[]` 很通用，但也带来成本：

- 输入需要先 materialize 全部 `long[]` 和 `FieldValue?[]`
- 数值输出提前装箱
- 每个窗口函数都倾向于分配完整输出数组

对于大数据集，这些分配会压住 GC，也让流式处理无法自然展开。

## typed evaluator

新的 typed evaluator 至少优先覆盖 double 路径。`SelectExecutor` 如果发现窗口函数支持 typed 输出，就可以复用 `double[] + bool[]` 表示数值和 null bitmap，避免提前把每行结果装箱。

首批迁移的函数包括：

- `moving_average`
- `ewma`
- `holt_winters`
- `running_sum`

SQL 输出保持兼容。typed 路径只是内部表示更紧凑。

## 流式状态接口

新的状态接口形如 `IWindowState.Update(timestamp, value) -> output`。这让窗口函数可以按 row/chunk 推进，而不是先收集全量时间戳和值数组。

旧接口没有删除，而是保留适配层。这样迁移可以逐步进行：已支持 streaming 的函数走新路径，未迁移函数继续走旧 materialized 路径。

## Span 批量路径

`moving_average`、`running_sum`、`running_min`、`running_max` 的内部实现改为 `ReadOnlySpan` / `Span` 处理。小窗口 moving average 还可以使用栈上环形缓冲，减少临时数组。

语义保持不变：

- 前 `n-1` 行仍输出 null
- 缺失值处理保持旧规则
- 时间顺序语义不变

窗口函数优化的目标不是改变 SQL，而是让相同 SQL 在更大的数据集上更稳、更少分配。
