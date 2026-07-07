using SonnetDB.Catalog;
using SonnetDB.Model;
using SonnetDB.Sql.Ast;

namespace SonnetDB.Query.Functions.Window;

// ────────────────────────────────────────────────────────────────────────────
// 异常 / 变点检测：anomaly / changepoint（PR #55）
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>anomaly(field, 'method', threshold)</c>：基于全窗口统计量的离群点检测。
/// 输入逐 series 计算，输出与输入逐行对应的布尔值（true 表示该点被判定为异常）。
/// 缺失值输出 null。
/// <para>
/// 支持的方法：
/// <list type="bullet">
///   <item><c>'zscore'</c>：|x - mean| / stddev &gt; threshold；样本方差使用 N-1 分母。</item>
///   <item><c>'mad'</c>：|x - median| / (1.4826 * MAD) &gt; threshold；对极值更鲁棒。</item>
///   <item><c>'iqr'</c>：x &lt; Q1 - threshold * IQR 或 x &gt; Q3 + threshold * IQR；threshold 通常取 1.5。</item>
/// </list>
/// </para>
/// </summary>
internal sealed class AnomalyFunction : IWindowFunction
{
    /// <inheritdoc />
    public string Name => "anomaly";

    /// <inheritdoc />
    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 3, 3);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        var method = ResolveMethod(call.Arguments[1]);
        double threshold = WindowFunctionBinder.ResolveNumericArgument(call, 2, Name);
        if (!(threshold > 0))
            throw new InvalidOperationException(
                $"窗口函数 {Name} 的 threshold 必须 > 0，实际为 {threshold}。");
        return new AnomalyEvaluator(col.Name, method, threshold);
    }

    private static AnomalyMethod ResolveMethod(SqlExpression arg)
    {
        if (arg is not LiteralExpression { Kind: SqlLiteralKind.String, StringValue: { } s })
            throw new InvalidOperationException(
                "anomaly 第 2 个参数必须是字符串字面量 'zscore' / 'mad' / 'iqr'。");
        return s.ToLowerInvariant() switch
        {
            "zscore" or "z-score" or "z_score" => AnomalyMethod.ZScore,
            "mad" => AnomalyMethod.Mad,
            "iqr" => AnomalyMethod.Iqr,
            _ => throw new InvalidOperationException(
                $"anomaly 不支持方法 '{s}'，仅支持 'zscore' / 'mad' / 'iqr'。"),
        };
    }
}

/// <summary>异常检测算法。</summary>
internal enum AnomalyMethod
{
    /// <summary>Z-score。</summary>
    ZScore,
    /// <summary>Median Absolute Deviation。</summary>
    Mad,
    /// <summary>Inter-Quartile Range。</summary>
    Iqr,
}

internal sealed class AnomalyEvaluator : IWindowEvaluator
{
    private readonly AnomalyMethod _method;
    private readonly double _threshold;

    public AnomalyEvaluator(string fieldName, AnomalyMethod method, double threshold)
    {
        FieldName = fieldName;
        _method = method;
        _threshold = threshold;
    }

    /// <inheritdoc />
    public string FieldName { get; }

    /// <inheritdoc />
    public object?[] Compute(long[] timestamps, FieldValue?[] values)
    {
        int n = timestamps.Length;
        var output = new object?[n];

        // 收集非空数值
        var samples = new List<double>(n);
        for (int i = 0; i < n; i++)
        {
            if (WindowFunctionBinder.TryToDouble(values[i], out var v))
                samples.Add(v);
        }

        if (samples.Count < 2)
        {
            // 样本不足，所有非空点输出 false
            for (int i = 0; i < n; i++)
                output[i] = WindowFunctionBinder.TryToDouble(values[i], out _) ? (object)false : null;
            return output;
        }

        switch (_method)
        {
            case AnomalyMethod.ZScore:
                ComputeZScore(timestamps, values, samples, output);
                break;
            case AnomalyMethod.Mad:
                ComputeMad(timestamps, values, samples, output);
                break;
            case AnomalyMethod.Iqr:
                ComputeIqr(timestamps, values, samples, output);
                break;
            default:
                throw new InvalidOperationException($"未知 anomaly 方法 {_method}。");
        }

        return output;
    }

