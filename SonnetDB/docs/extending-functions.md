---
layout: default
title: 扩展 SQL 函数 (UDF)
permalink: /extending-functions/
---

# 扩展 SQL 函数（UDF）

SonnetDB 在 PR #56 中提供了 **用户自定义函数（UDF）注册 API**，
允许嵌入式宿主在不修改 SonnetDB 源码的前提下扩展 SQL 函数：

| 类别 | 注册方法 | SQL 形态 |
|------|---------|---------|
| 标量 | `Tsdb.Functions.RegisterScalar(name, evaluator)` | `SELECT my_fn(value) FROM ...` |
| 聚合 | `Tsdb.Functions.RegisterAggregate(IAggregateFunction)` | `SELECT my_agg(value) FROM ... GROUP BY time(...)` |
| 窗口 | `Tsdb.Functions.RegisterWindow(IWindowFunction)` | `SELECT my_win(value) FROM ...` |
| 表值 (TVF) | `Tsdb.Functions.RegisterTableValuedFunction(name, executor)` | `SELECT * FROM my_tvf(measurement, ...)` |

> **AOT / Server 模式**：`SonnetDB` 启动 `Tsdb` 时强制设置
> `TsdbOptions.AllowUserFunctions = false`，任何 `Register*` 调用都会抛出
> `InvalidOperationException`。如需在 Server 模式启用 UDF，请自行 fork 并放开此选项；
> SonnetDB 上游不保证 AOT 兼容。

---

## 1. 标量 UDF — 委托形态

最常见的形态：把一个 `Func<IReadOnlyList<object?>, object?>` 注册为 SQL 函数。

```csharp
using SonnetDB.Engine;

using var db = Tsdb.Open(new TsdbOptions { RootDirectory = "./data" });

db.Functions.RegisterScalar(
    name: "celsius_to_fahrenheit",
    evaluator: args => Convert.ToDouble(args[0]) * 9.0 / 5.0 + 32.0,
    minArgumentCount: 1,
    maxArgumentCount: 1);
```

随后即可在 SQL 中使用：

```sql
SELECT time, celsius_to_fahrenheit(temperature) AS f
FROM sensor
WHERE device = 's-1';
```

> **作用域**：UDF 仅在所属 `Tsdb` 实例的查询中可见；
> 其他 `Tsdb` 实例并发执行 SQL 时不会看到本实例注册的函数，
> 由 `AsyncLocal<UserFunctionRegistry?>` 提供隔离。

> **覆盖内置函数**：注册同名 UDF 会**覆盖**同名内置函数。
> 例如重写 `abs` 不会报错，但请仅用于实验。

---

## 2. 标量 UDF — 自定义实现

如需自定义参数校验、自带状态等，可实现 `IScalarFunction`：

```csharp
using SonnetDB.Query.Functions;

public sealed class ClampFunction : IScalarFunction
{
    public string Name => "clamp";
    public int MinArgumentCount => 3;
    public int MaxArgumentCount => 3;

    public object? Evaluate(IReadOnlyList<object?> args)
    {
        double v   = Convert.ToDouble(args[0]);
        double lo  = Convert.ToDouble(args[1]);
        double hi  = Convert.ToDouble(args[2]);
        return Math.Clamp(v, lo, hi);
    }
}

db.Functions.RegisterScalar(new ClampFunction());
```

---

## 3. 聚合 UDF

聚合 UDF 必须实现 `IAggregateFunction` + 配套的 `IAggregateAccumulator`：

