namespace SonnetDB.Query.Functions.Control;

/// <summary>
/// PID 控制器的三个核心增益参数。
/// </summary>
/// <param name="Kp">比例增益。</param>
/// <param name="Ki">积分增益（Ki = Kp / Ti，其中 Ti 为积分时间）。</param>
/// <param name="Kd">微分增益（Kd = Kp × Td，其中 Td 为微分时间）。</param>
/// <remarks>
/// 控制律：u(t) = Kp·e(t) + Ki·∫e(t)dt + Kd·de(t)/dt
/// 其中 e(t) = setpoint - processVariable。
/// </remarks>
public sealed record PidParameters(double Kp, double Ki, double Kd);
