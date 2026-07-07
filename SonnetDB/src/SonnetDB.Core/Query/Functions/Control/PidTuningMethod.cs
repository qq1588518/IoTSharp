namespace SonnetDB.Query.Functions.Control;

/// <summary>
/// PID 参数整定方法。
/// </summary>
public enum PidTuningMethod : byte
{
    /// <summary>
    /// Ziegler-Nichols 阶跃响应法（默认）。
    /// 适用于大多数工业过程，整定激进，动态响应较快。
    /// </summary>
    ZieglerNichols = 0,

    /// <summary>
    /// Cohen-Coon 法。
    /// 适用于死区时间较大（θ/τ &gt; 0.2）的过程，超调量通常低于 Ziegler-Nichols。
    /// </summary>
    CohenCoon = 1,

    /// <summary>
    /// IMC（内模控制）PID 法（Skogestad SIMC 规则）。
    /// 提供更平稳的设定值跟踪与更好的鲁棒性，需通过 <see cref="PidEstimationOptions.ImcLambda"/> 调节闭环带宽。
    /// </summary>
    Imc = 2,
}
