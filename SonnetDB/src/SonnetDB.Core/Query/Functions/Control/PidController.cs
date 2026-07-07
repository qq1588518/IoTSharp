namespace SonnetDB.Query.Functions.Control;

/// <summary>
/// 离散时间步进 PID 控制器：u(t) = Kp·e + Ki·∑e·dt + Kd·de/dt，其中 e = setpoint − pv。
/// </summary>
/// <remarks>
/// <para>
/// 状态：<c>integral</c>（误差对时间的累计积分）、<c>prevError</c>（上一次的误差，用于计算微分项）、
/// <c>prevTimeMs</c>（上一次更新的时间戳，用于计算 dt）。
/// </para>
/// <para>
/// 首次调用 <see cref="Update(long, double, double)"/> 时只输出比例项 <c>Kp · e</c>
/// （没有上一行，<c>I</c>、<c>D</c> 项为 0）；后续行用相邻时间戳之差作为 <c>dt</c>（秒）。
/// 当 <c>dt ≤ 0</c> 时跳过 <c>I</c>、<c>D</c> 更新，仅输出 <c>Kp · e</c>，避免发散。
/// </para>
/// </remarks>
public sealed class PidController
{
    private readonly double _kp;
    private readonly double _ki;
    private readonly double _kd;

    private double _integral;
    private double _prevError;
    private long _prevTimeMs;
    private bool _hasPrev;

    /// <summary>使用指定增益创建 PID 控制器。</summary>
    /// <param name="kp">比例增益。</param>
    /// <param name="ki">积分增益。</param>
    /// <param name="kd">微分增益。</param>
    public PidController(double kp, double ki, double kd)
    {
        _kp = kp;
        _ki = ki;
        _kd = kd;
    }

    /// <summary>已累加的积分项 <c>∑ e · dt</c>。</summary>
    public double Integral => _integral;

    /// <summary>上一次的误差值 <c>setpoint − pv</c>；初始为 0。</summary>
    public double PrevError => _prevError;

    /// <summary>上一次更新的时间戳（毫秒）；首次更新前为 0。</summary>
    public long PrevTimeMs => _prevTimeMs;

    /// <summary>是否已经至少更新过一次。</summary>
    public bool HasHistory => _hasPrev;

    /// <summary>
    /// 用新的过程变量与 setpoint 推进控制器并返回控制量 u(t)。
    /// </summary>
    /// <param name="timestampMs">当前样本的时间戳（毫秒）。</param>
    /// <param name="processVariable">当前过程变量。</param>
    /// <param name="setpoint">期望值。</param>
    /// <returns>控制量 u(t)。</returns>
    public double Update(long timestampMs, double processVariable, double setpoint)
    {
        double error = setpoint - processVariable;

        if (!_hasPrev)
        {
            _hasPrev = true;
            _prevError = error;
            _prevTimeMs = timestampMs;
            return _kp * error;
        }

        double dt = (timestampMs - _prevTimeMs) / 1000.0;
        if (dt <= 0)
        {
            // 时间未推进：只输出比例项，不破坏积分项与微分项历史。
            return _kp * error + _ki * _integral;
        }

        _integral += error * dt;
        double derivative = (error - _prevError) / dt;

        _prevError = error;
        _prevTimeMs = timestampMs;

        return _kp * error + _ki * _integral + _kd * derivative;
    }

    /// <summary>清除内部状态（积分项、上一行误差与时间戳）。</summary>
    public void Reset()
    {
        _integral = 0;
        _prevError = 0;
        _prevTimeMs = 0;
        _hasPrev = false;
    }

    /// <summary>导出当前快照（用于跨段合并 / 持久化）。</summary>
    /// <returns>不可变快照。</returns>
    public PidControllerSnapshot Snapshot()
        => new(_integral, _prevError, _prevTimeMs, _hasPrev);

    /// <summary>从快照恢复内部状态。</summary>
    /// <param name="snapshot">先前调用 <see cref="Snapshot"/> 得到的快照。</param>
    public void Restore(PidControllerSnapshot snapshot)
    {
        _integral = snapshot.Integral;
        _prevError = snapshot.PrevError;
        _prevTimeMs = snapshot.PrevTimeMs;
        _hasPrev = snapshot.HasHistory;
    }
}

/// <summary>PID 控制器的可序列化状态快照。</summary>
/// <param name="Integral">积分项 <c>∑ e · dt</c>。</param>
/// <param name="PrevError">上一行误差。</param>
/// <param name="PrevTimeMs">上一行时间戳（毫秒）。</param>
/// <param name="HasHistory">是否已累计过样本。</param>
public readonly record struct PidControllerSnapshot(
    double Integral,
    double PrevError,
    long PrevTimeMs,
    bool HasHistory);
