using SonnetDB.Catalog;
using SonnetDB.Model;
using SonnetDB.Sql.Ast;

namespace SonnetDB.Query.Functions.Window;

// ────────────────────────────────────────────────────────────────────────────
// 平滑 / 预测：moving_average / ewma / holt_winters
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>moving_average(field, n)</c>：N 点滑动均值。前 <c>n-1</c> 行输出 null。
/// 缺失值不计入窗口分母。
/// </summary>
internal sealed class MovingAverageFunction : IWindowFunction
{
    public string Name => "moving_average";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 2, 2);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        int n = WindowFunctionBinder.ResolvePositiveIntArgument(call, 1, Name);
        return new MovingAverageEvaluator(col.Name, n);
    }
}

internal sealed class MovingAverageEvaluator : DoubleWindowEvaluatorBase
{
    private const int MaxStackWindowSize = 256;
    private readonly int _windowSize;

    public MovingAverageEvaluator(string fieldName, int windowSize)
    {
        FieldName = fieldName;
        _windowSize = windowSize;
    }

    public override string FieldName { get; }

    public override IWindowState CreateState()
        => new MovingAverageState(_windowSize);

    protected override void ComputeDoubleCore(
        ReadOnlySpan<long> timestamps,
        ReadOnlySpan<FieldValue?> values,
        Span<double> output,
        Span<bool> hasValue)
    {
        hasValue.Clear();
        if (_windowSize <= MaxStackWindowSize)
        {
            Span<double> window = stackalloc double[_windowSize];
            Span<bool> present = stackalloc bool[_windowSize];
            ComputeMovingAverage(values, _windowSize, window, present, output, hasValue);
            return;
        }

        ComputeMovingAverage(
            values,
            _windowSize,
            new double[_windowSize],
            new bool[_windowSize],
            output,
            hasValue);
    }

    protected override void ComputeObjectCore(
        ReadOnlySpan<long> timestamps,
        ReadOnlySpan<FieldValue?> values,
        Span<object?> output)
    {
        output.Clear();
        if (_windowSize <= MaxStackWindowSize)
        {
            Span<double> window = stackalloc double[_windowSize];
            Span<bool> present = stackalloc bool[_windowSize];
            ComputeMovingAverage(values, _windowSize, window, present, output);
            return;
        }

        ComputeMovingAverage(
            values,
            _windowSize,
            new double[_windowSize],
            new bool[_windowSize],
            output);
    }

    private static void ComputeMovingAverage(
        ReadOnlySpan<FieldValue?> values,
        int windowSize,
        Span<double> window,
        Span<bool> present,
        Span<double> output,
        Span<bool> hasValue)
    {
        double sum = 0;
        int count = 0;
        for (int i = 0; i < values.Length; i++)
        {
            int slot = i % windowSize;
            if (i >= windowSize && present[slot])
            {
                sum -= window[slot];
                count--;
            }

            if (WindowFunctionBinder.TryToDouble(values[i], out var v))
            {
                window[slot] = v;
                present[slot] = true;
                sum += v;
                count++;
            }
            else
            {
                window[slot] = 0;
                present[slot] = false;
            }

            if (i + 1 >= windowSize && count > 0)
            {
                output[i] = sum / count;
                hasValue[i] = true;
            }
        }
    }

    private static void ComputeMovingAverage(
        ReadOnlySpan<FieldValue?> values,
        int windowSize,
        Span<double> window,
        Span<bool> present,
        Span<object?> output)
    {
        double sum = 0;
        int count = 0;
        for (int i = 0; i < values.Length; i++)
        {
            int slot = i % windowSize;
            if (i >= windowSize && present[slot])
            {
                sum -= window[slot];
                count--;
            }

            if (WindowFunctionBinder.TryToDouble(values[i], out var v))
            {
                window[slot] = v;
                present[slot] = true;
                sum += v;
                count++;
            }
            else
            {
                window[slot] = 0;
                present[slot] = false;
            }

            if (i + 1 >= windowSize && count > 0)
                output[i] = sum / count;
        }
    }
}

internal sealed class MovingAverageState : IWindowState
{
    private readonly int _windowSize;
    private readonly double[] _window;
    private readonly bool[] _present;
    private double _sum;
    private int _count;
    private int _filled;

    public MovingAverageState(int windowSize)
    {
        _windowSize = windowSize;
        _window = new double[windowSize];
        _present = new bool[windowSize];
    }

    public WindowStateOutput Update(long timestamp, FieldValue? value)
    {
        int slot = _filled % _windowSize;
        // 从窗口中弹出旧值
        if (_filled >= _windowSize && _present[slot])
        {
            _sum -= _window[slot];
            _count--;
        }

        if (WindowFunctionBinder.TryToDouble(value, out var v))
        {
            _window[slot] = v;
            _present[slot] = true;
            _sum += v;
            _count++;
        }
        else
        {
            _window[slot] = 0;
            _present[slot] = false;
        }
        _filled++;

        // 至少累积 _windowSize 行后才输出（不足窗口大小返回 null）
        return _filled >= _windowSize && _count > 0
            ? WindowStateOutput.FromDouble(_sum / _count)
            : WindowStateOutput.Null();
    }
}

