using SonnetDB.Engine;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Query;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Query;

/// <summary>
/// <see cref="QueryEngine"/> 端到端集成测试：验证写入 → Flush → 查询 → Dispose → 重新 Open → 查询的完整链路。
/// </summary>
public sealed class QueryEngineIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TsdbOptions _opts;

    public QueryEngineIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _opts = new TsdbOptions
        {
            RootDirectory = _tempDir,
            WalBufferSize = 64 * 1024,
            FlushPolicy = new MemTableFlushPolicy { MaxPoints = 10_000_000, MaxBytes = 512 * 1024 * 1024 },
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
        };
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── 辅助方法 ──────────────────────────────────────────────────────────────

    private static Point MakePoint(string measurement, string host, long ts, FieldValue value)
        => Point.Create(measurement, ts,
            new Dictionary<string, string> { ["host"] = host },
            new Dictionary<string, FieldValue> { ["v"] = value });

    // ── 端到端：5 series × 1000 点 ──────────────────────────────────────────

    [Fact]
    public void EndToEnd_FiveSeriesThousandPoints_AllQueriesCorrect()
    {
        const int seriesCount = 5;
        const int pointsPerSeries = 1000;
        const int flushCount = 500;

        using (var db = Tsdb.Open(_opts))
        {
            // 写前 500 点
            for (int s = 0; s < seriesCount; s++)
                for (int i = 0; i < flushCount; i++)
                    db.Write(MakePoint("m", $"host{s}", i * 100L, FieldValue.FromDouble(i)));

            db.FlushNow();

            // 再写 500 点（未 Flush，在 MemTable 中）
            for (int s = 0; s < seriesCount; s++)
                for (int i = flushCount; i < pointsPerSeries; i++)
                    db.Write(MakePoint("m", $"host{s}", i * 100L, FieldValue.FromDouble(i)));

            var entries = db.Catalog.Snapshot().ToList();
            Assert.Equal(seriesCount, entries.Count);

            // 单 series 全量点查询 == 写入总量
            foreach (var entry in entries)
            {
                var q = new PointQuery(entry.Id, "v", TimeRange.All);
                var results = db.Query.Execute(q).ToList();
                Assert.Equal(pointsPerSeries, results.Count);

                // 验证升序
                for (int i = 1; i < results.Count; i++)
                    Assert.True(results[i].Timestamp >= results[i - 1].Timestamp);
            }

            // 全局 Count 聚合 == 写入总量
            foreach (var entry in entries)
            {
                var q = new AggregateQuery(entry.Id, "v", TimeRange.All, Aggregator.Count, 0);
                var result = db.Query.Execute(q).ToList();
                Assert.Single(result);
                Assert.Equal((long)pointsPerSeries, result[0].Count);
            }

            // BucketSizeMs=10000 桶聚合（每桶 100 点，共 10 桶）
            foreach (var entry in entries)
            {
                var q = new AggregateQuery(entry.Id, "v", TimeRange.All, Aggregator.Count, 10000L);
                var result = db.Query.Execute(q).ToList();
                // 1000 点 × 100ms/点 = 100000ms，桶大小 10000ms → 10 桶
                Assert.Equal(10, result.Count);
                Assert.All(result, b => Assert.Equal(100L, b.Count));
            }

            // ExecuteMany 结果合并正确
            var seriesIds = entries.Select(e => e.Id).ToList();
            var manyResult = db.Query.ExecuteMany(
                seriesIds, "v", TimeRange.All, Aggregator.Count, 0);

            Assert.Equal(seriesCount, manyResult.Count);
            Assert.All(manyResult.Values, buckets =>
            {
                Assert.Single(buckets);
                Assert.Equal((long)pointsPerSeries, buckets[0].Count);
            });
        }

        // ── 崩溃恢复后查询 ───────────────────────────────────────────────────
        using (var db2 = Tsdb.Open(_opts))
        {
            var entries = db2.Catalog.Snapshot().ToList();
            Assert.Equal(seriesCount, entries.Count);

            // 重新打开后，同样查询应返回相同结果
            foreach (var entry in entries)
            {
                var q = new PointQuery(entry.Id, "v", TimeRange.All);
                var results = db2.Query.Execute(q).ToList();
                Assert.Equal(pointsPerSeries, results.Count);
            }

            foreach (var entry in entries)
            {
                var q = new AggregateQuery(entry.Id, "v", TimeRange.All, Aggregator.Count, 0);
                var result = db2.Query.Execute(q).ToList();
                Assert.Single(result);
                Assert.Equal((long)pointsPerSeries, result[0].Count);
            }
        }
    }

    // ── 开空目录查询无数据 ────────────────────────────────────────────────────

    [Fact]
    public void Open_EmptyDirectory_QueryReturnsEmpty()
    {
        using var db = Tsdb.Open(_opts);

        // 未写任何数据
        var q = new PointQuery(0x1234_5678_9ABC_DEFuL, "v", TimeRange.All);
        var results = db.Query.Execute(q).ToList();
        Assert.Empty(results);
    }

    // ── Dispose → 重新 Open → 查询相同结果 ───────────────────────────────────

    [Fact]
    public void ReopenAfterDispose_QueryReturnsSameResults()
    {
        const int totalPoints = 100;

        // 第一次打开并写数据
        ulong seriesId;
        using (var db = Tsdb.Open(_opts))
        {
            for (int i = 0; i < totalPoints; i++)
                db.Write(MakePoint("sensor", "node1", i * 10L, FieldValue.FromDouble(i)));

            db.FlushNow();

            seriesId = db.Catalog.Snapshot().First().Id;
        }

        // 重新打开后查询
        using var db2 = Tsdb.Open(_opts);
        var q = new PointQuery(seriesId, "v", TimeRange.All);
        var results = db2.Query.Execute(q).ToList();

        Assert.Equal(totalPoints, results.Count);
        for (int i = 1; i < results.Count; i++)
            Assert.True(results[i].Timestamp >= results[i - 1].Timestamp);
    }
}
