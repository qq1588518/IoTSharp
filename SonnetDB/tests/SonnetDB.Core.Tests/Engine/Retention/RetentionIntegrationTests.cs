using SonnetDB.Engine;
using SonnetDB.Engine.Compaction;
using SonnetDB.Engine.Retention;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Query;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Engine.Retention;

/// <summary>
/// Retention 端到端集成测试：虚拟时钟驱动，验证全过期段 drop、部分过期墓碑注入、数据完整性及重启恢复。
/// </summary>
public sealed class RetentionIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public RetentionIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private TsdbOptions MakeOptions(RetentionPolicy retention, int maxPoints = 4) =>
        new TsdbOptions
        {
            RootDirectory = _tempDir,
            WalBufferSize = 64 * 1024,
            FlushPolicy = new MemTableFlushPolicy
            {
                MaxPoints = maxPoints,
                MaxBytes = 1024L * 1024 * 1024,
                MaxAge = TimeSpan.FromHours(24),
            },
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
            SyncWalOnEveryWrite = false,
            BackgroundFlush = new BackgroundFlushOptions { Enabled = false },
            Compaction = new CompactionPolicy { Enabled = false },
            Retention = retention,
        };

    private static Point MakePoint(long ts, double value, string field = "usage") =>
        Point.Create("cpu", ts,
            new Dictionary<string, string> { ["host"] = "srv1" },
            new Dictionary<string, FieldValue> { [field] = FieldValue.FromDouble(value) });

    private static IReadOnlyList<DataPoint> QueryAll(Tsdb db, ulong seriesId, string field) =>
        db.Query.Execute(new PointQuery(seriesId, field, new TimeRange(long.MinValue, long.MaxValue))).ToList();

    // ── 端到端：全过期段被 drop，磁盘文件不存在 ──────────────────────────────

    [Fact]
    public void Integration_ExpiredSegmentDropped_FileDeleted()
    {
        long nowValue = 1000L;
        var policy = new RetentionPolicy
        {
            Enabled = true,
            TtlInTimestampUnits = 1000,
            NowFn = () => Volatile.Read(ref nowValue),
            PollInterval = TimeSpan.FromHours(24),
        };

        using var db = Tsdb.Open(MakeOptions(policy, maxPoints: 4));

        // 获取 seriesId
        db.Write(MakePoint(100L, 1.0));
        db.Write(MakePoint(200L, 2.0));
        db.Write(MakePoint(300L, 3.0));
        db.Write(MakePoint(400L, 4.0));
        db.FlushNow();

        // 记录段文件路径
        var segments = db.ListSegments();
        Assert.NotEmpty(segments);
        var segPaths = segments.Select(s => s.Path).ToList();

        // 推进时钟：所有段过期
        Volatile.Write(ref nowValue, 10000L);
        db.Retention!.RunOnce();

        // 段已从 SegmentManager 移除
        Assert.Equal(0, db.Segments.SegmentCount);

        // 磁盘文件已删除
        foreach (var path in segPaths)
            Assert.False(File.Exists(path), $"过期段文件应已删除：{path}");
    }

    // ── 端到端：部分过期段注入墓碑，查询时过期点被过滤 ─────────────────────

    [Fact]
    public void Integration_PartiallyExpiredSegment_OldPointsFiltered()
    {
        long nowValue = 1000L;
        var policy = new RetentionPolicy
        {
            Enabled = true,
            TtlInTimestampUnits = 1000,
            NowFn = () => Volatile.Read(ref nowValue),
            PollInterval = TimeSpan.FromHours(24),
        };

        using var db = Tsdb.Open(MakeOptions(policy, maxPoints: 4));

        db.Write(MakePoint(8000L, 1.0));
        db.Write(MakePoint(8500L, 2.0));
        db.Write(MakePoint(9000L, 3.0));
        db.Write(MakePoint(9500L, 4.0));
        db.FlushNow();

        var entry = db.Catalog.Snapshot().First(e => e.Measurement == "cpu");
        ulong seriesId = entry.Id;

        // 推进时钟：cutoff = 9000，点 8000/8500 过期，9000/9500 不过期
        Volatile.Write(ref nowValue, 10000L);
        var stats = db.Retention!.RunOnce();

        Assert.Equal(1, stats.InjectedTombstones);

        // 查询：过期点应被过滤
        var points = QueryAll(db, seriesId, "usage");
        Assert.True(points.All(p => p.Timestamp >= 9000L), "过期点应被墓碑过滤");
        Assert.True(points.Count >= 2, "9000 和 9500 的点应保留");
    }

    // ── 端到端：新鲜数据不受影响 ─────────────────────────────────────────────

    [Fact]
    public void Integration_FreshData_Unaffected()
    {
        long nowValue = 1000L;
        var policy = new RetentionPolicy
        {
            Enabled = true,
            TtlInTimestampUnits = 1000,
            NowFn = () => Volatile.Read(ref nowValue),
            PollInterval = TimeSpan.FromHours(24),
        };

        using var db = Tsdb.Open(MakeOptions(policy, maxPoints: 4));

        // 写入新鲜数据（时间戳 >= 9000 时不会过期）
        db.Write(MakePoint(9500L, 1.0));
        db.Write(MakePoint(9600L, 2.0));
        db.Write(MakePoint(9700L, 3.0));
        db.Write(MakePoint(9800L, 4.0));
        db.FlushNow();

        int segBefore = db.Segments.SegmentCount;

        // 推进时钟：cutoff = 9000，以上所有点 >= 9000 → 不过期
        Volatile.Write(ref nowValue, 10000L);
        var stats = db.Retention!.RunOnce();

        Assert.Equal(0, stats.DroppedSegments);
        Assert.Equal(0, stats.InjectedTombstones);
        Assert.Equal(segBefore, db.Segments.SegmentCount);
    }

    // ── 端到端：Dispose + 重启 → 墓碑持久化，状态一致 ──────────────────────

    [Fact]
    public void Integration_Restart_TombstonesRestored()
    {
        long nowValue = 1000L;
        var policy = new RetentionPolicy
        {
            Enabled = true,
            TtlInTimestampUnits = 1000,
            NowFn = () => Volatile.Read(ref nowValue),
            PollInterval = TimeSpan.FromHours(24),
        };

        ulong seriesId;

        {
            using var db = Tsdb.Open(MakeOptions(policy, maxPoints: 4));

            db.Write(MakePoint(8000L, 1.0));
            db.Write(MakePoint(8500L, 2.0));
            db.Write(MakePoint(9000L, 3.0));
            db.Write(MakePoint(9500L, 4.0));
            db.FlushNow();

            seriesId = db.Catalog.Snapshot().First(e => e.Measurement == "cpu").Id;

            // 推进时钟：注入墓碑
            Volatile.Write(ref nowValue, 10000L);
            db.Retention!.RunOnce();

            Assert.True(db.Tombstones.Count > 0);
        }

        // 重启：墓碑应从 manifest 或 WAL 恢复
        var policy2 = new RetentionPolicy
        {
            Enabled = true,
            TtlInTimestampUnits = 1000,
            NowFn = () => 10000L,
            PollInterval = TimeSpan.FromHours(24),
        };

        using var db2 = Tsdb.Open(MakeOptions(policy2, maxPoints: 4));
        Assert.True(db2.Tombstones.Count > 0, "重启后墓碑应从持久化存储恢复");

        // 过期点仍被过滤
        var points = QueryAll(db2, seriesId, "usage");
        Assert.True(points.All(p => p.Timestamp >= 9000L), "重启后过期点仍应被过滤");
    }

    // ── 端到端：[cutoff, +∞) 的新数据完整 ────────────────────────────────────

    [Fact]
    public void Integration_FreshDataAfterCutoff_CompletelyIntact()
    {
        long nowValue = 1000L;
        var policy = new RetentionPolicy
        {
            Enabled = true,
            TtlInTimestampUnits = 1000,
            NowFn = () => Volatile.Read(ref nowValue),
            PollInterval = TimeSpan.FromHours(24),
        };

        using var db = Tsdb.Open(MakeOptions(policy, maxPoints: 8));

        // 写入混合段（部分过期 + 部分新鲜）
        db.Write(MakePoint(7000L, 1.0));
        db.Write(MakePoint(8000L, 2.0));
        db.Write(MakePoint(9000L, 3.0)); // cutoff
        db.Write(MakePoint(9100L, 4.0));
        db.Write(MakePoint(9200L, 5.0));
        db.Write(MakePoint(9300L, 6.0));
        db.Write(MakePoint(9400L, 7.0));
        db.Write(MakePoint(9500L, 8.0));
        db.FlushNow();

        var entry = db.Catalog.Snapshot().First(e => e.Measurement == "cpu");
        ulong seriesId = entry.Id;

        // 推进时钟
        Volatile.Write(ref nowValue, 10000L);
        db.Retention!.RunOnce();

        // 查询 [9000, +∞) 应完整
        var fresh = db.Query.Execute(new PointQuery(seriesId, "usage", new TimeRange(9000L, long.MaxValue))).ToList();
        // 应含 9000, 9100, 9200, 9300, 9400, 9500 共 6 个点（cutoff=9000 点不过期）
        Assert.Equal(6, fresh.Count);
        Assert.All(fresh, p => Assert.True(p.Timestamp >= 9000L));
        var expectedTimestamps = new long[] { 9000L, 9100L, 9200L, 9300L, 9400L, 9500L };
        Assert.Equal(expectedTimestamps, fresh.Select(p => p.Timestamp).OrderBy(t => t).ToArray());
    }
}
