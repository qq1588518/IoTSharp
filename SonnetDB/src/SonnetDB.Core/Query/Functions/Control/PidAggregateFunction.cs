using SonnetDB.Catalog;
using SonnetDB.Query.Functions.Aggregates;
using SonnetDB.Sql.Ast;
using SonnetDB.Storage.Format;

namespace SonnetDB.Query.Functions.Control;

/// <summary>
/// <c>pid(field, setpoint, kp, ki, kd)</c>：聚合形态的 PID 控制律。
/// 在 <c>GROUP BY time(...)</c> 桶内逐行推进 <see cref="PidController"/>，
/// 桶结束时输出最终控制量 u(t)。
/// </summary>
internal sealed class PidAggregateFunction : IAggregateFunction
{
    private const int _expectedArgumentCount = 5;

    public string Name => "pid";

    public Aggregator? LegacyAggregator => null;

    public string? ResolveFieldName(FunctionCallExpression call, MeasurementSchema schema)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(schema);

        if (call.IsStar)
            throw new InvalidOperationException("pid(*) 非法。");
        if (call.Arguments.Count != _expectedArgumentCount)
            throw new InvalidOperationException(
                $"pid(...) 需要 {_expectedArgumentCount} 个参数（字段, setpoint, kp, ki, kd），实际 {call.Arguments.Count}。");

        if (call.Arguments[0] is not IdentifierExpression id)
            throw new InvalidOperationException("pid(...) 第一个参数必须是字段名。");

        var col = schema.TryGetColumn(id.Name)
            ?? throw new InvalidOperationException($"pid({id.Name}, ...) 引用了未知列。");
        if (col.Role != MeasurementColumnRole.Field)
            throw new InvalidOperationException(
                $"pid 只能作用于 FIELD 列（'{id.Name}' 是 {col.Role}）。");
        if (col.DataType == FieldType.String)
            throw new InvalidOperationException(
                $"pid 不支持 String 字段 '{id.Name}'。");

        return col.Name;
    }

    public IAggregateAccumulator CreateAccumulator(FunctionCallExpression call, MeasurementSchema schema)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (call.Arguments.Count != _expectedArgumentCount)
            throw new InvalidOperationException(
                $"pid(...) 需要 {_expectedArgumentCount} 个参数（字段, setpoint, kp, ki, kd），实际 {call.Arguments.Count}。");

        double setpoint = ResolveNumericArgument(call, 1, "setpoint");
        double kp = ResolveNumericArgument(call, 2, "kp");
        double ki = ResolveNumericArgument(call, 3, "ki");
        double kd = ResolveNumericArgument(call, 4, "kd");
        return new PidAccumulator(setpoint, kp, ki, kd);
    }

    IAggregateAccumulator? IAggregateFunction.CreateAccumulator(FunctionCallExpression call, MeasurementSchema schema)
        => CreateAccumulator(call, schema);

    /// <summary>解析 PID 数值常量参数（允许形如 <c>-1</c> 的带负号字面量）。</summary>
    internal static double ResolveNumericArgument(FunctionCallExpression call, int index, string parameterName)
    {
        var arg = call.Arguments[index];
        double sign = 1.0;
        if (arg is UnaryExpression { Operator: SqlUnaryOperator.Negate, Operand: var operand })
        {
            sign = -1.0;
            arg = operand;
        }

        return arg switch
        {
            LiteralExpression { Kind: SqlLiteralKind.Integer } lit => sign * lit.IntegerValue,
            LiteralExpression { Kind: SqlLiteralKind.Float } lit => sign * lit.FloatValue,
            _ => throw new InvalidOperationException(
                $"pid(...) 参数 '{parameterName}' 必须是数值字面量。"),
        };
    }
}

/// <summary>
/// <c>pid</c> 聚合的累加器：包装一个 <see cref="PidController"/>，
/// <see cref="Add(long, double)"/> 推进控制器，<see cref="Finalize"/> 返回桶内最后一行的 u(t)。
/// </summary>
internal sealed class PidAccumulator : IAggregateAccumulator
{
    private readonly double _setpoint;
    private readonly PidController _controller;
    private double _lastU;
    private long _count;

    public PidAccumulator(double setpoint, double kp, double ki, double kd)
    {
        _setpoint = setpoint;
        _controller = new PidController(kp, ki, kd);
    }

    public long Count => _count;

    public void Add(double value) => Add(0L, value);

    public void Add(long timestampMs, double value)
    {
        if (double.IsNaN(value)) return;
        _lastU = _controller.Update(timestampMs, value, _setpoint);
        _count++;
    }

    public void Merge(IAggregateAccumulator other)
    {
        if (other is not PidAccumulator p)
            throw new ArgumentException($"无法将 {other.GetType().Name} 合并进 PidAccumulator。", nameof(other));
        if (p._count == 0) return;
        // 跨段合并：假设两段时间序列连续（同一桶被分片），用对方的最终状态接续。
        if (_count == 0)
        {
            _controller.Restore(p._controller.Snapshot());
            _lastU = p._lastU;
        }
        else
        {
            // 取时间戳更晚的那一段作为最终状态。
            if (p._controller.PrevTimeMs > _controller.PrevTimeMs)
            {
                _controller.Restore(p._controller.Snapshot());
                _lastU = p._lastU;
            }
        }
        _count += p._count;
    }

    public object? Finalize() => _count == 0 ? null : _lastU;
}
