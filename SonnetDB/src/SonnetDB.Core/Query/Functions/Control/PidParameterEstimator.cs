using SonnetDB.Model;

namespace SonnetDB.Query.Functions.Control;

/// <summary>
/// 根据过程阶跃响应的历史时序数据估算 PID 控制器参数。
/// </summary>
/// <remarks>
/// <para>
/// <b>算法概述</b>（Sundaresan &amp; Krishnaswamy 两点法）：
/// </para>
/// <list type="number">
///   <item>
///     从数据首尾分别取均值，得到初始稳态 y₀ 和最终稳态 y∞，计算过程增益
///     K = (y∞ − y₀) / Δu。
///   </item>
///   <item>
///     线性插值找到响应达到 35% 与 85% 的时刻（t₃₅、t₈₅），利用公式
///     τ = (t₈₅ − t₃₅) / 1.4663，θ = t₃₅ − 0.4308τ 辨识一阶纯滞后（FOPDT）模型。
///   </item>
///   <item>
///     根据 <see cref="PidTuningMethod"/> 指定的整定规则（Ziegler-Nichols、Cohen-Coon
///     或 Skogestad SIMC/IMC）计算 Kp、Ki、Kd。
///   </item>
/// </list>
/// <para>
/// <b>输入数据要求</b>：数据应覆盖完整的阶跃响应，包含阶跃前的稳态和阶跃后趋于稳态的过程；
/// 时间戳升序排列；至少 10 个采样点。
/// </para>
/// </remarks>
public static class PidParameterEstimator
{
    private const int _minSampleCount = 10;

    // 一阶系统中响应到达35%时对应的归一化时间系数：-ln(1-0.35) = -ln(0.65) ≈ 0.4308
    private const double _t35Factor = 0.4308;

    // 一阶系统中响应到达85%时对应的归一化时间系数：-ln(1-0.85) = -ln(0.15) ≈ 1.8971
    private const double _t85Factor = 1.8971;

    // t85因子与t35因子之差，用于计算τ
    private const double _tauDivisor = _t85Factor - _t35Factor; // ≈ 1.4663

    /// <summary>
    /// 根据阶跃响应的历史时序样本估算 PID 参数。
    /// </summary>
    /// <param name="samples">
    /// 按时间升序排列的时序样本（毫秒时间戳，过程变量数值）。
    /// 数据应覆盖完整的阶跃响应过程（含响应前稳态）。
    /// </param>
    /// <param name="options">估算选项；若为 <c>null</c> 则使用默认值。</param>
    /// <returns>估算得到的 PID 参数（Kp、Ki、Kd）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="samples"/> 为 null。</exception>
    /// <exception cref="ArgumentException">样本数量少于 10 个。</exception>
    /// <exception cref="InvalidOperationException">
    /// 过程响应未达到 35% 或 85% 稳态、阶跃幅度接近零，或 IMC λ 参数非正。
    /// </exception>
    public static PidParameters Estimate(
        IReadOnlyList<(long TimestampMs, double Value)> samples,
        PidEstimationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count < _minSampleCount)
            throw new ArgumentException(
                $"至少需要 {_minSampleCount} 个数据点，实际提供了 {samples.Count} 个。",
                nameof(samples));

        options ??= new PidEstimationOptions();
        ValidateOptions(options);

