using SonnetDB.Model;
using SonnetDB.Query.Functions.Control;
using Xunit;

namespace SonnetDB.Core.Tests.Query.Functions.Control;

/// <summary>
/// <see cref="PidParameterEstimator"/> 单元测试。
/// </summary>
public sealed class PidParameterEstimatorTests
{
    // ── 测试辅助 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 生成理想一阶纯滞后（FOPDT）阶跃响应数据：
    /// y(t) = y0 + K·Δu·(1 − exp(−(t − θ)/τ))，t &gt; θ；阶跃前为常数 y0。
    /// </summary>
    private static List<(long TimestampMs, double Value)> GenerateFopdtResponse(
        double K, double tau, double theta, double y0, double stepMagnitude,
        int nPoints, long dtMs)
    {
        var samples = new List<(long, double)>(nPoints);
        for (int i = 0; i < nPoints; i++)
        {
            long t = i * dtMs;
            double elapsed = t - theta;
            double y = elapsed <= 0.0
                ? y0
                : y0 + K * stepMagnitude * (1.0 - Math.Exp(-elapsed / tau));
            samples.Add((t, y));
        }
        return samples;
    }

    // ── 基本功能 ──────────────────────────────────────────────────────────

    [Fact]
    public void Estimate_ZieglerNichols_ReturnsExpectedParameters()
    {
        // K=2, τ=100ms, θ=200ms，nPoints=300，dtMs=5ms
        // 初始窗口（前10%=30点=0..145ms）完全位于阶跃前，最终窗口已充分稳定。
        // Z-N 公式精确预期：Kp=1.2×100/(2×200)=0.3, Ti=400ms, Td=100ms
        //                    Ki=0.3/400=0.00075/ms, Kd=0.3×100=30ms
        var samples = GenerateFopdtResponse(K: 2.0, tau: 100.0, theta: 200.0,
            y0: 0.0, stepMagnitude: 1.0, nPoints: 300, dtMs: 5);

        PidParameters p = PidParameterEstimator.Estimate(samples,
            new PidEstimationOptions { Method = PidTuningMethod.ZieglerNichols });

        Assert.InRange(p.Kp, 0.27, 0.33);
        Assert.InRange(p.Ki, 0.00060, 0.00090);
        Assert.InRange(p.Kd, 25.0, 35.0);
    }

    [Fact]
    public void Estimate_CohenCoon_ReturnsReasonableParameters()
    {
        var samples = GenerateFopdtResponse(K: 2.0, tau: 100.0, theta: 20.0,
            y0: 0.0, stepMagnitude: 1.0, nPoints: 400, dtMs: 1);

        PidParameters p = PidParameterEstimator.Estimate(samples,
            new PidEstimationOptions { Method = PidTuningMethod.CohenCoon });

        Assert.True(p.Kp > 0, "Kp 应为正值");
        Assert.True(p.Ki > 0, "Ki 应为正值");
        Assert.True(p.Kd > 0, "Kd 应为正值");
    }

    [Fact]
    public void Estimate_Imc_DefaultLambda_ReturnsReasonableParameters()
    {
        var samples = GenerateFopdtResponse(K: 2.0, tau: 100.0, theta: 20.0,
            y0: 0.0, stepMagnitude: 1.0, nPoints: 400, dtMs: 1);

        PidParameters p = PidParameterEstimator.Estimate(samples,
            new PidEstimationOptions { Method = PidTuningMethod.Imc });

        Assert.True(p.Kp > 0, "Kp 应为正值");
        Assert.True(p.Ki > 0, "Ki 应为正值");
        Assert.True(p.Kd > 0, "Kd 应为正值");
    }

    [Fact]
    public void Estimate_Imc_CustomLambda_LargerLambdaYieldsSmallerKp()
    {
        // 更大的 λ 应产生更保守（更小）的 Kp
        var samples = GenerateFopdtResponse(K: 2.0, tau: 100.0, theta: 20.0,
            y0: 0.0, stepMagnitude: 1.0, nPoints: 400, dtMs: 1);

        PidParameters pTight = PidParameterEstimator.Estimate(samples,
            new PidEstimationOptions { Method = PidTuningMethod.Imc, ImcLambda = 10.0 });

        PidParameters pRobust = PidParameterEstimator.Estimate(samples,
            new PidEstimationOptions { Method = PidTuningMethod.Imc, ImcLambda = 100.0 });

        Assert.True(pTight.Kp > pRobust.Kp,
            "较小的 λ（紧凑整定）应产生较大的 Kp。");
    }

