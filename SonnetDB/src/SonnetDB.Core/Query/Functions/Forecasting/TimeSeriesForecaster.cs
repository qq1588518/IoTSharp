namespace SonnetDB.Query.Functions.Forecasting;

/// <summary>预测算法。</summary>
public enum ForecastAlgorithm
{
    /// <summary>简单线性外推（最小二乘拟合）。</summary>
    Linear,
    /// <summary>Holt-Winters 三参数（加法）模型；当 <c>season &lt;= 1</c> 时退化为 Holt 双指数平滑。</summary>
    HoltWinters,
}

/// <summary>预测一行结果。</summary>
/// <param name="TimestampMs">预测点时间戳（毫秒）。</param>
/// <param name="Value">预测中心值。</param>
/// <param name="Lower">95% 置信下界。</param>
/// <param name="Upper">95% 置信上界。</param>
public readonly record struct ForecastPoint(long TimestampMs, double Value, double Lower, double Upper);

/// <summary>
/// 纯 C# 时序预测器：内置 <see cref="ForecastAlgorithm.Linear"/> 与
/// <see cref="ForecastAlgorithm.HoltWinters"/> 两种算法，支持 95% 置信区间，
/// 不依赖任何第三方库。
/// </summary>
public static class TimeSeriesForecaster
{
    /// <summary>正态分布 95% 双侧分位点。</summary>
    private const double _z95 = 1.959963984540054;

    /// <summary>
    /// 对一段按时间递增排列的样本进行预测。
    /// </summary>
    /// <param name="timestampsMs">观测时间戳（毫秒），必须严格递增且至少含 2 个点。</param>
    /// <param name="values">观测值；与 <paramref name="timestampsMs"/> 等长，可包含 <c>NaN</c> 表示缺失。</param>
    /// <param name="horizon">要外推的点数，必须 &gt; 0。</param>
    /// <param name="algorithm">所选算法。</param>
    /// <param name="season">季节长度（点数）；仅 <see cref="ForecastAlgorithm.HoltWinters"/> 使用，&lt;= 1 表示无季节。</param>
    /// <returns>长度为 <paramref name="horizon"/> 的预测序列。</returns>
    public static ForecastPoint[] Forecast(
        long[] timestampsMs,
        double[] values,
        int horizon,
        ForecastAlgorithm algorithm,
        int season = 0)
    {
        ArgumentNullException.ThrowIfNull(timestampsMs);
        ArgumentNullException.ThrowIfNull(values);
        if (timestampsMs.Length != values.Length)
            throw new ArgumentException("timestampsMs 与 values 长度不一致。", nameof(values));
        if (timestampsMs.Length < 2)
            throw new InvalidOperationException("forecast 至少需要 2 个观测点。");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(horizon);

        long stepMs = EstimateStepMs(timestampsMs);
        long lastTs = timestampsMs[^1];

        return algorithm switch
        {
            ForecastAlgorithm.Linear => ForecastLinear(timestampsMs, values, horizon, lastTs, stepMs),
            ForecastAlgorithm.HoltWinters => ForecastHoltWinters(values, horizon, lastTs, stepMs, season),
            _ => throw new InvalidOperationException($"未知预测算法 {algorithm}。"),
        };
    }

    /// <summary>估算样本平均时间间隔（毫秒）；至少返回 1。</summary>
    internal static long EstimateStepMs(long[] timestampsMs)
    {
        long total = timestampsMs[^1] - timestampsMs[0];
        long step = total / Math.Max(1, timestampsMs.Length - 1);
        return Math.Max(1, step);
    }

    private static ForecastPoint[] ForecastLinear(
        long[] timestampsMs,
        double[] values,
        int horizon,
        long lastTs,
        long stepMs)
    {
        // 最小二乘拟合 y = a + b*x，其中 x 为相对秒数。
        double t0 = timestampsMs[0];
        int n = 0;
        double sumX = 0, sumY = 0, sumXX = 0, sumXY = 0;
        for (int i = 0; i < values.Length; i++)
        {
            double y = values[i];
            if (double.IsNaN(y)) continue;
            double x = (timestampsMs[i] - t0) / 1000.0;
            sumX += x;
            sumY += y;
            sumXX += x * x;
            sumXY += x * y;
            n++;
        }

        if (n < 2)
            throw new InvalidOperationException("forecast(linear) 至少需要 2 个非空观测点。");

        double meanX = sumX / n;
        double meanY = sumY / n;
        double denom = sumXX - n * meanX * meanX;
        double slope = denom == 0 ? 0 : (sumXY - n * meanX * meanY) / denom;
        double intercept = meanY - slope * meanX;

        // 残差标准差用于置信区间
        double sse = 0;
        for (int i = 0; i < values.Length; i++)
        {
            double y = values[i];
            if (double.IsNaN(y)) continue;
            double x = (timestampsMs[i] - t0) / 1000.0;
            double yhat = intercept + slope * x;
            double e = y - yhat;
            sse += e * e;
        }
        double residualStd = Math.Sqrt(sse / Math.Max(1, n - 2));

        var points = new ForecastPoint[horizon];
        for (int h = 0; h < horizon; h++)
        {
            long ts = lastTs + stepMs * (h + 1);
            double x = (ts - t0) / 1000.0;
            double yhat = intercept + slope * x;
            // 简化：随步长 sqrt(h) 放宽区间
            double width = _z95 * residualStd * Math.Sqrt(h + 1);
            points[h] = new ForecastPoint(ts, yhat, yhat - width, yhat + width);
        }
        return points;
    }