    private void ComputeZScore(long[] timestamps, FieldValue?[] values, List<double> samples, object?[] output)
    {
        double mean = 0;
        foreach (var v in samples) mean += v;
        mean /= samples.Count;

        double sumSq = 0;
        foreach (var v in samples)
        {
            double d = v - mean;
            sumSq += d * d;
        }
        double stddev = Math.Sqrt(sumSq / (samples.Count - 1));

        for (int i = 0; i < timestamps.Length; i++)
        {
            if (!WindowFunctionBinder.TryToDouble(values[i], out var x))
            {
                output[i] = null;
                continue;
            }

            if (stddev == 0)
            {
                output[i] = false;
                continue;
            }

            double z = Math.Abs(x - mean) / stddev;
            output[i] = z > _threshold;
        }
    }

    private void ComputeMad(long[] timestamps, FieldValue?[] values, List<double> samples, object?[] output)
    {
        double median = ComputeMedian(samples);

        // MAD = median(|xi - median|)
        var deviations = new List<double>(samples.Count);
        foreach (var v in samples)
            deviations.Add(Math.Abs(v - median));
        double mad = ComputeMedian(deviations);

        // 1.4826 是把 MAD 转为正态分布等效 stddev 的尺度因子
        double scale = 1.4826 * mad;

        for (int i = 0; i < timestamps.Length; i++)
        {
            if (!WindowFunctionBinder.TryToDouble(values[i], out var x))
            {
                output[i] = null;
                continue;
            }

            if (scale == 0)
            {
                output[i] = false;
                continue;
            }

            double score = Math.Abs(x - median) / scale;
            output[i] = score > _threshold;
        }
    }

    private void ComputeIqr(long[] timestamps, FieldValue?[] values, List<double> samples, object?[] output)
    {
        var sorted = new double[samples.Count];
        samples.CopyTo(sorted);
        Array.Sort(sorted);

        double q1 = Quantile(sorted, 0.25);
        double q3 = Quantile(sorted, 0.75);
        double iqr = q3 - q1;
        double lower = q1 - _threshold * iqr;
        double upper = q3 + _threshold * iqr;

        for (int i = 0; i < timestamps.Length; i++)
        {
            if (!WindowFunctionBinder.TryToDouble(values[i], out var x))
            {
                output[i] = null;
                continue;
            }

            if (iqr == 0)
            {
                output[i] = false;
                continue;
            }

            output[i] = x < lower || x > upper;
        }
    }

    private static double ComputeMedian(List<double> values)
    {
        var sorted = new double[values.Count];
        values.CopyTo(sorted);
        Array.Sort(sorted);
        int n = sorted.Length;
        if (n % 2 == 1) return sorted[n / 2];
        return (sorted[n / 2 - 1] + sorted[n / 2]) * 0.5;
    }

    /// <summary>线性插值分位数（与 NumPy 默认 'linear' 行为一致）。</summary>
    private static double Quantile(double[] sorted, double q)
    {
        int n = sorted.Length;
        if (n == 0) return 0;
        if (n == 1) return sorted[0];
        double pos = q * (n - 1);
        int idx = (int)Math.Floor(pos);
        double frac = pos - idx;
        if (idx + 1 >= n) return sorted[n - 1];
        return sorted[idx] + frac * (sorted[idx + 1] - sorted[idx]);
    }
}

/// <summary>
/// <c>changepoint(field, 'cusum', threshold[, drift])</c>：CUSUM 累积和变点检测。
/// 用前 <c>max(5, n/4)</c> 个非空样本估计基线均值与样本标准差 σ，然后对每个点计算
/// <c>S_t = max(0, S_{t-1} + (x_t - mean) - drift·σ)</c> 与对称下侧累积；
/// 当任意一支超过 <c>threshold·σ</c> 时输出 true 并重置累积器。
/// </summary>
/// <remarks>
/// 参数：
/// <list type="bullet">
///   <item><c>field</c>：FIELD 列。</item>
///   <item><c>'cusum'</c>：当前唯一支持的方法。</item>
///   <item><c>threshold</c>：触发阈值（单位：倍样本标准差），常用 3~5。</item>
///   <item><c>drift</c>：可选，敏感度上限（单位：倍样本标准差），默认 0.5。</item>
/// </list>
/// </remarks>
internal sealed class ChangepointFunction : IWindowFunction
{
    /// <inheritdoc />
    public string Name => "changepoint";

