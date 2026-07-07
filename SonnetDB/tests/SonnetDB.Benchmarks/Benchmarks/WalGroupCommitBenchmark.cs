using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using SonnetDB.Catalog;
using SonnetDB.Engine;
using SonnetDB.Engine.Compaction;
using SonnetDB.Engine.Retention;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Format;
using SonnetDB.Storage.Segments;

namespace SonnetDB.Benchmarks.Benchmarks;

/// <summary>
/// WAL group-commit 基准：对比并发写请求在启用/禁用 group-commit 时的同步 WAL 写入开销。
/// </summary>
[Config(typeof(WalGroupCommitConfig))]
[MemoryDiagnoser]
[BenchmarkCategory("WalGroupCommit")]
public class WalGroupCommitBenchmark
{
    private string _rootDir = string.Empty;
    private Tsdb? _db;
    private Point[] _points = [];

    /// <summary>是否启用 WAL group-commit。</summary>
    [Params(false, true)]
    public bool GroupCommitEnabled { get; set; }

    /// <summary>并发写请求数量。</summary>
    [Params(32)]
    public int WriterCount { get; set; }

    /// <summary>每次迭代前创建空数据库。</summary>
    [IterationSetup]
    public void IterationSetup()
    {
        _points = Enumerable.Range(0, WriterCount)
            .Select(i => CreateMetricPoint(i))
            .ToArray();

        _rootDir = Path.Combine(Path.GetTempPath(), $"sonnetdb_wal_group_commit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDir);
        _db = Tsdb.Open(new TsdbOptions
        {
            RootDirectory = _rootDir,
            SyncWalOnEveryWrite = true,
            WalGroupCommit = new WalGroupCommitOptions
            {
                Enabled = GroupCommitEnabled,
                FlushWindow = TimeSpan.FromMilliseconds(2),
            },
            FlushPolicy = new MemTableFlushPolicy
            {
                MaxBytes = long.MaxValue,
                MaxPoints = int.MaxValue,
                MaxAge = TimeSpan.MaxValue,
            },
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
            BackgroundFlush = new BackgroundFlushOptions { Enabled = false },
            Compaction = new CompactionPolicy { Enabled = false },
            Retention = new RetentionPolicy { Enabled = false },
        });
        _db.CreateMeasurement(CreateMetricSchema());
    }

    /// <summary>每次迭代后关闭并删除数据库目录。</summary>
    [IterationCleanup]
    public void IterationCleanup()
    {
        _db?.Dispose();
        _db = null;
        if (Directory.Exists(_rootDir))
            Directory.Delete(_rootDir, recursive: true);
    }

    /// <summary>并发执行多个同步 WAL 写请求。</summary>
    [Benchmark(Description = "WAL 同步写入并发请求")]
    public async Task ConcurrentWriteRequests()
    {
        using var ready = new CountdownEvent(WriterCount);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = new Task[WriterCount];
        for (int i = 0; i < tasks.Length; i++)
        {
            int index = i;
            tasks[i] = StartWriteAsync(index, ready, start);
        }

        ready.Wait();
        start.SetResult();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task StartWriteAsync(
        int index,
        CountdownEvent ready,
        TaskCompletionSource start)
    {
        ready.Signal();
        await start.Task.ConfigureAwait(false);
        _db!.Write(_points[index]);
    }

    private static MeasurementSchema CreateMetricSchema()
        => MeasurementSchema.Create(
            "metric",
            new[]
            {
                new MeasurementColumn("host", MeasurementColumnRole.Tag, FieldType.String),
                new MeasurementColumn("value", MeasurementColumnRole.Field, FieldType.Float64),
            });

    private static Point CreateMetricPoint(int index)
        => Point.Create(
            "metric",
            1_700_000_000_000L + index,
            new Dictionary<string, string> { ["host"] = "h1" },
            new Dictionary<string, FieldValue> { ["value"] = FieldValue.FromDouble(index) });

    private sealed class WalGroupCommitConfig : ManualConfig
    {
        public WalGroupCommitConfig()
        {
            AddJob(Job.Default
                .WithStrategy(RunStrategy.Monitoring)
                .WithWarmupCount(0)
                .WithIterationCount(3));
        }
    }
}