    private static ForecastPoint[] ForecastHoltWinters(
        double[] values,
        int horizon,
        long lastTs,
        long stepMs,
        int season)
    {
        // 使用固定的稳健参数（避免引入梯度搜索，保持纯 C# 与 AOT 友好）。
        const double alpha = 0.4;
        const double beta = 0.1;
        const double gamma = 0.2;

        bool seasonal = season > 1 && values.Length >= 2 * season;

        // 初始化 level / trend / seasonals
        double level = double.NaN;
        double trend = 0;
        double[]? seasonals = null;

        if (seasonal)
        {
            seasonals = InitSeasonals(values, season);
            // 用第一个完整季的均值作为 level，第二季 - 第一季均值差作为 trend 初值
            double firstSeasonAvg = SeasonAverage(values, 0, season);
            double secondSeasonAvg = SeasonAverage(values, season, season);
            level = firstSeasonAvg;
            trend = (secondSeasonAvg - firstSeasonAvg) / season;
        }

        // 单遍训练（残差平方和用于置信区间）
        double sse = 0;
        int residualCount = 0;
        for (int i = 0; i < values.Length; i++)
        {
            double y = values[i];
            if (double.IsNaN(y))
            {
                if (!double.IsNaN(level))
                {
                    // 缺失：不更新平滑量，但维持 level/trend 推进
                    level += trend;
                }
                continue;
            }

            if (double.IsNaN(level))
            {
                if (!seasonal)
                {
                    level = y;
                }
                continue;
            }

            double prevLevel = level;
            if (seasonal)
            {
                int s = i % season;
                double seasonal_i = seasonals![s];
                double yhat = (prevLevel + trend) + seasonal_i;
                double residual = y - yhat;
                sse += residual * residual;
                residualCount++;

                level = alpha * (y - seasonal_i) + (1 - alpha) * (prevLevel + trend);
                trend = beta * (level - prevLevel) + (1 - beta) * trend;
                seasonals[s] = gamma * (y - level) + (1 - gamma) * seasonal_i;
            }
            else
            {
                double yhat = prevLevel + trend;
                double residual = y - yhat;
                sse += residual * residual;
                residualCount++;

                if (residualCount == 1)
                {
                    // 初始 trend 用首两点差
                    trend = y - prevLevel;
                    level = y;
                }
                else
                {
                    level = alpha * y + (1 - alpha) * (prevLevel + trend);
                    trend = beta * (level - prevLevel) + (1 - beta) * trend;
                }
            }
        }

        if (double.IsNaN(level))
            throw new InvalidOperationException("forecast(holt_winters) 至少需要 2 个非空观测点。");

        double residualStd = residualCount > 1
            ? Math.Sqrt(sse / Math.Max(1, residualCount - 1))
            : 0;

        var points = new ForecastPoint[horizon];
        for (int h = 1; h <= horizon; h++)
        {
            double yhat = level + h * trend;
            if (seasonal)
            {
                int s = (values.Length + h - 1) % season;
                yhat += seasonals![s];
            }
            long ts = lastTs + stepMs * h;
            double width = _z95 * residualStd * Math.Sqrt(h);
            points[h - 1] = new ForecastPoint(ts, yhat, yhat - width, yhat + width);
        }
        return points;
    }

    private static double[] InitSeasonals(double[] values, int season)
    {
        int seasons = values.Length / season;
        var seasonAverages = new double[seasons];
        for (int s = 0; s < seasons; s++)
            seasonAverages[s] = SeasonAverage(values, s * season, season);

        var seasonals = new double[season];
        for (int i = 0; i < season; i++)
        {
            double sum = 0;
            int count = 0;
            for (int s = 0; s < seasons; s++)
            {
                double v = values[s * season + i];
                if (double.IsNaN(v)) continue;
                sum += v - seasonAverages[s];
                count++;
            }
            seasonals[i] = count > 0 ? sum / count : 0;
        }
        return seasonals;
    }

    private static double SeasonAverage(double[] values, int start, int season)
    {
        double sum = 0;
        int count = 0;
        for (int i = 0; i < season; i++)
        {
            double v = values[start + i];
            if (double.IsNaN(v)) continue;
            sum += v;
            count++;
        }
        return count > 0 ? sum / count : 0;
    }
}
