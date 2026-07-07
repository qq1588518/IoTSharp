using SonnetDB.Catalog;
using SonnetDB.Model;
using SonnetDB.Query.Functions.Control;
using SonnetDB.Query.Functions.Window;
using SonnetDB.Sql.Ast;

namespace SonnetDB.Query.Functions.Control;

/// <summary>
/// <c>pid_series(field, setpoint, kp, ki, kd)</c>：行级 PID 控制律窗口函数。
/// 对每个 series 的每一行运行 <see cref="PidController"/>，输出对应的控制量 u(t)。
/// 主要用于回测和将控制律结果直接 <c>INSERT … SELECT</c> 写回执行器表。
/// </summary>
internal sealed class PidSeriesFunction : IWindowFunction
{
    public string Name => "pid_series";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 5, 5);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        double setpoint = WindowFunctionBinder.ResolveNumericArgument(call, 1, Name);
        double kp = WindowFunctionBinder.ResolveNumericArgument(call, 2, Name);
        double ki = WindowFunctionBinder.ResolveNumericArgument(call, 3, Name);
        double kd = WindowFunctionBinder.ResolveNumericArgument(call, 4, Name);
        return new PidSeriesEvaluator(col.Name, setpoint, kp, ki, kd);
    }
}

internal sealed class PidSeriesEvaluator : IWindowEvaluator
{
    private readonly double _setpoint;
    private readonly double _kp;
    private readonly double _ki;
    private readonly double _kd;

    public PidSeriesEvaluator(string fieldName, double setpoint, double kp, double ki, double kd)
    {
        FieldName = fieldName;
        _setpoint = setpoint;
        _kp = kp;
        _ki = ki;
        _kd = kd;
    }

    public string FieldName { get; }

    public object?[] Compute(long[] timestamps, FieldValue?[] values)
    {
        ArgumentNullException.ThrowIfNull(timestamps);
        ArgumentNullException.ThrowIfNull(values);
        if (timestamps.Length != values.Length)
            throw new ArgumentException("timestamps 与 values 长度不一致。", nameof(values));

        var output = new object?[timestamps.Length];
        var pid = new PidController(_kp, _ki, _kd);

        for (int i = 0; i < timestamps.Length; i++)
        {
            if (values[i] is not { } fv || !fv.TryGetNumeric(out double pv))
            {
                output[i] = null;
                continue;
            }

            output[i] = pid.Update(timestamps[i], pv, _setpoint);
        }

        return output;
    }
}
