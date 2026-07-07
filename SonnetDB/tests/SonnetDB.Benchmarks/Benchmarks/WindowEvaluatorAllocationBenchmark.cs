using BenchmarkDotNet.Attributes;
using SonnetDB.Model;
using SonnetDB.Query.Functions;
using SonnetDB.Query.Functions.Window;

namespace SonnetDB.Benchmarks.Benchmarks;

/// <summary>
/// 窗口函数 typed evaluator 分配基准。
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("WindowEvaluator")]
public class WindowEvaluatorAllocationBenchmark
{
    private const int PointCount = 50_000;

    private readonly MovingAverageEvaluator _movingAverage = new("value", 60);
    private readonly EwmaEvaluator _ewma = new("value", 0.2);
    private readonly HoltWintersEvaluator _holtWinters = new("value", 0.4, 0.1);
    private readonly CumulativeSumEvaluator _runningSum = new("value");

    private long[] _timestamps = [];
    private FieldValue?[] _values = [];

    /// <summary>准备固定输入数据。</summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        _timestamps = new long[PointCount];
        _values = new FieldValue?[PointCount];
        for (int i = 0; i < PointCount; i++)
        {
            _timestamps[i] = i * 1000L;
            _values[i] = FieldValue.FromDouble(100d + Math.Sin(i * 0.01d) * 10d + i * 0.001d);
        }
    }

    /// <summary>typed moving_average 输出。</summary>
    [Benchmark(Description = "moving_average typed double")]
    public WindowDoubleOutput MovingAverage_Typed()
        => _movingAverage.ComputeDouble(_timestamps, _values);

    /// <summary>兼容 object?[] moving_average 输出。</summary>
    [Benchmark(Description = "moving_average boxed object[]")]
    public object?[] MovingAverage_BoxedCompatibility()
        => _movingAverage.Compute(_timestamps, _values);

    /// <summary>typed ewma 输出。</summary>
    [Benchmark(Description = "ewma typed double")]
    public WindowDoubleOutput Ewma_Typed()
        => _ewma.ComputeDouble(_timestamps, _values);

    /// <summary>兼容 object?[] ewma 输出。</summary>
    [Benchmark(Description = "ewma boxed object[]")]
    public object?[] Ewma_BoxedCompatibility()
        => _ewma.Compute(_timestamps, _values);

    /// <summary>typed holt_winters 输出。</summary>
    [Benchmark(Description = "holt_winters typed double")]
    public WindowDoubleOutput HoltWinters_Typed()
        => _holtWinters.ComputeDouble(_timestamps, _values);

    /// <summary>兼容 object?[] holt_winters 输出。</summary>
    [Benchmark(Description = "holt_winters boxed object[]")]
    public object?[] HoltWinters_BoxedCompatibility()
        => _holtWinters.Compute(_timestamps, _values);

    /// <summary>typed running_sum 输出。</summary>
    [Benchmark(Description = "running_sum typed double")]
    public WindowDoubleOutput RunningSum_Typed()
        => _runningSum.ComputeDouble(_timestamps, _values);

    /// <summary>兼容 object?[] running_sum 输出。</summary>
    [Benchmark(Description = "running_sum boxed object[]")]
    public object?[] RunningSum_BoxedCompatibility()
        => _runningSum.Compute(_timestamps, _values);
}