```csharp
using SonnetDB.Catalog;
using SonnetDB.Query;
using SonnetDB.Query.Functions;
using SonnetDB.Sql.Ast;

public sealed class GeomeanFunction : IAggregateFunction
{
    public string Name => "geomean";
    // 用户聚合必须返回 null（Aggregator 枚举仅供内置 7 个聚合占位）
    public Aggregator? LegacyAggregator => null;

    public string? ResolveFieldName(FunctionCallExpression call, MeasurementSchema schema)
    {
        var id = (IdentifierExpression)call.Arguments[0];
        var col = schema.TryGetColumn(id.Name)
            ?? throw new InvalidOperationException($"未知列 {id.Name}");
        if (col.Role != MeasurementColumnRole.Field)
            throw new InvalidOperationException($"geomean 只能作用于 FIELD 列");
        return col.Name;
    }

    public IAggregateAccumulator CreateAccumulator(FunctionCallExpression call, MeasurementSchema schema)
        => new Accumulator();

    private sealed class Accumulator : IAggregateAccumulator
    {
        private double _sumLog;
        public long Count { get; private set; }

        public void Add(double value)
        {
            if (value <= 0 || double.IsNaN(value)) return;
            _sumLog += Math.Log(value);
            Count++;
        }

        public void Merge(IAggregateAccumulator other)
        {
            var o = (Accumulator)other;
            _sumLog += o._sumLog;
            Count += o.Count;
        }

        public object? Finalize() => Count == 0 ? null : Math.Exp(_sumLog / Count);
    }
}

db.Functions.RegisterAggregate(new GeomeanFunction());
```

`Merge` 必须满足结合律 + 幂等性，以便跨段 / 跨桶合并。

---

## 4. 窗口 UDF

窗口 UDF 实现 `IWindowFunction`，与 PR #53 内置窗口函数共享 `IWindowEvaluator` 协议：

```csharp
public sealed class CenteredAverageFunction : IWindowFunction
{
    public string Name => "centered_avg";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        var fieldName = WindowFunctionBinder.ResolveFieldArgument(call, schema, 0);
        return new Evaluator(fieldName);
    }

    private sealed class Evaluator : IWindowEvaluator
    {
        public string FieldName { get; }
        public Evaluator(string fieldName) { FieldName = fieldName; }

        public object?[] Compute(long[] timestamps, FieldValue?[] values)
        {
            int n = values.Length;
            var output = new object?[n];
            for (int i = 1; i < n - 1; i++)
            {
                if (WindowFunctionBinder.TryToDouble(values[i - 1], out var a) &&
                    WindowFunctionBinder.TryToDouble(values[i],     out var b) &&
                    WindowFunctionBinder.TryToDouble(values[i + 1], out var c))
                {
                    output[i] = (a + b + c) / 3.0;
                }
            }
            return output;
        }
    }
}
```

---

## 5. 表值函数 UDF

表值 UDF 出现在 `FROM` 子句中，第 1 个参数必须是 measurement 标识符
（与内置 `forecast` 一致，由 Parser 强制）。

```csharp
using SonnetDB.Sql.Execution;

db.Functions.RegisterTableValuedFunction("downsample",
    (tsdb, statement) =>
    {
        // 解析参数：downsample(measurement, field, bucket_ms)
        var call = statement.TableValuedFunction!;
        // ... 校验、按桶聚合、构造行集 ...

        return new SelectExecutionResult(
            columns: new[] { "time", "avg" },
            rows: /* IReadOnlyList<IReadOnlyList<object?>> */ ...);
    });
```

随后即可：

```sql
SELECT * FROM downsample(cpu, usage, 60000) WHERE host='web-01';
```

> 注意：`'forecast'` 名称已被内置函数占用，注册同名 TVF 会抛出。

---

## 6. 取消注册 / 启用判定

```csharp
db.Functions.Unregister("celsius_to_fahrenheit"); // 任意类别均可移除
bool enabled = db.Functions.IsEnabled;            // false 时所有 Register 调用都会抛
```

`Unregister` 返回 `bool` 表示是否实际移除了某个条目。

---

## 7. 不在本里程碑范围

* **跨进程持久化**：UDF 仅生存于内存中，进程重启后需要再次注册。
* **沙箱化执行**：UDF 直接以宿主进程权限运行，请仅注册可信代码。
* **Server / HTTP 模式启用 UDF**：默认禁用以保证 AOT；如需启用需修改 `TsdbRegistry`。
* **更高阶预测算法（ARIMA / Prophet / 神经网络）**：可作为 UDF 自行接入，
  SonnetDB 上游不内置；内置 forecast 算法见 [forecast](/forecast/)。
