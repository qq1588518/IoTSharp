using SonnetDB.Catalog;
using SonnetDB.Model;
using SonnetDB.Sql.Ast;

namespace SonnetDB.Query.Functions.Window;

// ────────────────────────────────────────────────────────────────────────────
// 状态分析：state_changes / state_duration
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>state_changes(field)</c>：累计的状态变化次数。首行输出 0；之后每当当前值
/// 与上一非空值不相等时计数 +1。支持任意可比较类型（数值 / 布尔 / 字符串）。
/// </summary>
internal sealed class StateChangesFunction : IWindowFunction
{
    public string Name => "state_changes";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 1, 1);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0, allowString: true);
        return new StateChangesEvaluator(col.Name);
    }
}

internal sealed class StateChangesEvaluator : IWindowEvaluator
{
    public StateChangesEvaluator(string fieldName) => FieldName = fieldName;

    public string FieldName { get; }

    public object?[] Compute(long[] timestamps, FieldValue?[] values)
    {
        var output = new object?[timestamps.Length];
        long count = 0;
        FieldValue? prev = null;
        for (int i = 0; i < timestamps.Length; i++)
        {
            var cur = values[i];
            if (cur is null)
            {
                output[i] = count;
                continue;
            }

            if (prev is { } p)
            {
                if (!cur.Value.Equals(p))
                    count++;
            }
            output[i] = count;
            prev = cur;
        }
        return output;
    }
}

/// <summary>
/// <c>state_duration(field)</c>：当前状态已持续的毫秒数；状态发生变化时归零。
/// 缺失值不重置状态，只是累计差分。
/// </summary>
internal sealed class StateDurationFunction : IWindowFunction
{
    public string Name => "state_duration";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 1, 1);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0, allowString: true);
        return new StateDurationEvaluator(col.Name);
    }
}

internal sealed class StateDurationEvaluator : IWindowEvaluator
{
    public StateDurationEvaluator(string fieldName) => FieldName = fieldName;

    public string FieldName { get; }

    public object?[] Compute(long[] timestamps, FieldValue?[] values)
    {
        var output = new object?[timestamps.Length];
        long stateStartTs = 0;
        FieldValue? prevState = null;
        bool initialized = false;

        for (int i = 0; i < timestamps.Length; i++)
        {
            var cur = values[i];
            if (cur is null)
            {
                output[i] = initialized ? (object)(timestamps[i] - stateStartTs) : null;
                continue;
            }

            if (!initialized)
            {
                stateStartTs = timestamps[i];
                prevState = cur;
                initialized = true;
            }
            else if (!cur.Value.Equals(prevState!.Value))
            {
                stateStartTs = timestamps[i];
                prevState = cur;
            }

            output[i] = timestamps[i] - stateStartTs;
        }
        return output;
    }
}