/// <summary>
/// <c>ewma(field, alpha)</c>：指数加权移动平均，<c>0 &lt; alpha &lt;= 1</c>。
/// 公式：<c>s_t = alpha * x_t + (1 - alpha) * s_{t-1}</c>，首个有效值为 <c>x_t</c> 自身。
/// 缺失值跳过（不更新状态）。
/// </summary>
internal sealed class EwmaFunction : IWindowFunction
{
    public string Name => "ewma";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 2, 2);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        double alpha = WindowFunctionBinder.ResolveNumericArgument(call, 1, Name);
        if (alpha <= 0 || alpha > 1)
            throw new InvalidOperationException(
                $"窗口函数 ewma 的 alpha 必须在 (0, 1] 区间内，实际为 {alpha}。");
        return new EwmaEvaluator(col.Name, alpha);
    }
}

internal sealed class EwmaEvaluator : DoubleWindowEvaluatorBase
{
    private readonly double _alpha;

    public EwmaEvaluator(string fieldName, double alpha)
    {
        FieldName = fieldName;
        _alpha = alpha;
    }

    public override string FieldName { get; }

    public override IWindowState CreateState()
        => new EwmaState(_alpha);
}

internal sealed class EwmaState : IWindowState
{
    private readonly double _alpha;
    private double _s;
    private bool _initialized;

    public EwmaState(double alpha)
    {
        _alpha = alpha;
    }

    public WindowStateOutput Update(long timestamp, FieldValue? value)
    {
        if (!WindowFunctionBinder.TryToDouble(value, out var v))
            return _initialized ? WindowStateOutput.FromDouble(_s) : WindowStateOutput.Null();

        if (!_initialized)
        {
            _s = v;
            _initialized = true;
        }
        else
        {
            _s = _alpha * v + (1 - _alpha) * _s;
        }

        return WindowStateOutput.FromDouble(_s);
    }
}

/// <summary>
/// <c>holt_winters(field, alpha, beta)</c>：双指数（Holt 加法）平滑，输出每行的拟合值
/// <c>level + trend</c>。<c>0 &lt; alpha, beta &lt;= 1</c>；不含季节性分量
/// （季节性 forecast 由 PR #55 的 TVF 形式提供）。
/// </summary>
internal sealed class HoltWintersFunction : IWindowFunction
{
    public string Name => "holt_winters";

    public IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema)
    {
        WindowFunctionBinder.RequireArgumentCount(call, Name, 3, 3);
        var col = WindowFunctionBinder.ResolveFieldArgument(call, schema, Name, 0);
        double alpha = WindowFunctionBinder.ResolveNumericArgument(call, 1, Name);
        double beta = WindowFunctionBinder.ResolveNumericArgument(call, 2, Name);
        if (alpha <= 0 || alpha > 1)
            throw new InvalidOperationException(
                $"窗口函数 holt_winters 的 alpha 必须在 (0, 1] 区间内，实际为 {alpha}。");
        if (beta <= 0 || beta > 1)
            throw new InvalidOperationException(
                $"窗口函数 holt_winters 的 beta 必须在 (0, 1] 区间内，实际为 {beta}。");
        return new HoltWintersEvaluator(col.Name, alpha, beta);
    }
}

internal sealed class HoltWintersEvaluator : DoubleWindowEvaluatorBase
{
    private readonly double _alpha;
    private readonly double _beta;

    public HoltWintersEvaluator(string fieldName, double alpha, double beta)
    {
        FieldName = fieldName;
        _alpha = alpha;
        _beta = beta;
    }

    public override string FieldName { get; }

    public override IWindowState CreateState()
        => new HoltWintersState(_alpha, _beta);
}

internal sealed class HoltWintersState : IWindowState
{
    private readonly double _alpha;
    private readonly double _beta;
    private double _level;
    private double _trend;
    private bool _haveLevel;
    private bool _haveTrend;

    public HoltWintersState(double alpha, double beta)
    {
        _alpha = alpha;
        _beta = beta;
    }

    public WindowStateOutput Update(long timestamp, FieldValue? value)
    {
        if (!WindowFunctionBinder.TryToDouble(value, out var x))
        {
            return _haveLevel
                ? WindowStateOutput.FromDouble(_level + (_haveTrend ? _trend : 0.0))
                : WindowStateOutput.Null();
        }

        if (!_haveLevel)
        {
            _level = x;
            _haveLevel = true;
            return WindowStateOutput.FromDouble(_level);
        }

        double prevLevel = _level;
        if (!_haveTrend)
        {
            // 初始 trend 用首两点差
            _trend = x - _level;
            _haveTrend = true;
            _level = x;
        }
        else
        {
            _level = _alpha * x + (1 - _alpha) * (_level + _trend);
            _trend = _beta * (_level - prevLevel) + (1 - _beta) * _trend;
        }

        return WindowStateOutput.FromDouble(_level + _trend);
    }
}
