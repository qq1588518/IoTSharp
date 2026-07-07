namespace SonnetDB.Benchmarks.Helpers;

/// <summary>
/// 生成用于基准测试的模拟时序数据点。
/// </summary>
public static class DataGenerator
{
    /// <summary>
    /// 生成指定数量的模拟传感器时序数据点。
    /// 时间戳从 2024-01-01 00:00:00 UTC 开始，每秒递增一个数据点。
    /// </summary>
    /// <param name="count">数据点数量。</param>
    /// <returns>数据点数组，按时间递增排列。</returns>
    public static BenchmarkDataPoint[] Generate(int count)
    {
        var rng = new Random(42);
        var baseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var points = new BenchmarkDataPoint[count];
        for (int i = 0; i < count; i++)
        {
            points[i] = new BenchmarkDataPoint(
                baseTime.AddSeconds(i).ToUnixTimeMilliseconds(),
                "server001",
                Math.Round(rng.NextDouble() * 100.0, 4));
        }

        return points;
    }

    /// <summary>
    /// 数据集的起始时间戳（毫秒）。对应 2024-01-01 00:00:00 UTC。
    /// </summary>
    public static readonly long StartTimestampMs =
        new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    /// <summary>
    /// 用于范围查询和聚合查询的起始时间戳（毫秒）。
    /// 取整个数据集的最后 10% 起始位置，约对应 2024-01-11 09:46:40 UTC。
    /// </summary>
    public static long QueryFromMs(int totalCount) =>
        new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
            .AddSeconds(totalCount * 9 / 10)
            .ToUnixTimeMilliseconds();

    /// <summary>
    /// 用于范围查询和聚合查询的结束时间戳（毫秒）。
    /// 取整个数据集的最后时间戳 + 1ms（exclusive）。
    /// </summary>
    public static long QueryToMs(int totalCount) =>
        new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
            .AddSeconds(totalCount)
            .ToUnixTimeMilliseconds();
}

/// <summary>
/// 基准测试专用数据点（只读结构体，避免堆分配）。
/// </summary>
/// <param name="Timestamp">Unix 时间戳（毫秒）。</param>
/// <param name="Host">主机名标签。</param>
/// <param name="Value">传感器数值。</param>
public readonly record struct BenchmarkDataPoint(long Timestamp, string Host, double Value);
