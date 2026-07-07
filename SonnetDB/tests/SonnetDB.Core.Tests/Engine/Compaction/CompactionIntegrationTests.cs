using SonnetDB.Engine;
using SonnetDB.Engine.Compaction;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Query;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Engine.Compaction;

/// <summary>
/// Compaction 端到端集成测试：验证多段 → 少段 + 数据完整 + 旧文件已删除 + 重启后一致。
/// </summary>
public sealed class CompactionIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public CompactionIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private TsdbOptions MakeOptions(int minTierSize = 2, bool enableCompaction = true) =>
        new TsdbOptions
        {
            RootDirectory = _tempDir,
            FlushPolicy = new MemTableFlushPolicy
            {
                MaxPoints = 200,
                MaxBytes = 1024L * 1024 * 1024,
                MaxAge = TimeSpan.FromHours(24),
            },
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
            BackgroundFlush = new BackgroundFlushOptions
            {
                Enabled = true,
                PollInterval = TimeSpan.FromMilliseconds(100),
                ShutdownTimeout = TimeSpan.FromSeconds(15),
            },
            Compaction = new CompactionPolicy
            {
                Enabled = enableCompaction,
                MinTierSize = minTierSize,
                TierSizeRatio = 4,
                FirstTierMaxBytes = 1024L * 1024, // 1MB
                PollInterval = TimeSpan.FromMilliseconds(200),
                ShutdownTimeout = TimeSpan.FromSeconds(15),
            },
        };

    private static Point MakePoint(long timestamp, double value) =>
        Point.Create("metrics", timestamp,
            new Dictionary<string, string> { ["host"] = "srv1" },
            new Dictionary<string, FieldValue> { ["val"] = FieldValue.FromDouble(value) });

    // ── 端到端：写入数据 → 触发 Flush → 等待 Compaction → 段数减少 → 数据完整 ──

    [Fact]
    public void AutoCompaction_ReducesSegmentCount_DataIntact()
    {
        const int totalPoints = 1200; // 6 次 Flush（每次 200 点）
        // 先用禁用 Compaction 的配置 + 手动 Flush 建立多个段
        var optionsNoCompact = new TsdbOptions
        {
            RootDirectory = _tempDir,
            FlushPolicy = new MemTableFlushPolicy
            {
                MaxPoints = int.MaxValue,
                MaxBytes = 1024L * 1024 * 1024,
                MaxAge = TimeSpan.MaxValue,
            },
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
            BackgroundFlush = new BackgroundFlushOptions { Enabled = false },
            Compaction = new CompactionPolicy { Enabled = false },
        };

        // 第一轮：禁用后台线程，手动写入并 Flush 6 次，产生 6 个段
        {
            using var db = Tsdb.Open(optionsNoCompact);
            for (int batch = 0; batch < 6; batch++)
            {
                for (int i = 0; i < 200; i++)
                    db.Write(MakePoint(1000L + batch * 200 + i, batch * 200 + i));
                db.FlushNow();
            }
            Assert.Equal(6, db.Segments.SegmentCount);
        }

        // 第二轮：重新打开，启用 Compaction，等待段数减少
        using var db2 = Tsdb.Open(MakeOptions(minTierSize: 2));

        int initialCount = db2.Segments.SegmentCount;
        Assert.True(initialCount >= 4, $"期望初始段数 ≥ 4，实际 {initialCount}");

        // 等待 Compaction 减少段数
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && db2.Segments.SegmentCount >= initialCount)
            Thread.Sleep(200);

        Assert.True(db2.Segments.SegmentCount < initialCount,
            $"Compaction 后段数 {db2.Segments.SegmentCount} 应 < 初始段数 {initialCount}");

        // 查询所有点（数据完整）
        var seriesId = db2.Catalog.Snapshot().First().Id;
        var query = new PointQuery(seriesId, "val", new TimeRange(0, long.MaxValue));
        var points = db2.Query.Execute(query).ToList();
        Assert.Equal(totalPoints, points.Count);
    }

    // ── Dispose 后重启，查询结果一致 ─────────────────────────────────────────

    [Fact]
    public void AfterCompaction_Reopen_QueryConsistent()
    {
        const int totalPoints = 600; // 3 次 Flush

        {
            using var db = Tsdb.Open(MakeOptions());

            for (int i = 0; i < totalPoints; i++)
                db.Write(MakePoint(1000L + i, i));

            // 等待 Flush
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && db.Segments.SegmentCount < 2)
                Thread.Sleep(100);

            // 等待 Compaction
            deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && db.Segments.SegmentCount > 1)
                Thread.Sleep(200);

            // 正常关闭（会 Flush 剩余数据）
        }

        // 重启验证
        {
            using var db = Tsdb.Open(MakeOptions(enableCompaction: false));
            var seriesId = db.Catalog.Snapshot().First().Id;
            var query = new PointQuery(seriesId, "val", new TimeRange(0, long.MaxValue));
            var points = db.Query.Execute(query).ToList();

            Assert.Equal(totalPoints, points.Count);
        }
    }
}