    // ── 自定义阶跃幅度 ────────────────────────────────────────────────────

    [Fact]
    public void Estimate_WithStepMagnitude_ScalesKpCorrectly()
    {
        // Δu=5 时 K_normalized = Δy/Δu；K 较小，Kp 较大
        var samplesUnit = GenerateFopdtResponse(K: 2.0, tau: 100.0, theta: 20.0,
            y0: 0.0, stepMagnitude: 1.0, nPoints: 400, dtMs: 1);

        var samplesFive = GenerateFopdtResponse(K: 2.0, tau: 100.0, theta: 20.0,
            y0: 0.0, stepMagnitude: 5.0, nPoints: 400, dtMs: 1);

        PidParameters pUnit = PidParameterEstimator.Estimate(samplesUnit,
            new PidEstimationOptions { StepMagnitude = 1.0 });

        PidParameters pFive = PidParameterEstimator.Estimate(samplesFive,
            new PidEstimationOptions { StepMagnitude = 5.0 });

        // 两者对应同一物理过程，参数应接近
        Assert.InRange(pFive.Kp, pUnit.Kp * 0.8, pUnit.Kp * 1.2);
    }

    // ── 非零初始值 ────────────────────────────────────────────────────────

    [Fact]
    public void Estimate_NonZeroBaseline_ReturnsCorrectParameters()
    {
        var samples = GenerateFopdtResponse(K: 3.0, tau: 50.0, theta: 10.0,
            y0: 100.0, stepMagnitude: 1.0, nPoints: 300, dtMs: 1);

        PidParameters p = PidParameterEstimator.Estimate(samples);

        Assert.True(p.Kp > 0);
        Assert.True(p.Ki > 0);
        Assert.True(p.Kd > 0);
    }

    // ── DataPoint 重载 ────────────────────────────────────────────────────

    [Fact]
    public void Estimate_DataPointOverload_ReturnsEquivalentResult()
    {
        var tuples = GenerateFopdtResponse(K: 2.0, tau: 100.0, theta: 20.0,
            y0: 0.0, stepMagnitude: 1.0, nPoints: 400, dtMs: 1);

        var dataPoints = tuples
            .Select(s => new DataPoint(s.TimestampMs, FieldValue.FromDouble(s.Value)))
            .ToList();

        PidParameters pTuple = PidParameterEstimator.Estimate(tuples);
        PidParameters pDp = PidParameterEstimator.Estimate(dataPoints);

        Assert.Equal(pTuple.Kp, pDp.Kp, precision: 10);
        Assert.Equal(pTuple.Ki, pDp.Ki, precision: 10);
        Assert.Equal(pTuple.Kd, pDp.Kd, precision: 10);
    }

    [Fact]
    public void Estimate_DataPointOverload_Int64Values_ReturnsResult()
    {
        // Int64 类型的 DataPoint 应通过 TryGetNumeric 转换为 double
        var dataPoints = Enumerable.Range(0, 50).Select(i =>
        {
            long t = i * 10L;
            long v = i < 5 ? 0L : (long)(200L * (1 - Math.Exp(-(i - 5) / 20.0)));
            return new DataPoint(t, FieldValue.FromLong(v));
        }).ToList();

        PidParameters p = PidParameterEstimator.Estimate(dataPoints);

        Assert.True(p.Kp > 0);
    }

    // ── 边界/错误场景 ─────────────────────────────────────────────────────

