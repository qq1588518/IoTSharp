using SonnetDB.Model;
using SonnetDB.Query.Functions.Window;
using Xunit;

namespace SonnetDB.Core.Tests.Query.Functions;

/// <summary>
/// PR #55 — Tier 4 异常 / 变点检测窗口函数 evaluator 的纯算法单元测试。
/// </summary>
public sealed class AnomalyChangepointFunctionTests
{
    private static FieldValue?[] D(params double?[] values)
    {
        var result = new FieldValue?[values.Length];
        for (int i = 0; i < values.Length; i++)
            result[i] = values[i] is { } v ? FieldValue.FromDouble(v) : null;
        return result;
    }

    private static long[] T(int count)
    {
        var t = new long[count];
        for (int i = 0; i < count; i++) t[i] = i * 1000L;
        return t;
    }

    // ── anomaly ───────────────────────────────────────────────────────────

    [Fact]
    public void AnomalyEvaluator_ZScore_FlagsLargeDeviation()
    {
        // 注意：离群点本身会拉大 stddev，因此 z-score 阈值通常取 2.0~3.0；
        // MAD 方法对此更鲁棒，见下方测试。
        var ev = new AnomalyEvaluator("x", AnomalyMethod.ZScore, threshold: 2.0);
        var values = D(10, 11, 9, 10, 12, 100, 11, 9);
        var result = ev.Compute(T(values.Length), values);

        // 100 是显著的离群点
        Assert.True((bool)result[5]!);
        // 其余点为 false
        for (int i = 0; i < result.Length; i++)
        {
            if (i == 5) continue;
            Assert.False((bool)result[i]!);
        }
    }

    [Fact]
    public void AnomalyEvaluator_ZScore_NullInputProducesNull()
    {
        var ev = new AnomalyEvaluator("x", AnomalyMethod.ZScore, threshold: 1.5);
        var values = D(10, null, 11, 12, 50, 9);
        var result = ev.Compute(T(values.Length), values);
        Assert.Null(result[1]);
        Assert.True((bool)result[4]!);
    }

    [Fact]
    public void AnomalyEvaluator_Mad_FlagsLargeDeviation()
    {
        var ev = new AnomalyEvaluator("x", AnomalyMethod.Mad, threshold: 3.0);
        var values = D(10, 11, 10, 9, 11, 10, 200, 10);
        var result = ev.Compute(T(values.Length), values);

        Assert.True((bool)result[6]!);
        Assert.False((bool)result[0]!);
    }

    [Fact]
    public void AnomalyEvaluator_Iqr_FlagsOutsideFence()
    {
        var ev = new AnomalyEvaluator("x", AnomalyMethod.Iqr, threshold: 1.5);
        var values = D(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 100);
        var result = ev.Compute(T(values.Length), values);
        Assert.True((bool)result[10]!);
        Assert.False((bool)result[5]!);
    }

    [Fact]
    public void AnomalyEvaluator_ConstantSeries_NoAnomaly()
    {
        var ev = new AnomalyEvaluator("x", AnomalyMethod.ZScore, threshold: 2.0);
        var values = D(5, 5, 5, 5, 5);
        var result = ev.Compute(T(values.Length), values);
        foreach (var r in result) Assert.False((bool)r!);
    }

    // ── changepoint ───────────────────────────────────────────────────────

    [Fact]
    public void ChangepointEvaluator_Cusum_DetectsLevelShift()
    {
        var ev = new ChangepointEvaluator("x", ChangepointMethod.Cusum, threshold: 3.0, drift: 0.5);
        // 前 10 点均值 ~10，后 10 点跳到 ~20
        var values = D(
            10, 10.2, 9.8, 10.1, 10, 9.9, 10.3, 10, 9.7, 10.1,
            20, 20.1, 19.9, 20.2, 20, 19.8, 20.1, 19.9, 20, 20.1);
        var result = ev.Compute(T(values.Length), values);

        // 前 10 点不应触发
        for (int i = 0; i < 10; i++)
            Assert.False((bool)result[i]!);

        // 跳变后 5 个点内必须有触发
        bool fired = false;
        for (int i = 10; i < 15; i++)
            if ((bool)result[i]!) fired = true;
        Assert.True(fired);
    }

    [Fact]
    public void ChangepointEvaluator_StableSeries_NoChangepoint()
    {
        var ev = new ChangepointEvaluator("x", ChangepointMethod.Cusum, threshold: 5.0, drift: 0.5);
        var values = D(10, 10.1, 9.9, 10.05, 9.95, 10.02, 9.98, 10.0);
        var result = ev.Compute(T(values.Length), values);
        foreach (var r in result) Assert.False((bool)r!);
    }

    [Fact]
    public void ChangepointEvaluator_NullInputProducesNull()
    {
        var ev = new ChangepointEvaluator("x", ChangepointMethod.Cusum, threshold: 3.0, drift: 0.5);
        var values = D(10, null, 10, 20, 20);
        var result = ev.Compute(T(values.Length), values);
        Assert.Null(result[1]);
    }
}
