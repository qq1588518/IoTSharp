using SonnetDB.Engine;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Engine;

/// <summary>
/// <see cref="Tsdb"/> 并发安全测试。
/// </summary>
public sealed class TsdbConcurrencyTests : IDisposable
{
    private readonly string _tempDir;

    public TsdbConcurrencyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private TsdbOptions MakeOptions() =>
        new TsdbOptions
        {
            RootDirectory = _tempDir,
            WalBufferSize = 64 * 1024,
            FlushPolicy = new MemTableFlushPolicy
            {
                MaxPoints = 10_000_000,
                MaxBytes = 1024L * 1024 * 1024,
                MaxAge = TimeSpan.FromHours(24),
            },
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
        };

    /// <summary>
    /// 单写者串行 Write 1000 次（Parallel.For 模拟高并发），验证内部加锁无异常。
    /// </summary>
    [Fact]
    public void SingleWriter_ParallelWrites_NoException()
    {
        const int writeCount = 1000;
        using var db = Tsdb.Open(MakeOptions());

        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, writeCount, i =>
        {
            try
            {
                var p = Point.Create("metric", 1000L + i,
                    new Dictionary<string, string> { ["host"] = "h" },
                    new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromDouble(i) });
                db.Write(p);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
        Assert.Equal(writeCount, (int)db.MemTable.PointCount);
    }

    /// <summary>
    /// 写入期间多线程调用 ListSegments 和 Catalog.TryGet 不应抛异常。
    /// </summary>
    [Fact]
    public async Task WriteAndRead_Concurrent_NoException()
    {
        const int writeCount = 200;
        const int readThreads = 4;
        using var db = Tsdb.Open(MakeOptions());

        using var cts = new CancellationTokenSource();
        var readExceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        // 启动读取线程（通过检查 cts.Token.IsCancellationRequested 退出循环，不依赖 Task.Run 的 token 传参）
        var readTasks = Enumerable.Range(0, readThreads).Select(_ =>
            Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var segs = db.ListSegments();
                        var snap = db.Catalog.Snapshot();
                        _ = segs.Count;
                        _ = snap.Count;
                    }
                    catch (ObjectDisposedException)
                    {
                        // 预期在 db 关闭后可能发生
                        break;
                    }
                    catch (Exception ex)
                    {
                        readExceptions.Add(ex);
                    }
                }
            })).ToArray();

        // 写入线程
        for (int i = 0; i < writeCount; i++)
        {
            var p = Point.Create("m", 1000L + i,
                new Dictionary<string, string> { ["k"] = "v" },
                new Dictionary<string, FieldValue> { ["f"] = FieldValue.FromDouble(i) });
            db.Write(p);
        }

        await cts.CancelAsync();
        await Task.WhenAll(readTasks);

        Assert.Empty(readExceptions);
    }

    /// <summary>
    /// FlushNow 和 Write 并发调用（串行实现下不应死锁）。
    /// </summary>
    [Fact]
    public async Task FlushNow_And_Write_Concurrent_NoDeadlock()
    {
        const int cycles = 5;
        using var db = Tsdb.Open(MakeOptions());

        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var tasks = Enumerable.Range(0, cycles).Select(cycle =>
            Task.Run(() =>
            {
                try
                {
                    // Write some points
                    for (int i = 0; i < 10; i++)
                    {
                        var p = Point.Create("m", 1000L + cycle * 100 + i,
                            new Dictionary<string, string> { ["c"] = cycle.ToString() },
                            new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromDouble(i) });
                        db.Write(p);
                    }

                    // Flush
                    db.FlushNow();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);
    }

    [Fact]
    public void ConcurrentWriteMany_WithSchemaExpansion_NoExceptionAndSchemaRecoverable()
    {
        const int batchCount = 8;
        const int pointsPerBatch = 4;
        var db = Tsdb.Open(MakeOptions());
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, batchCount, batch =>
        {
            try
            {
                var points = new Point[pointsPerBatch];
                for (int i = 0; i < points.Length; i++)
                {
                    string fieldName = $"f_{batch}_{i}";
                    points[i] = Point.Create("concurrent_metric", 1000L + batch * 100 + i,
                        new Dictionary<string, string>
                        {
                            ["host"] = $"h{batch}",
                            [$"tag_{batch}"] = i.ToString(),
                        },
                        new Dictionary<string, FieldValue>
                        {
                            [fieldName] = FieldValue.FromDouble(i),
                        });
                }

                db.WriteMany(points);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);

        var schema = db.Measurements.TryGet("concurrent_metric");
        Assert.NotNull(schema);
        for (int batch = 0; batch < batchCount; batch++)
        {
            Assert.NotNull(schema!.TryGetColumn($"tag_{batch}"));
            for (int i = 0; i < pointsPerBatch; i++)
                Assert.NotNull(schema.TryGetColumn($"f_{batch}_{i}"));
        }

        db.CrashSimulationCloseWal();

        using var reopened = Tsdb.Open(MakeOptions());
        var recovered = reopened.Measurements.TryGet("concurrent_metric");
        Assert.NotNull(recovered);
        for (int batch = 0; batch < batchCount; batch++)
        {
            Assert.NotNull(recovered!.TryGetColumn($"tag_{batch}"));
            for (int i = 0; i < pointsPerBatch; i++)
                Assert.NotNull(recovered.TryGetColumn($"f_{batch}_{i}"));
        }
    }
}
