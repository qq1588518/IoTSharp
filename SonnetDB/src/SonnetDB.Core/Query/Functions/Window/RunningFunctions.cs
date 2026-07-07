using SonnetDB.Catalog;
using SonnetDB.Model;
using SonnetDB.Sql.Ast;

namespace SonnetDB.Query.Functions.Window;

// ────────────────────────────────────────────────────────────────────────────
// 累计 / 积分类窗口函数：cumulative_sum / integral
// ────────────────────────────────────────────────────────────────────────────

/// <summary><c>cumulative_sum(field)</c>：从首行起的累计和；缺失值保留前一累计值。</summary>
internal sealed class CumulativeSumFunction : IWindowFunction
{
    public string Name => "cumulative_sum";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 1, 1);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        return new CumulativeSumEvaluator(col.Name);
    }
}

/// <summary><c>running_sum(field)</c>：<see cref="CumulativeSumFunction"/> 的兼容别名。</summary>
internal sealed class RunningSumFunction : IWindowFunction
{
    public string Name => "running_sum";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 1, 1);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        return new CumulativeSumEvaluator(col.Name);
    }
}

/// <summary><c>running_min(field)</c>：从首个有效值开始的累计最小值；缺失值保留前一累计值。</summary>
internal sealed class RunningMinFunction : IWindowFunction
{
    public string Name => "running_min";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 1, 1);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        return new RunningExtremeEvaluator(col.Name, isMin: true);
    }
}

/// <summary><c>running_max(field)</c>：从首个有效值开始的累计最大值；缺失值保留前一累计值。</summary>
internal sealed class RunningMaxFunction : IWindowFunction
{
    public string Name => "running_max";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 1, 1);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        return new RunningExtremeEvaluator(col.Name, isMin: false);
    }
}

internal sealed class CumulativeSumEvaluator : DoubleWindowEvaluatorBase
{
    public CumulativeSumEvaluator(string fieldName) => FieldName = fieldName;

    public override string FieldName { get; }

    public override IWindowState CreateState()
        => new CumulativeSumState();

    protected override void ComputeDoubleCore(
        ReadOnlySpan<long> timestamps,
        ReadOnlySpan<FieldValue?> values,
        Span<double> output,
        Span<bool> hasValue)
    {
        hasValue.Clear();
        double sum = 0;
        bool seen = false;
        for (int i = 0; i < values.Length; i++)
        {
            if (WindowFunctionBinder.TryToDouble(values[i], out var v))
            {
                sum += v;
                seen = true;
            }

            if (seen)
            {
                output[i] = sum;
                hasValue[i] = true;
            }
        }
    }

    protected override void ComputeObjectCore(
        ReadOnlySpan<long> timestamps,
        ReadOnlySpan<FieldValue?> values,
        Span<object?> output)
    {
        output.Clear();
        double sum = 0;
        bool seen = false;
        for (int i = 0; i < values.Length; i++)
        {
            if (WindowFunctionBinder.TryToDouble(values[i], out var v))
            {
                sum += v;
                seen = true;
            }

            if (seen)
                output[i] = sum;
        }
    }
}

internal sealed class CumulativeSumState : IWindowState
{
    private double _sum;
    private bool _seen;

    public WindowStateOutput Update(long timestamp, FieldValue? value)
    {
        if (WindowFunctionBinder.TryToDouble(value, out var v))
        {
            _sum += v;
            _seen = true;
        }

        return _seen
            ? WindowStateOutput.FromDouble(_sum)
            : WindowStateOutput.Null();
    }
}

internal sealed class RunningExtremeEvaluator : DoubleWindowEvaluatorBase
{
    private readonly bool _isMin;

    public RunningExtremeEvaluator(string fieldName, bool isMin)
    {
        FieldName = fieldName;
        _isMin = isMin;
    }

    public override string FieldName { get; }

    public override IWindowState CreateState()
        => new RunningExtremeState(_isMin);

    protected override void ComputeDoubleCore(
        ReadOnlySpan<long> timestamps,
        ReadOnlySpan<FieldValue?> values,
        Span<double> output,
        Span<bool> hasValue)
    {
        hasValue.Clear();
        double current = 0;
        bool seen = false;
        for (int i = 0; i < values.Length; i++)
        {
            if (WindowFunctionBinder.TryToDouble(values[i], out var v))
            {
                current = !seen
                    ? v
                    : _isMin ? Math.Min(current, v) : Math.Max(current, v);
                seen = true;
            }

            if (seen)
            {
                output[i] = current;
                hasValue[i] = true;
            }
        }
    }

    protected override void ComputeObjectCore(
        ReadOnlySpan<long> timestamps,
        ReadOnlySpan<FieldValue?> values,
        Span<object?> output)
    {
        output.Clear();
        double current = 0;
        bool seen = false;
        for (int i = 0; i < values.Length; i++)
        {
            if (WindowFunctionBinder.TryToDouble(values[i], out var v))
            {
                current = !seen
                    ? v
                    : _isMin ? Math.Min(current, v) : Math.Max(current, v);
                seen = true;
            }

            if (seen)
                output[i] = current;
        }
    }
}

internal sealed class RunningExtremeState : IWindowState
{
    private readonly bool _isMin;
    private double _current;
    private bool _seen;

    public RunningExtremeState(bool isMin)
    {
        _isMin = isMin;
    }

    public WindowStateOutput Update(long timestamp, FieldValue? value)
    {
        if (WindowFunctionBinder.TryToDouble(value, out var v))
        {
            _current = !_seen
                ? v
                : _isMin ? Math.Min(_current, v) : Math.Max(_current, v);
            _seen = true;
        }

        return _seen
            ? WindowStateOutput.FromDouble(_current)
            : WindowStateOutput.Null();
    }
}

/// <summary>
/// <c>integral(field [, unit])</c>：基于梯形法的累计积分，单位默认 1 秒。
/// 缺失值跳过、不参与积分（视为采样间断）。
/// </summary>
internal sealed class IntegralFunction : IWindowFunction
{
    public string Name => "integral";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 1, 2);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        long unitMs = WindowFunctionBinder.ResolveUnitMillisecondsArgument(call, 1, Name);
        return new IntegralEvaluator(col.Name, unitMs);
    }
}

internal sealed class IntegralEvaluator : IWindowEvaluator
{
    private readonly long _unitMs;

    public IntegralEvaluator(string fieldName, long unitMs)
    {
        FieldName = fieldName;
        _unitMs = unitMs;
    }

    public string FieldName { get; }

    public object?[] Compute(long[] timestamps, FieldValue?[] values)
    {
        var output = new object?[timestamps.Length];
        double area = 0;
        double? prev = null;
        long prevTs = 0;
        for (int i = 0; i < timestamps.Length; i++)
        {
            if (!WindowFunctionBinder.TryToDouble(values[i], out var cur))
            {
                output[i] = prev is null ? null : (object)area;
                continue;
            }

            if (prev is { } p)
            {
                long dtMs = timestamps[i] - prevTs;
                if (dtMs > 0)
                    area += 0.5 * (p + cur) * dtMs / _unitMs;
            }

            output[i] = area;
            prev = cur;
            prevTs = timestamps[i];
        }
        return output;
    }
}
