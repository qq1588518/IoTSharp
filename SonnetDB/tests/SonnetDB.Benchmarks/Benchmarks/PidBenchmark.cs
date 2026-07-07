using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using SonnetDB.Engine;
using SonnetDB.Engine.Compaction;
using SonnetDB.Engine.Retention;
using SonnetDB.Memory;
using SonnetDB.Sql;
using SonnetDB.Sql.Execution;

namespace SonnetDB.Benchmarks.Benchmarks;

/// <summary>
/// SonnetDB PID 控制函数基准，覆盖逐点控制律、时间桶聚合与阶跃响应自动整定。
/// </summary>
[Config(typeof(PidBenchmarkConfig))]
[MemoryDiagnoser]
[BenchmarkCategory("Pid")]
public class PidBenchmark
{
    private const int DataPointCount = 50_000;
    private const long StartTimestampMs = 1_700_000_000_000;
    private string _rootDirectory = string.Empty;
    private Tsdb? _db;

    /// <summary>
    /// 初始化反应器阶跃响应数据集。
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), $"sonnetdb_bench_pid_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDirectory);
        _db = Tsdb.Open(new TsdbOptions
        {
            RootDirectory = _rootDirectory,
            FlushPolicy = new MemTableFlushPolicy
            {
                MaxBytes = long.MaxValue,
                MaxPoints = int.MaxValue,
                MaxAge = TimeSpan.MaxValue,
            },
            BackgroundFlush = new BackgroundFlushOptions { Enabled = false },
            Compaction = new CompactionPolicy { Enabled = false },
            Retention = new RetentionPolicy { Enabled = false },
        });

        SqlExecutor.Execute(_db,
            "CREATE MEASUREMENT reactor (device TAG, temperature FIELD FLOAT)");
        InsertStepResponse();
        _db.FlushNow();
    }

    /// <summary>
    /// 清理临时数据库目录。
    /// </summary>
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _db?.Dispose();
        _db = null;
        if (Directory.Exists(_rootDirectory))
            Directory.Delete(_rootDirectory, recursive: true);
    }

    /// <summary>
    /// 逐行计算 PID 控制输出，模拟历史回测或控制回写前的离线计算。
    /// </summary>
    /// <returns>返回结果行数，防止 BenchmarkDotNet 消除调用。</returns>
    [Benchmark(Baseline = true, Description = "SonnetDB pid_series(50k)")]
    public int SonnetDB_PidSeries()
    {
        var result = (SelectExecutionResult)SqlExecutor.Execute(Db,
            "SELECT time, pid_series(temperature, 75.0, 0.6, 0.1, 0.05) " +
            "FROM reactor WHERE device = 'r1'")!;
        return result.Rows.Count;
    }

    /// <summary>
    /// 在 1 分钟时间桶内执行 PID 聚合，模拟仪表盘或周期性下发场景。
    /// </summary>
    /// <returns>返回时间桶数量。</returns>
    [Benchmark(Description = "SonnetDB pid(50k, 1m buckets)")]
    public int SonnetDB_PidAggregate1Min()
    {
        var result = (SelectExecutionResult)SqlExecutor.Execute(Db,
            "SELECT pid(temperature, 75.0, 0.6, 0.1, 0.05) " +
            "FROM reactor WHERE device = 'r1' GROUP BY time(1m)")!;
        return result.Rows.Count;
    }

    /// <summary>
    /// 使用 Ziegler-Nichols 方法进行阶跃响应 PID 参数整定。
    /// </summary>
    /// <returns>返回 JSON 结果长度。</returns>
    [Benchmark(Description = "SonnetDB pid_estimate ZN(50k)")]
    public int SonnetDB_PidEstimateZn()
    {
        var result = (SelectExecutionResult)SqlExecutor.Execute(Db,
            "SELECT pid_estimate(temperature, 'zn', 1.0, 0.1, 0.1, NULL) " +
            "FROM reactor WHERE device = 'r1'")!;
        return Convert.ToString(result.Rows[0][0], CultureInfo.InvariantCulture)!.Length;
    }

    /// <summary>
    /// 使用 IMC 方法进行阶跃响应 PID 参数整定。
    /// </summary>
    /// <returns>返回 JSON 结果长度。</returns>
    [Benchmark(Description = "SonnetDB pid_estimate IMC(50k)")]
    public int SonnetDB_PidEstimateImc()
    {
        var result = (SelectExecutionResult)SqlExecutor.Execute(Db,
            "SELECT pid_estimate(temperature, 'imc', 1.0, 0.1, 0.1, 50) " +
            "FROM reactor WHERE device = 'r1'")!;
        return Convert.ToString(result.Rows[0][0], CultureInfo.InvariantCulture)!.Length;
    }

    private Tsdb Db => _db ?? throw new InvalidOperationException("Benchmark database is not initialized.");

    private void InsertStepResponse()
    {
        const int batchSize = 5_000;
        var sql = new StringBuilder(64 * batchSize);
        for (int offset = 0; offset < DataPointCount; offset += batchSize)
        {
            int end = Math.Min(offset + batchSize, DataPointCount);
            sql.Clear();
            sql.Append("INSERT INTO reactor (time, device, temperature) VALUES ");
            for (int i = offset; i < end; i++)
            {
                if (i > offset) sql.Append(", ");
                long timestamp = StartTimestampMs + i * 1_000L;
                double temperature = GenerateTemperature(i);
                sql.Append(CultureInfo.InvariantCulture,
                    $"({timestamp}, 'r1', {temperature:G17})");
            }

            SqlExecutor.Execute(Db, sql.ToString());
        }
    }

    private static double GenerateTemperature(int index)
    {
        const int stepStart = 100;
        const double baseline = 65.0;
        const double amplitude = 15.0;
        const double tauSeconds = 600.0;

        if (index < stepStart)
            return baseline;

        double elapsed = index - stepStart;
        double response = baseline + amplitude * (1.0 - Math.Exp(-elapsed / tauSeconds));
        double ripple = Math.Sin(index * 0.017) * 0.04;
        return response + ripple;
    }

    private sealed class PidBenchmarkConfig : ManualConfig
    {
        public PidBenchmarkConfig()
        {
            AddJob(Job.ShortRun.WithId("PidShortRun"));
        }
    }
}
