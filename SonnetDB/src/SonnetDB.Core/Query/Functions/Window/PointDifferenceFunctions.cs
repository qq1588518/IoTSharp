using SonnetDB.Catalog;
using SonnetDB.Model;
using SonnetDB.Sql.Ast;

namespace SonnetDB.Query.Functions.Window;

// ────────────────────────────────────────────────────────────────────────────
// 差分类窗口函数：difference / delta / derivative / non_negative_derivative
// / rate / irate / increase。
// 全部为 (current - previous) 的变体；首行输出 null。
// ────────────────────────────────────────────────────────────────────────────

internal sealed class DifferenceFunction : IWindowFunction
{
    public string Name => "difference";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 1, 1);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        return new DifferenceEvaluator(col.Name, scale: 1.0, nonNegative: false);
    }
}

/// <summary>
/// <c>delta</c>：在行级窗口里语义等同于 <see cref="DifferenceFunction"/>（当前行减上一行）。
/// </summary>
internal sealed class DeltaFunction : IWindowFunction
{
    public string Name => "delta";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 1, 1);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        return new DifferenceEvaluator(col.Name, scale: 1.0, nonNegative: false);
    }
}

/// <summary>
/// <c>increase</c>：行级版本输出 <c>max(0, current - previous)</c>，用于计数器场景下
/// 抑制 reset 引起的负差。
/// </summary>
internal sealed class IncreaseFunction : IWindowFunction
{
    public string Name => "increase";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 1, 1);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        return new DifferenceEvaluator(col.Name, scale: 1.0, nonNegative: true);
    }
}

/// <summary>
/// <c>derivative(field [, unit])</c>：按时间差归一化的差分，单位默认 1 秒。
/// 当 dt == 0 时输出 null。
/// </summary>
internal sealed class DerivativeFunction : IWindowFunction
{
    public string Name => "derivative";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 1, 2);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        long unitMs = WindowFunctionBinder.ResolveUnitMillisecondsArgument(call, 1, Name);
        return new DerivativeEvaluator(col.Name, unitMs, nonNegative: false);
    }
}

/// <summary>
/// <c>non_negative_derivative(field [, unit])</c>：与 <see cref="DerivativeFunction"/> 相同，
/// 但负差（counter reset）输出 null。
/// </summary>
internal sealed class NonNegativeDerivativeFunction : IWindowFunction
{
    public string Name => "non_negative_derivative";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 1, 2);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        long unitMs = WindowFunctionBinder.ResolveUnitMillisecondsArgument(call, 1, Name);
        return new DerivativeEvaluator(col.Name, unitMs, nonNegative: true);
    }
}

/// <summary><c>rate(field [, unit])</c>：等价于 <see cref="NonNegativeDerivativeFunction"/>。</summary>
internal sealed class RateFunction : IWindowFunction
{
    public string Name => "rate";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 1, 2);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        long unitMs = WindowFunctionBinder.ResolveUnitMillisecondsArgument(call, 1, Name);
        return new DerivativeEvaluator(col.Name, unitMs, nonNegative: true);
    }
}

/// <summary>
/// <c>irate(field [, unit])</c>：行级窗口里与 <see cref="DerivativeFunction"/> 等价
/// （PromQL 的 irate 在区间内只取最后两点，行级语义本就是相邻两点差）。
/// </summary>
internal sealed class IrateFunction : IWindowFunction
{
    public string Name => "irate";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 1, 2);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        long unitMs = WindowFunctionBinder.ResolveUnitMillisecondsArgument(call, 1, Name);
        return new DerivativeEvaluator(col.Name, unitMs, nonNegative: true);
    }
}

/// <summary>共享的差分求值器。</summary>
internal sealed class DifferenceEvaluator : IWindowEvaluator
{
    private readonly double _scale;
    private readonly bool _nonNegative;

    public DifferenceEvaluator(string fieldName, double scale, bool nonNegative)
    {
        FieldName = fieldName;
        _scale = scale;
        _nonNegative = nonNegative;
    }

    public string FieldName { get; }

    public object?[] Compute(long[] timestamps, FieldValue?[] values)
    {
        var output = new object?[timestamps.Length];
        double? prev = null;
        for (int i = 0; i < timestamps.Length; i++)
        {
            if (!WindowFunctionBinder.TryToDouble(values[i], out var cur))
            {
                output[i] = null;
                // 不更新 prev：缺失值不参与差分链
                continue;
            }

            if (prev is { } p)
            {
                double diff = (cur - p) * _scale;
                if (_nonNegative && diff < 0)
                    output[i] = null;
                else
                    output[i] = diff;
            }
            else
            {
                output[i] = null;
            }
            prev = cur;
        }
        return output;
    }
}

/// <summary>带时间归一化的差分求值器（derivative / rate / non_negative_derivative）。</summary>
internal sealed class DerivativeEvaluator : IWindowEvaluator
{
    private readonly long _unitMs;
    private readonly bool _nonNegative;

    public DerivativeEvaluator(string fieldName, long unitMs, bool nonNegative)
    {
        FieldName = fieldName;
        _unitMs = unitMs;
        _nonNegative = nonNegative;
    }

    public string FieldName { get; }

    public object?[] Compute(long[] timestamps, FieldValue?[] values)
    {
        var output = new object?[timestamps.Length];
        double? prev = null;
        long prevTs = 0;
        for (int i = 0; i < timestamps.Length; i++)
        {
            if (!WindowFunctionBinder.TryToDouble(values[i], out var cur))
            {
                output[i] = null;
                continue;
            }

            if (prev is { } p)
            {
                long dtMs = timestamps[i] - prevTs;
                if (dtMs <= 0)
                {
                    output[i] = null;
                }
                else
                {
                    double rate = (cur - p) * _unitMs / dtMs;
                    if (_nonNegative && rate < 0)
                        output[i] = null;
                    else
                        output[i] = rate;
                }
            }
            else
            {
                output[i] = null;
            }
            prev = cur;
            prevTs = timestamps[i];
        }
        return output;
    }
}
