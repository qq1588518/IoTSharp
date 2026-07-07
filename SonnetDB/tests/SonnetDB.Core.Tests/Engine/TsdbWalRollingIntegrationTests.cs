using SonnetDB.Engine;
using SonnetDB.Engine.Compaction;
using SonnetDB.Model;
using SonnetDB.Storage.Segments;
using SonnetDB.Wal;
using Xunit;

namespace SonnetDB.Core.Tests.Engine;

/// <summary>
/// WAL 多段滚动端到端集成测试：测试 <see cref="Tsdb"/> 在 WAL 滚动策略下的整体行为。
/// </summary>
public sealed class TsdbWalRollingIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public TsdbWalRollingIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private TsdbOptions MakeOptions(long maxBytesPerSegment = 64 * 1024) =>
        new TsdbOptions
        {
            RootDirectory = _tempDir,
            WalBufferSize = 4 * 1024,
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
            WalRolling = new WalRollingPolicy
            {
                Enabled = true,
                MaxBytesPerSegment = maxBytesPerSegment,
                MaxRecordsPerSegment = 1_000_000,
            },
            BackgroundFlush = new BackgroundFlushOptions { Enabled = false },
            Compaction = new CompactionPolicy { Enabled = false },
        };

    [Fact]
    public void LargeWrite_ProducesMultipleWalSegments()
    {
        // 设置极小的字节阈值，确保写入 200 个点会触发多次 Roll
        var opts = MakeOptions(maxBytesPerSegment: 512);
        using var db = Tsdb.Open(opts);

        var tags = new Dictionary<string, string> { ["host"] = "srv1" };
        for (int i = 0; i < 200; i++)
        {
            var p = Point.Create("cpu", 1000L + i, tags,
                new Dictionary<string, FieldValue> { ["usage"] = FieldValue.FromDouble(i) });
            db.Write(p);
        }

        string walDir = TsdbPaths.WalDir(_tempDir);
        var walSegs = WalSegmentLayout.Enumerate(walDir);
        Assert.True(walSegs.Count > 1, $"Expected >1 WAL segment, got {walSegs.Count}");
    }

    [Fact]
    public void FlushNow_RecyclesOldWalSegments_OnlyActiveRemains()
    {
        var opts = MakeOptions(maxBytesPerSegment: 512);
        using var db = Tsdb.Open(opts);

        var tags = new Dictionary<string, string> { ["host"] = "srv1" };
        // 写入足够多的点产生多个 WAL segment
        for (int i = 0; i < 200; i++)
        {
            var p = Point.Create("cpu", 1000L + i, tags,
                new Dictionary<string, FieldValue> { ["usage"] = FieldValue.FromDouble(i) });
            db.Write(p);
        }

        string walDir = TsdbPaths.WalDir(_tempDir);
        int segsBefore = WalSegmentLayout.Enumerate(walDir).Count;
        Assert.True(segsBefore > 1);

        // Flush → 应回收所有旧 segment，只剩 active
        var result = db.FlushNow();
        Assert.NotNull(result);

        var segsAfter = WalSegmentLayout.Enumerate(walDir);
        Assert.Single(segsAfter);
    }

    [Fact]
    public void MultipleFlushes_WalSegmentCountStaysSmall()
    {
        var opts = MakeOptions(maxBytesPerSegment: 256);
        using var db = Tsdb.Open(opts);

        var tags = new Dictionary<string, string> { ["host"] = "srv1" };
        string walDir = TsdbPaths.WalDir(_tempDir);

        for (int flush = 0; flush < 5; flush++)
        {
            // 每次写入 50 个点
            for (int i = 0; i < 50; i++)
            {
                var p = Point.Create("cpu", 1000L + flush * 1000 + i, tags,
                    new Dictionary<string, FieldValue> { ["usage"] = FieldValue.FromDouble(i) });
                db.Write(p);
            }
            db.FlushNow();

            var walSegs = WalSegmentLayout.Enumerate(walDir);
            // 每次 Flush 后 WAL 目录只剩 active segment
            Assert.Single(walSegs);
        }
    }

    [Fact]
    public void CrashAfterWrite_RecoveredOnReopen()
    {
        const int pointCount = 100;
        var opts = MakeOptions(maxBytesPerSegment: 256);
        var tags = new Dictionary<string, string> { ["host"] = "srv1" };

        // 会话 1：写入点但崩溃
        var db = Tsdb.Open(opts);
        for (int i = 0; i < pointCount; i++)
        {
            var p = Point.Create("cpu", 1000L + i, tags,
                new Dictionary<string, FieldValue> { ["usage"] = FieldValue.FromDouble(i) });
            db.Write(p);
        }
        db.CrashSimulationCloseWal(); // 崩溃

        // 会话 2：重新打开，replay 应恢复所有点
        using var db2 = Tsdb.Open(opts);
        Assert.Equal(1, db2.Catalog.Count);
        Assert.Equal(pointCount, (int)db2.MemTable.PointCount);
    }

    [Fact]
    public void CrashAfterFlush_ThenWrite_RecoveredCorrectly()
    {
        const int flushCount = 50;
        const int afterFlushCount = 20;
        var opts = MakeOptions(maxBytesPerSegment: 256);
        var tags = new Dictionary<string, string> { ["host"] = "srv1" };

        var db = Tsdb.Open(opts);
        for (int i = 0; i < flushCount; i++)
        {
            var p = Point.Create("cpu", 1000L + i, tags,
                new Dictionary<string, FieldValue> { ["usage"] = FieldValue.FromDouble(i) });
            db.Write(p);
        }
        db.FlushNow();

        for (int i = 0; i < afterFlushCount; i++)
        {
            var p = Point.Create("cpu", 2000L + i, tags,
                new Dictionary<string, FieldValue> { ["usage"] = FieldValue.FromDouble(100 + i) });
            db.Write(p);
        }
        db.CrashSimulationCloseWal(); // 崩溃

        using var db2 = Tsdb.Open(opts);
        // Flush 之后的 20 个点应通过 WAL replay 恢复
        Assert.Equal(afterFlushCount, (int)db2.MemTable.PointCount);
        // Flush 之前的 50 个点已在 Segment 中
        Assert.Single(db2.ListSegments());
    }
}
