namespace SonnetDB.Query.Functions.Control;

/// <summary>
/// <see cref="PidParameterEstimator.Estimate"/> 的估算选项。
/// </summary>
public sealed record PidEstimationOptions
{
    /// <summary>
    /// PID 整定方法，默认 <see cref="PidTuningMethod.ZieglerNichols"/>。
    /// </summary>
    public PidTuningMethod Method { get; init; } = PidTuningMethod.ZieglerNichols;

    /// <summary>
    /// 阶跃输入幅度（Δu）。用于计算过程增益 K = Δy / Δu。
    /// 若为 <c>null</c>，则假定 Δu = 1.0（输出为归一化增益下的参数）。
    /// </summary>
    public double? StepMagnitude { get; init; }

    /// <summary>
    /// IMC 整定参数 λ（期望闭环时间常数，与输入数据时间戳单位一致）。
    /// 若为 <c>null</c>，则默认取 λ = θ（死区时间，紧凑整定）。
    /// 仅当 <see cref="Method"/> 为 <see cref="PidTuningMethod.Imc"/> 时有效。
    /// 增大 λ 可获得更平稳但响应更慢的控制效果。
    /// </summary>
    public double? ImcLambda { get; init; }

    /// <summary>
    /// 用于估算响应前基线稳态的数据比例（0 到 0.5，不含端点），默认 0.1。
    /// 取数据前 10% 的均值作为初始稳态 y₀。
    /// </summary>
    public double InitialFraction { get; init; } = 0.1;

    /// <summary>
    /// 用于估算响应后最终稳态的数据比例（0 到 0.5，不含端点），默认 0.15。
    /// 取数据后 15% 的均值作为最终稳态 y∞。
    /// </summary>
    public double FinalFraction { get; init; } = 0.15;
}