        var (fopdtK, fopdtTau, fopdtTheta) = IdentifyFopdt(samples, options);
        return ComputePidParameters(fopdtK, fopdtTau, fopdtTheta, options);
    }

    /// <summary>
    /// 根据 <see cref="DataPoint"/> 序列估算 PID 参数（便捷重载）。
    /// </summary>
    /// <param name="samples">
    /// 按时间升序排列的数据点序列，值类型须为 Float64 或 Int64。
    /// </param>
    /// <param name="options">估算选项；若为 <c>null</c> 则使用默认值。</param>
    /// <returns>估算得到的 PID 参数（Kp、Ki、Kd）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="samples"/> 为 null。</exception>
    /// <exception cref="ArgumentException">样本数量不足，或存在非数值类型的字段值。</exception>
    /// <exception cref="InvalidOperationException">过程响应不满足 FOPDT 辨识条件。</exception>
    public static PidParameters Estimate(
        IReadOnlyList<DataPoint> samples,
        PidEstimationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var converted = new (long TimestampMs, double Value)[samples.Count];
        for (int i = 0; i < samples.Count; i++)
        {
            if (!samples[i].Value.TryGetNumeric(out double v))
                throw new ArgumentException(
                    $"索引 {i} 处的数据点值类型 {samples[i].Value.Type} 不可转换为数值。",
                    nameof(samples));

            converted[i] = (samples[i].Timestamp, v);
        }

        return Estimate(converted, options);
    }

    // ── 参数校验 ────────────────────────────────────────────────────────────

    private static void ValidateOptions(PidEstimationOptions options)
    {
        if (options.InitialFraction <= 0.0 || options.InitialFraction >= 0.5)
            throw new ArgumentOutOfRangeException(
                nameof(options), options.InitialFraction,
                "InitialFraction 必须在 (0, 0.5) 范围内。");

        if (options.FinalFraction <= 0.0 || options.FinalFraction >= 0.5)
            throw new ArgumentOutOfRangeException(
                nameof(options), options.FinalFraction,
                "FinalFraction 必须在 (0, 0.5) 范围内。");

        if (options.StepMagnitude.HasValue && options.StepMagnitude.Value == 0.0)
            throw new ArgumentOutOfRangeException(
                nameof(options), options.StepMagnitude.Value,
                "StepMagnitude 不得为零。");

        if (options.ImcLambda.HasValue && options.ImcLambda.Value <= 0.0)
            throw new ArgumentOutOfRangeException(
                nameof(options), options.ImcLambda.Value,
                "ImcLambda 必须大于零。");
    }

    // ── FOPDT 辨识 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 从阶跃响应数据中辨识 FOPDT 模型参数（K, τ, θ）。
    /// 采用 Sundaresan &amp; Krishnaswamy（1977）35%/85% 两点法。
    /// </summary>
    private static (double K, double Tau, double Theta) IdentifyFopdt(
        IReadOnlyList<(long TimestampMs, double Value)> samples,
        PidEstimationOptions options)
    {
        int n = samples.Count;
        int initialCount = Math.Max(1, (int)(n * options.InitialFraction));
        int finalCount = Math.Max(1, (int)(n * options.FinalFraction));

        double yInitial = ComputeMean(samples, 0, initialCount);
        double yFinal = ComputeMean(samples, n - finalCount, finalCount);
        double deltaY = yFinal - yInitial;

        if (Math.Abs(deltaY) < 1e-12)
            throw new InvalidOperationException(
                "阶跃幅度接近于零，无法辨识 FOPDT 模型。" +
                "请确认输入数据包含完整的阶跃响应过程（初始稳态与最终稳态之间有明显变化）。");

        double stepMagnitude = options.StepMagnitude ?? 1.0;
        double K = deltaY / stepMagnitude;

        bool rising = deltaY > 0;
        double y35 = yInitial + 0.35 * deltaY;
        double y85 = yInitial + 0.85 * deltaY;

        // 找到响应到达35%和85%的插值时刻（相对于t[0]，ms）
        double? t35Ms = FindCrossingTime(samples, y35, rising);
        double? t85Ms = FindCrossingTime(samples, y85, rising);

        if (t35Ms is null)
            throw new InvalidOperationException(
                "过程响应未达到 35% 稳态，无法辨识 FOPDT 模型。" +
                "请提供覆盖完整阶跃响应的数据，或调整 FinalFraction 选项。");

        if (t85Ms is null)
            throw new InvalidOperationException(
                "过程响应未达到 85% 稳态，无法辨识 FOPDT 模型。" +
                "请提供覆盖完整阶跃响应的数据，或调整 FinalFraction 选项。");

        // 两点法公式
        double tau = (t85Ms.Value - t35Ms.Value) / _tauDivisor;
        double theta = t35Ms.Value - tau * _t35Factor;

        // θ 不能为负（数据噪声或模型不匹配时的保护）
        if (theta < 0.0)
        {
            // 退化为最小采样间隔
            theta = samples[1].TimestampMs - samples[0].TimestampMs;
        }

        return (K, tau, theta);
    }

    /// <summary>
    /// 在时序数据中通过线性插值找到第一次穿越 <paramref name="targetY"/> 的时刻。
    /// </summary>
    /// <returns>
    /// 相对于 <c>samples[0].TimestampMs</c> 的经过时间（ms）；未穿越则返回 <c>null</c>。
    /// </returns>
    private static double? FindCrossingTime(
        IReadOnlyList<(long TimestampMs, double Value)> samples,
        double targetY,
        bool rising)
    {
        long t0 = samples[0].TimestampMs;
        for (int i = 1; i < samples.Count; i++)
        {
            double prev = samples[i - 1].Value;
            double curr = samples[i].Value;

            bool crossed = rising
                ? prev < targetY && curr >= targetY
                : prev > targetY && curr <= targetY;

            if (!crossed)
                continue;

            // 线性插值
            double fraction = (targetY - prev) / (curr - prev);
            double elapsedMs = (samples[i - 1].TimestampMs - t0)
                               + fraction * (samples[i].TimestampMs - samples[i - 1].TimestampMs);
            return elapsedMs;
        }

        return null;
    }

    /// <summary>计算样本子集的算术平均值。</summary>
    private static double ComputeMean(
        IReadOnlyList<(long TimestampMs, double Value)> samples,
        int start,
        int count)
    {
        double sum = 0.0;
        for (int i = start; i < start + count; i++)
            sum += samples[i].Value;
        return sum / count;
    }

    // ── 整定规则 ────────────────────────────────────────────────────────────

    private static PidParameters ComputePidParameters(
        double K,
        double tau,
        double theta,
        PidEstimationOptions options)
    {
        return options.Method switch
        {
            PidTuningMethod.ZieglerNichols => ComputeZieglerNichols(K, tau, theta),
            PidTuningMethod.CohenCoon => ComputeCohenCoon(K, tau, theta),
            PidTuningMethod.Imc => ComputeImc(K, tau, theta, options.ImcLambda),
            _ => throw new ArgumentOutOfRangeException(
                     nameof(options), options.Method,
                     $"不支持的整定方法：{options.Method}。")
        };
    }

    /// <summary>
    /// Ziegler-Nichols 阶跃响应法整定（1942）。
    /// <para>公式：Kp = 1.2τ/(Kθ)，Ti = 2θ，Td = 0.5θ。</para>
    /// </summary>
    private static PidParameters ComputeZieglerNichols(double K, double tau, double theta)
    {
        double kp = 1.2 * tau / (K * theta);
        double ti = 2.0 * theta;
        double td = 0.5 * theta;
        return new PidParameters(kp, kp / ti, kp * td);
    }

    /// <summary>
    /// Cohen-Coon 整定法（1953）。
    /// <para>
    /// 公式：Kp = (τ/(Kθ))·(4/3 + θ/(4τ))，Ti = θ·(32+6r)/(13+8r)，Td = 4θ/(11+2r)，
    /// 其中 r = θ/τ。
    /// </para>
    /// </summary>
    private static PidParameters ComputeCohenCoon(double K, double tau, double theta)
    {
        double r = theta / tau;
        double kp = (tau / (K * theta)) * (4.0 / 3.0 + r / 4.0);
        double ti = theta * (32.0 + 6.0 * r) / (13.0 + 8.0 * r);
        double td = 4.0 * theta / (11.0 + 2.0 * r);
        return new PidParameters(kp, kp / ti, kp * td);
    }

    /// <summary>
    /// IMC-PID 整定（Skogestad SIMC 规则，2003）。
    /// <para>
    /// 公式：Kp = (τ + θ/2)/(K·(λ + θ/2))，Ti = τ + θ/2，Td = τθ/(2τ + θ)。
    /// 默认 λ = θ（紧凑整定）；增大 λ 可获得更平稳、鲁棒性更高的响应。
    /// </para>
    /// </summary>
    private static PidParameters ComputeImc(double K, double tau, double theta, double? lambda)
    {
        double lam = lambda ?? theta;
        double tauEff = tau + theta / 2.0;
        double kp = tauEff / (K * (lam + theta / 2.0));
        double ti = tauEff;
        double td = tau * theta / (2.0 * tau + theta);
        return new PidParameters(kp, kp / ti, kp * td);
    }
}