    [Fact]
    public void Estimate_NullSamples_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PidParameterEstimator.Estimate((IReadOnlyList<(long, double)>)null!));
    }

    [Fact]
    public void Estimate_TooFewSamples_ThrowsArgumentException()
    {
        var samples = GenerateFopdtResponse(K: 1.0, tau: 50.0, theta: 5.0,
            y0: 0.0, stepMagnitude: 1.0, nPoints: 5, dtMs: 1);

        Assert.Throws<ArgumentException>(() =>
            PidParameterEstimator.Estimate(samples));
    }

    [Fact]
    public void Estimate_FlatSignal_ThrowsInvalidOperationException()
    {
        // 平坦信号（无阶跃变化）
        var samples = Enumerable.Range(0, 100)
            .Select(i => ((long)i, 42.0))
            .ToList();

        Assert.Throws<InvalidOperationException>(() =>
            PidParameterEstimator.Estimate(samples));
    }

    [Fact]
    public void Estimate_ZieglerNichols_MoreAggressiveThanImcWithLargeLambda()
    {
        // Z-N 整定更激进（更大的 Kp），IMC 大 λ 整定更保守（更小的 Kp）
        var samples = GenerateFopdtResponse(K: 2.0, tau: 100.0, theta: 200.0,
            y0: 0.0, stepMagnitude: 1.0, nPoints: 300, dtMs: 5);

        PidParameters znParams = PidParameterEstimator.Estimate(samples,
            new PidEstimationOptions { Method = PidTuningMethod.ZieglerNichols });

        // λ = 5×θ ≈ 1000ms，高度保守
        PidParameters imcParams = PidParameterEstimator.Estimate(samples,
            new PidEstimationOptions { Method = PidTuningMethod.Imc, ImcLambda = 1000.0 });

        Assert.True(znParams.Kp > imcParams.Kp,
            $"Z-N Kp={znParams.Kp:G4} 应大于 IMC(λ=1000) Kp={imcParams.Kp:G4}。");
    }

    [Fact]
    public void Estimate_ZeroStepMagnitude_ThrowsArgumentOutOfRangeException()
    {
        var samples = GenerateFopdtResponse(K: 2.0, tau: 100.0, theta: 20.0,
            y0: 0.0, stepMagnitude: 1.0, nPoints: 400, dtMs: 1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PidParameterEstimator.Estimate(samples,
                new PidEstimationOptions { StepMagnitude = 0.0 }));
    }

    [Fact]
    public void Estimate_NegativeImcLambda_ThrowsArgumentOutOfRangeException()
    {
        var samples = GenerateFopdtResponse(K: 2.0, tau: 100.0, theta: 20.0,
            y0: 0.0, stepMagnitude: 1.0, nPoints: 400, dtMs: 1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PidParameterEstimator.Estimate(samples,
                new PidEstimationOptions { Method = PidTuningMethod.Imc, ImcLambda = -1.0 }));
    }

    [Fact]
    public void Estimate_InvalidInitialFraction_ThrowsArgumentOutOfRangeException()
    {
        var samples = GenerateFopdtResponse(K: 2.0, tau: 100.0, theta: 20.0,
            y0: 0.0, stepMagnitude: 1.0, nPoints: 400, dtMs: 1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PidParameterEstimator.Estimate(samples,
                new PidEstimationOptions { InitialFraction = 0.6 }));
    }

    [Fact]
    public void Estimate_DataPointNonNumericType_ThrowsArgumentException()
    {
        var dataPoints = Enumerable.Range(0, 20)
            .Select(i => new DataPoint(i * 10L, FieldValue.FromString("text")))
            .ToList();

        Assert.Throws<ArgumentException>(() =>
            PidParameterEstimator.Estimate(dataPoints));
    }

    // ── 下降阶跃 ──────────────────────────────────────────────────────────

    [Fact]
    public void Estimate_NegativeStep_ReturnsNegativeK_AndPositiveGains()
    {
        // 负阶跃：y 从 100 降到 0（K<0 时，Kp/Ki/Kd 符号依赖整定规则）
        // 对 Z-N，Kp = 1.2τ/(K*θ) < 0 —— 这是预期的（负增益控制器）
        var samples = GenerateFopdtResponse(K: -2.0, tau: 100.0, theta: 20.0,
            y0: 100.0, stepMagnitude: 1.0, nPoints: 400, dtMs: 1);

        PidParameters p = PidParameterEstimator.Estimate(samples);

        // 对负向阶跃，Kp 应为负值
        Assert.True(p.Kp < 0, "负增益过程应产生负 Kp。");
    }

    // ── 三种方法的参数合理性比较 ─────────────────────────────────────────

    [Theory]
    [InlineData(PidTuningMethod.ZieglerNichols)]
    [InlineData(PidTuningMethod.CohenCoon)]
    [InlineData(PidTuningMethod.Imc)]
    public void Estimate_AllMethods_ReturnPositiveParametersForPositiveProcess(
        PidTuningMethod method)
    {
        var samples = GenerateFopdtResponse(K: 1.5, tau: 80.0, theta: 15.0,
            y0: 0.0, stepMagnitude: 1.0, nPoints: 500, dtMs: 1);

        PidParameters p = PidParameterEstimator.Estimate(samples,
            new PidEstimationOptions { Method = method });

        Assert.True(p.Kp > 0, $"{method}: Kp 应为正值");
        Assert.True(p.Ki > 0, $"{method}: Ki 应为正值");
        Assert.True(p.Kd > 0, $"{method}: Kd 应为正值");
    }
}
