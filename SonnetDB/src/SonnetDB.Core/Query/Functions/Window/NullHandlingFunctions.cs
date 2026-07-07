using SonnetDB.Catalog;
using SonnetDB.Model;
using SonnetDB.Sql.Ast;

namespace SonnetDB.Query.Functions.Window;

// ────────────────────────────────────────────────────────────────────────────
// 缺失值处理：fill / locf / interpolate
// ────────────────────────────────────────────────────────────────────────────

/// <summary><c>fill(field, value)</c>：用指定数值填充缺失值。原值非空时直接透传。</summary>
internal sealed class FillFunction : IWindowFunction
{
    public string Name => "fill";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 2, 2);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        double fillValue = WindowFunctionBinder.ResolveNumericArgument(call, 1, Name);
        return new FillEvaluator(col.Name, fillValue);
    }
}

internal sealed class FillEvaluator : IWindowEvaluator
{
    private readonly double _fill;

    public FillEvaluator(string fieldName, double fill)
    {
        FieldName = fieldName;
        _fill = fill;
    }

    public string FieldName { get; }

    public object?[] Compute(long[] timestamps, FieldValue?[] values)
    {
        var output = new object?[timestamps.Length];
        for (int i = 0; i < timestamps.Length; i++)
        {
            output[i] = WindowFunctionBinder.TryToDouble(values[i], out var v) ? v : _fill;
        }
        return output;
    }
}

/// <summary>
/// <c>locf(field)</c>：last observation carried forward。原值非空时透传，
/// 否则使用最近一次的非空值；首段缺失输出 null。
/// </summary>
internal sealed class LocfFunction : IWindowFunction
{
    public string Name => "locf";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 1, 1);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        return new LocfEvaluator(col.Name);
    }
}

internal sealed class LocfEvaluator : IWindowEvaluator
{
    public LocfEvaluator(string fieldName) => FieldName = fieldName;

    public string FieldName { get; }

    public object?[] Compute(long[] timestamps, FieldValue?[] values)
    {
        var output = new object?[timestamps.Length];
        double? last = null;
        for (int i = 0; i < timestamps.Length; i++)
        {
            if (WindowFunctionBinder.TryToDouble(values[i], out var v))
            {
                last = v;
            }
            output[i] = last;
        }
        return output;
    }
}

/// <summary>
/// <c>interpolate(field)</c>：在两个相邻非空采样点之间做线性插值。
/// 首段或末段缺失（无对应锚点）输出 null。
/// </summary>
internal sealed class InterpolateFunction : IWindowFunction
{
    public string Name => "interpolate";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 1, 1);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        return new InterpolateEvaluator(col.Name);
    }
}

internal sealed class InterpolateEvaluator : IWindowEvaluator
{
    public InterpolateEvaluator(string fieldName) => FieldName = fieldName;

    public string FieldName { get; }

    public object?[] Compute(long[] timestamps, FieldValue?[] values)
    {
        int n = timestamps.Length;
        var numeric = new double[n];
        var hasValue = new bool[n];
        for (int i = 0; i < n; i++)
        {
            if (WindowFunctionBinder.TryToDouble(values[i], out var v))
            {
                numeric[i] = v;
                hasValue[i] = true;
            }
        }

        var output = new object?[n];
        int leftIdx = -1;
        for (int i = 0; i < n; i++)
        {
            if (hasValue[i])
            {
                output[i] = numeric[i];
                leftIdx = i;
                continue;
            }

            // 当前缺失：需要向右找下一个有值的位置
            int rightIdx = -1;
            for (int j = i + 1; j < n; j++)
            {
                if (hasValue[j]) { rightIdx = j; break; }
            }

            if (leftIdx < 0 || rightIdx < 0)
            {
                // 首段或末段缺失：保持 null
                output[i] = null;
                continue;
            }

            long t0 = timestamps[leftIdx];
            long t1 = timestamps[rightIdx];
            double v0 = numeric[leftIdx];
            double v1 = numeric[rightIdx];
            // 时间戳严格递增，t1 > t0
            double frac = (timestamps[i] - t0) / (double)(t1 - t0);
            output[i] = v0 + frac * (v1 - v0);
        }
        return output;
    }
}
