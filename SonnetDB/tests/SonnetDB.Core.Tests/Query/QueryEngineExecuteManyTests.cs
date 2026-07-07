using SonnetDB.Engine;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Query;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Query;

/// <summary>
/// <see cref="QueryEngine.ExecuteMany"/> 批量聚合的单元测试。
/// </summary>
public sealed class QueryEngineExecuteManyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TsdbOptions _opts;

    public QueryEngineExecuteManyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _opts = new TsdbOptions
        {
            RootDirectory = _tempDir,
            WalBufferSize = 64 * 1024,
            FlushPolicy = new MemTableFlushPolicy { MaxPoints = 1_000_000, MaxBytes = 64 * 1024 * 1024 },
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
        };
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── 辅助方法 ──────────────────────────────────────────────────────────────

    private static Point MakePoint(string measurement, string host, long ts, string field, FieldValue value)
        => Point.Create(measurement, ts,
            new Dictionary<string, string> { ["host"] = host },
            new Dictionary<string, FieldValue> { [field] = value });

    // ── ExecuteMany 5 个 series ────────────────────────────────────────────

    [Fact]
    public void ExecuteMany_FiveSeries_ReturnsDictionarySizeEqualFive()
    {
        using var db = Tsdb.Open(_opts);

        // 写 5 个不同 host 的序列，各 20 点
        for (int s = 0; s < 5; s++)
            for (int i = 0; i < 20; i++)
                db.Write(MakePoint("cpu", $"host{s}", 1000L + i, "usage", FieldValue.FromDouble(s * 100.0 + i)));

        var entries = db.Catalog.Snapshot().ToList();
        Assert.Equal(5, entries.Count);

        var seriesIds = entries.Select(e => e.Id).ToList();

        var result = db.Query.ExecuteMany(
            seriesIds, "usage", TimeRange.All, Aggregator.Count, 0);

        Assert.Equal(5, result.Count);
        Assert.All(result.Values, buckets =>
        {
            Assert.Single(buckets);
            Assert.Equal(20L, buckets[0].Count);
        });
    }

    [Fact]
    public void ExecuteMany_ResultConsistentWithSingleExecute()
    {
        using var db = Tsdb.Open(_opts);

        // 写 3 个序列
        for (int s = 0; s < 3; s++)
            for (int i = 1; i <= 10; i++)
                db.Write(MakePoint("sensor", $"s{s}", i * 1000L, "temp",
                    FieldValue.FromDouble(s * 10.0 + i)));

        var entries = db.Catalog.Snapshot().ToList();
        var seriesIds = entries.Select(e => e.Id).ToList();

        var manyResult = db.Query.ExecuteMany(
            seriesIds, "temp", TimeRange.All, Aggregator.Sum, 0);

        // 与单次查询比较
        foreach (var entry in entries)
        {
            var singleQ = new AggregateQuery(entry.Id, "temp", TimeRange.All, Aggregator.Sum, 0);
            var singleBuckets = db.Query.Execute(singleQ).ToList();

            Assert.True(manyResult.ContainsKey(entry.Id));
            var manyBuckets = manyResult[entry.Id];

            Assert.Equal(singleBuckets.Count, manyBuckets.Count);
            for (int i = 0; i < singleBuckets.Count; i++)
                Assert.Equal(singleBuckets[i].Value, manyBuckets[i].Value, precision: 9);
        }
    }

    [Fact]
    public void ExecuteMany_EmptySeriesList_ReturnsEmptyDictionary()
    {
        using var db = Tsdb.Open(_opts);

        var result = db.Query.ExecuteMany(
            Array.Empty<ulong>(), "usage", TimeRange.All, Aggregator.Count, 0);

        Assert.Empty(result);
    }
}
