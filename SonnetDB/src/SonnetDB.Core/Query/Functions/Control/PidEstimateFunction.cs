using System.Globalization;
using SonnetDB.Catalog;
using SonnetDB.Sql.Ast;
using SonnetDB.Storage.Format;

namespace SonnetDB.Query.Functions.Control;

/// <summary>
/// <c>pid_estimate(field, method, step_magnitude, initial_fraction, final_fraction, imc_lambda)</c>：
/// 阶跃响应自动整定的聚合函数。在查询结果集（通常是一段历史阶跃响应数据）上调用
/// <see cref="PidParameterEstimator.Estimate(IReadOnlyList{System.ValueTuple{long, double}}, PidEstimationOptions?)"/>，
/// 返回 JSON 字符串 <c>{"kp":..,"ki":..,"kd":..}</c>。
/// </summary>
/// <remarks>
/// 参数语义：
/// <list type="bullet">
///   <item><c>field</c>：FIELD 列名（非字符串）。</item>
///   <item><c>method</c>：字符串字面量 <c>'zn'</c> / <c>'cc'</c> / <c>'imc'</c>，或 NULL（默认 ZN）。</item>
///   <item><c>step_magnitude</c>：数值字面量或 NULL（NULL 表示假定 Δu = 1.0）。</item>
///   <item><c>initial_fraction</c>：数值字面量 (0,0.5) 或 NULL（默认 0.1）。</item>
///   <item><c>final_fraction</c>：数值字面量 (0,0.5) 或 NULL（默认 0.1）。</item>
///   <item><c>imc_lambda</c>：数值字面量或 NULL（NULL 表示 λ = θ）。</item>
/// </list>
/// </remarks>
internal sealed class PidEstimateFunction : IAggregateFunction
{
    private const int _expectedArgumentCount = 6;

    public string Name => "pid_estimate";

    public Aggregator? LegacyAggregator => null;

    public string? ResolveFieldName(FunctionCallExpression call, MeasurementSchema schema)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(schema);

        if (call.IsStar)
            throw new InvalidOperationException("pid_estimate(*) 非法。");
        if (call.Arguments.Count != _expectedArgumentCount)
            throw new InvalidOperationException(
                $"pid_estimate(...) 需要 {_expectedArgumentCount} 个参数（field, method, step_magnitude, initial_fraction, final_fraction, imc_lambda），实际 {call.Arguments.Count}。");

        if (call.Arguments[0] is not IdentifierExpression id)
            throw new InvalidOperationException("pid_estimate(...) 第一个参数必须是字段名。");

        var col = schema.TryGetColumn(id.Name)
            ?? throw new InvalidOperationException($"pid_estimate({id.Name}, ...) 引用了未知列。");
        if (col.Role != MeasurementColumnRole.Field)
            throw new InvalidOperationException(
                $"pid_estimate 只能作用于 FIELD 列（'{id.Name}' 是 {col.Role}）。");
        if (col.DataType == FieldType.String)
            throw new InvalidOperationException(
                $"pid_estimate 不支持 String 字段 '{id.Name}'。");

        return col.Name;
    }

    public IAggregateAccumulator CreateAccumulator(FunctionCallExpression call, MeasurementSchema schema)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (call.Arguments.Count != _expectedArgumentCount)
            throw new InvalidOperationException(
                $"pid_estimate(...) 需要 {_expectedArgumentCount} 个参数（field, method, step_magnitude, initial_fraction, final_fraction, imc_lambda），实际 {call.Arguments.Count}。");

        var method = ResolveMethod(call.Arguments[1]);
        var stepMagnitude = ResolveOptionalNumber(call.Arguments[2], "step_magnitude");
        var initialFraction = ResolveOptionalNumber(call.Arguments[3], "initial_fraction");
        var finalFraction = ResolveOptionalNumber(call.Arguments[4], "final_fraction");
        var imcLambda = ResolveOptionalNumber(call.Arguments[5], "imc_lambda");

        var options = new PidEstimationOptions
        {
            Method = method,
            StepMagnitude = stepMagnitude,
            InitialFraction = initialFraction ?? 0.1,
            FinalFraction = finalFraction ?? 0.1,
            ImcLambda = imcLambda,
        };

        return new PidEstimateAccumulator(options);
    }

    IAggregateAccumulator? IAggregateFunction.CreateAccumulator(FunctionCallExpression call, MeasurementSchema schema)
        => CreateAccumulator(call, schema);

    private static PidTuningMethod ResolveMethod(SqlExpression arg)
    {
        if (arg is LiteralExpression { Kind: SqlLiteralKind.Null })
            return PidTuningMethod.ZieglerNichols;

        if (arg is LiteralExpression { Kind: SqlLiteralKind.String, StringValue: var s } && s is not null)
        {
            return s.ToLowerInvariant() switch
            {
                "zn" or "ziegler-nichols" or "ziegler_nichols" or "zieglernichols" => PidTuningMethod.ZieglerNichols,
                "cc" or "cohen-coon" or "cohen_coon" or "cohencoon" => PidTuningMethod.CohenCoon,
                "imc" or "skogestad" or "simc" => PidTuningMethod.Imc,
                _ => throw new InvalidOperationException(
                    $"pid_estimate(...) 未知整定方法 '{s}'，支持 'zn' / 'cc' / 'imc'。"),
            };
        }

        throw new InvalidOperationException("pid_estimate(...) 参数 'method' 必须是字符串字面量或 NULL。");
    }

    private static double? ResolveOptionalNumber(SqlExpression arg, string parameterName)
    {
        if (arg is LiteralExpression { Kind: SqlLiteralKind.Null })
            return null;

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
                $"pid_estimate(...) 参数 '{parameterName}' 必须是数值字面量或 NULL。"),
        };
    }
}

/// <summary>
/// <c>pid_estimate</c> 聚合的累加器：收集 (timestamp, value) 样本，
/// <see cref="Finalize"/> 时调用 <see cref="PidParameterEstimator.Estimate(IReadOnlyList{System.ValueTuple{long, double}}, PidEstimationOptions?)"/>
/// 返回 JSON 编码的 PID 参数。
/// </summary>
internal sealed class PidEstimateAccumulator : IAggregateAccumulator
{
    private readonly PidEstimationOptions _options;
    private readonly List<(long TimestampMs, double Value)> _samples = new();

    public PidEstimateAccumulator(PidEstimationOptions options)
    {
        _options = options;
    }

    public long Count => _samples.Count;

    public void Add(double value) => Add(0L, value);

    public void Add(long timestampMs, double value)
    {
        if (double.IsNaN(value)) return;
        _samples.Add((timestampMs, value));
    }

    public void Merge(IAggregateAccumulator other)
    {
        if (other is not PidEstimateAccumulator p)
            throw new ArgumentException(
                $"无法将 {other.GetType().Name} 合并进 PidEstimateAccumulator。", nameof(other));
        if (p._samples.Count == 0) return;
        _samples.AddRange(p._samples);
    }

    public object? Finalize()
    {
        if (_samples.Count == 0) return null;
        // 估算前确保按时间戳升序，避免上游分段返回顺序不确定。
        _samples.Sort(static (a, b) => a.TimestampMs.CompareTo(b.TimestampMs));

        var p = PidParameterEstimator.Estimate(_samples, _options);
        var c = CultureInfo.InvariantCulture;
        return $"{{\"kp\":{p.Kp.ToString("R", c)},\"ki\":{p.Ki.ToString("R", c)},\"kd\":{p.Kd.ToString("R", c)}}}";
    }
}
