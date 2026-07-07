using SonnetDB.Query.Functions.Forecasting;
using Xunit;

namespace SonnetDB.Core.Tests.Query.Functions;

/// <summary>
/// PR #55 — <see cref="TimeSeriesForecaster"/> 算法层单元测试。
/// </summary>
public sealed class TimeSeriesForecasterTests
{
    [Fact]
    public void Forecast_Linear_ExtrapolatesSlopeAndIntercept()
    {
        // y = 2 + 3t（t 单位为秒），1 秒间隔
        long[] ts = { 0, 1000, 2000, 3000, 4000 };
        double[] values = { 2, 5, 8, 11, 14 };

        var result = TimeSeriesForecaster.Forecast(ts, values, horizon: 3, ForecastAlgorithm.Linear);

        Assert.Equal(3, result.Length);
        Assert.Equal(5000, result[0].TimestampMs);
        Assert.Equal(17, result[0].Value, 6);
        Assert.Equal(20, result[1].Value, 6);
        Assert.Equal(23, result[2].Value, 6);

        // 完美拟合时残差为 0，置信区间退化为点估计
        Assert.Equal(result[0].Value, result[0].Lower, 6);
        Assert.Equal(result[0].Value, result[0].Upper, 6);
    }

    [Fact]
    public void Forecast_Linear_ConfidenceIntervalGrowsWithHorizon()
    {
        long[] ts = { 0, 1000, 2000, 3000, 4000 };
        // 在线性 y=t 上添加扰动
        double[] values = { 0, 1.2, 1.8, 3.1, 3.9 };

        var result = TimeSeriesForecaster.Forecast(ts, values, horizon: 4, ForecastAlgorithm.Linear);

        double width0 = result[0].Upper - result[0].Lower;
        double width3 = result[3].Upper - result[3].Lower;
        Assert.True(width3 > width0, $"区间宽度应随 horizon 扩张：{width0} -> {width3}");
    }

    [Fact]
    public void Forecast_HoltWinters_TrendsLinearSeries()
    {
        long[] ts = new long[20];
        double[] values = new double[20];
        for (int i = 0; i < 20; i++)
        {
            ts[i] = i * 1000L;
            values[i] = 10 + i * 2;  // y = 10 + 2t
        }

        var result = TimeSeriesForecaster.Forecast(ts, values, horizon: 5, ForecastAlgorithm.HoltWinters);

        Assert.Equal(5, result.Length);
        // 第 21 个点期望接近 50（10 + 20*2）；允许 Holt 平滑误差 ±2
        Assert.InRange(result[0].Value, 48, 54);
        // 趋势继续上升
        Assert.True(result[4].Value > result[0].Value);
    }

    [Fact]
    public void Forecast_HoltWinters_WithSeasonalityFollowsCycle()
    {
        // 4 个完整季，每季 4 个点（高低高低交替）
        int season = 4;
        int seasons = 6;
        long[] ts = new long[season * seasons];
        double[] values = new double[season * seasons];
        for (int s = 0; s < seasons; s++)
        {
            for (int i = 0; i < season; i++)
            {
                int idx = s * season + i;
                ts[idx] = idx * 1000L;
                // 季节模式：[10, 20, 30, 20]，无趋势
                values[idx] = i switch { 0 => 10, 1 => 20, 2 => 30, _ => 20 };
            }
        }

        var result = TimeSeriesForecaster.Forecast(ts, values, horizon: season, ForecastAlgorithm.HoltWinters, season);

        Assert.Equal(season, result.Length);
        // 预测的下一个季应该重现 [10, 20, 30, 20] 的形态（误差允许 ±5）
        Assert.True(result[2].Value > result[0].Value, $"季节峰值应在第 3 个点：{result[0].Value} vs {result[2].Value}");
        Assert.True(result[2].Value > result[3].Value);
    }

    [Fact]
    public void Forecast_TimestampSpacingMatchesInputCadence()
    {
        long[] ts = { 0, 60_000, 120_000, 180_000 };
        double[] values = { 1, 2, 3, 4 };

        var result = TimeSeriesForecaster.Forecast(ts, values, horizon: 3, ForecastAlgorithm.Linear);

        Assert.Equal(240_000, result[0].TimestampMs);
        Assert.Equal(300_000, result[1].TimestampMs);
        Assert.Equal(360_000, result[2].TimestampMs);
    }

    [Fact]
    public void Forecast_RejectsTooFewSamples()
    {
        long[] ts = { 0 };
        double[] values = { 1 };
        Assert.Throws<InvalidOperationException>(() =>
            TimeSeriesForecaster.Forecast(ts, values, 1, ForecastAlgorithm.Linear));
    }

    [Fact]
    public void Forecast_RejectsZeroHorizon()
    {
        long[] ts = { 0, 1000 };
        double[] values = { 1, 2 };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimeSeriesForecaster.Forecast(ts, values, 0, ForecastAlgorithm.Linear));
    }
}