    /// <inheritdoc />
    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 3, 4);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        var method = ResolveMethod(call.Arguments[1]);
        double threshold = WindowFunctionBinder.ResolveNumericArgument(call, 2, Name);
        if (!(threshold > 0))
            throw new InvalidOperationException(
                $"窗口函数 {Name} 的 threshold 必须 > 0，实际为 {threshold}。");

        double drift = 0.5;
        if (call.Arguments.Count == 4)
        {
            drift = WindowFunctionBinder.ResolveNumericArgument(call, 3, Name);
            if (drift < 0)
                throw new InvalidOperationException(
                    $"窗口函数 {Name} 的 drift 必须 >= 0，实际为 {drift}。");
        }

        return new ChangepointEvaluator(col.Name, method, threshold, drift);
    }

    private static ChangepointMethod ResolveMethod(SqlExpression arg)
    {
        if (arg is not LiteralExpression { Kind: SqlLiteralKind.String, StringValue: { } s })
            throw new InvalidOperationException(
                "changepoint 第 2 个参数必须是字符串字面量 'cusum'。");
        return s.ToLowerInvariant() switch
        {
            "cusum" => ChangepointMethod.Cusum,
            _ => throw new InvalidOperationException(
                $"changepoint 不支持方法 '{s}'，仅支持 'cusum'。"),
        };
    }
}

/// <summary>变点检测算法。</summary>
internal enum ChangepointMethod
{
    /// <summary>双边累积和（Cumulative Sum）。</summary>
    Cusum,
}

internal sealed class ChangepointEvaluator : IWindowEvaluator
{
    private readonly ChangepointMethod _method;
    private readonly double _threshold;
    private readonly double _drift;

    public ChangepointEvaluator(string fieldName, ChangepointMethod method, double threshold, double drift)
    {
        FieldName = fieldName;
        _method = method;
        _threshold = threshold;
        _drift = drift;
    }

    /// <inheritdoc />
    public string FieldName { get; }

    /// <inheritdoc />
    public object?[] Compute(long[] timestamps, FieldValue?[] values)
    {
        if (_method != ChangepointMethod.Cusum)
            throw new InvalidOperationException($"未知 changepoint 方法 {_method}。");

        int n = timestamps.Length;
        var output = new object?[n];

        // 收集非空数值索引列表
        var sampleIdx = new List<int>(n);
        for (int i = 0; i < n; i++)
        {
            if (WindowFunctionBinder.TryToDouble(values[i], out _))
                sampleIdx.Add(i);
        }

        if (sampleIdx.Count < 4)
        {
            for (int i = 0; i < n; i++)
                output[i] = WindowFunctionBinder.TryToDouble(values[i], out _) ? (object)false : null;
            return output;
        }

        // 用前 warmup 个非空样本作为基线，避免变点本身污染均值 / 方差。
        int warmup = Math.Max(5, sampleIdx.Count / 4);
        if (warmup > sampleIdx.Count - 1) warmup = sampleIdx.Count - 1;

        double sum = 0;
        for (int j = 0; j < warmup; j++)
        {
            WindowFunctionBinder.TryToDouble(values[sampleIdx[j]], out var v);
            sum += v;
        }
        double mean = sum / warmup;

        double sumSq = 0;
        for (int j = 0; j < warmup; j++)
        {
            WindowFunctionBinder.TryToDouble(values[sampleIdx[j]], out var v);
            double d = v - mean;
            sumSq += d * d;
        }
        double sigma = warmup > 1 ? Math.Sqrt(sumSq / (warmup - 1)) : 0;

        if (sigma == 0)
        {
            for (int i = 0; i < n; i++)
                output[i] = WindowFunctionBinder.TryToDouble(values[i], out _) ? (object)false : null;
            return output;
        }

        double k = _drift * sigma;
        double h = _threshold * sigma;

        double sHi = 0;
        double sLo = 0;
        for (int i = 0; i < n; i++)
        {
            if (!WindowFunctionBinder.TryToDouble(values[i], out var x))
            {
                output[i] = null;
                continue;
            }

            double dev = x - mean;
            sHi = Math.Max(0, sHi + dev - k);
            sLo = Math.Min(0, sLo + dev + k);

            bool fired = sHi > h || sLo < -h;
            output[i] = fired;
            if (fired)
            {
                // 重置累积器，准备探测下一个变点
                sHi = 0;
                sLo = 0;
            }
        }

        return output;
    }
}
