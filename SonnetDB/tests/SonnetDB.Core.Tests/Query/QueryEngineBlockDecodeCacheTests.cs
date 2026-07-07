using SonnetDB.Engine;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Query;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Query;

/// <summary>
/// <see cref="QueryEngine"/> 使用 SegmentReader block 解码缓存的回归测试。
/// </summary>
public sealed class QueryEngineBlockDecodeCacheTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TsdbOptions _options;

    public QueryEngineBlockDecodeCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _options = new TsdbOptions
        {
            RootDirectory = _tempDir,
            FlushPolicy = new MemTableFlushPolicy { MaxPoints = 1_000_000, MaxBytes = 64 * 1024 * 1024 },
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
            SegmentReaderOptions = new SegmentReaderOptions { DecodeBlockCacheMaxBytes = 1024 * 1024 },
        };
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Execute_RepeatedPointQuery_HitsBlockDecodeCache()
    {
        using var db = Tsdb.Open(_options);
        for (int i = 0; i < 32; i++)
        {
            db.Write(Point.Create(
                "cpu",
                1000L + i,
                new Dictionary<string, string> { ["host"] = "h1" },
                new Dictionary<string, FieldValue> { ["usage"] = FieldValue.FromDouble(i) }));
        }
        db.FlushNow();

        var series = db.Catalog.Snapshot().Single();
        var query = new PointQuery(series.Id, "usage", new TimeRange(1008L, 1015L));
        var reader = Assert.Single(db.Segments.Readers);

        var first = db.Query.Execute(query).ToArray();
        long hitsAfterFirst = reader.DecodeCacheHitCount;
        long missesAfterFirst = reader.DecodeCacheMissCount;

        var second = db.Query.Execute(query).ToArray();

        Assert.Equal(first, second);
        Assert.Equal(8, second.Length);
        Assert.True(missesAfterFirst >= 1);
        Assert.True(
            reader.DecodeCacheHitCount > hitsAfterFirst,
            $"Expected repeated query to hit decode cache. Hits before={hitsAfterFirst}, after={reader.DecodeCacheHitCount}.");
        Assert.Equal(missesAfterFirst, reader.DecodeCacheMissCount);
    }
}
