using SonnetDB.Catalog;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Wal;
using Xunit;

namespace SonnetDB.Core.Tests.Memory;

/// <summary>
/// MemTable 集成测试：模拟从 WAL 到 MemTable 的完整链路。
/// </summary>
public sealed class MemTableIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public MemTableIntegrationTests()
    {
        _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string TempFile() =>
        System.IO.Path.Combine(_tempDir, System.IO.Path.GetRandomFileName() + ".SDBWAL");

    [Fact]
    public void WalToMemTable_TwoSeries_CorrectStructure()
    {
        string walPath = TempFile();
        var tags1 = new Dictionary<string, string> { ["host"] = "a" };
        var tags2 = new Dictionary<string, string> { ["host"] = "b" };

        // Step 1: Pre-compute series IDs
        var precomputedCatalog = new SeriesCatalog();
        var entry1 = precomputedCatalog.GetOrAdd("cpu", tags1);
        var entry2 = precomputedCatalog.GetOrAdd("cpu", tags2);

        // Step 2: Write WAL with CreateSeries + WritePoint records
        using (var writer = WalWriter.Open(walPath))
        {
            writer.AppendCreateSeries(entry1.Id, "cpu", tags1);
            writer.AppendCreateSeries(entry2.Id, "cpu", tags2);

            // Series 1: 5 points for "usage" field
            for (int i = 0; i < 5; i++)
                writer.AppendWritePoint(entry1.Id, 1000L + i * 100, "usage", FieldValue.FromDouble(10.0 + i));

            // Series 2: 3 points for "usage" field + 2 for "temp" field
            for (int i = 0; i < 3; i++)
                writer.AppendWritePoint(entry2.Id, 2000L + i * 100, "usage", FieldValue.FromDouble(20.0 + i));
            for (int i = 0; i < 2; i++)
                writer.AppendWritePoint(entry2.Id, 3000L + i * 100, "temp", FieldValue.FromDouble(70.0 + i));

            writer.Sync();
        }

        // Step 3: Replay WAL and load into MemTable
        var catalog = new SeriesCatalog();
        var memTable = new MemTable();
        var records = WalReplay.ReplayInto(walPath, catalog);
        int replayed = memTable.ReplayFrom(records);

        // Step 4: Assert catalog
        Assert.Equal(2, catalog.Count);

        // Step 5: Assert MemTable structure
        // 3 distinct (SeriesId, FieldName) buckets: (entry1, usage), (entry2, usage), (entry2, temp)
        Assert.Equal(replayed, (int)memTable.PointCount);
        Assert.Equal(10, replayed);
        Assert.Equal(3, memTable.SeriesCount);

        // entry1 has 1 bucket: "usage"
        var entry1Buckets = memTable.GetBySeries(entry1.Id);
        Assert.Single(entry1Buckets);
        Assert.Equal("usage", entry1Buckets[0].Key.FieldName);
        Assert.Equal(5, entry1Buckets[0].Count);

        // entry2 has 2 buckets: "temp" and "usage" (sorted by FieldName)
        var entry2Buckets = memTable.GetBySeries(entry2.Id);
        Assert.Equal(2, entry2Buckets.Count);
        Assert.Equal("temp", entry2Buckets[0].Key.FieldName);
        Assert.Equal("usage", entry2Buckets[1].Key.FieldName);
        Assert.Equal(2, entry2Buckets[0].Count);
        Assert.Equal(3, entry2Buckets[1].Count);
    }

    [Fact]
    public void WalToMemTable_SnapshotOrdered_CorrectLsn()
    {
        string walPath = TempFile();
        var tags = new Dictionary<string, string> { ["host"] = "srv" };

        var precomputedCatalog = new SeriesCatalog();
        var entry = precomputedCatalog.GetOrAdd("sensor", tags);

        // Write out-of-order timestamps
        using (var writer = WalWriter.Open(walPath))
        {
            writer.AppendCreateSeries(entry.Id, "sensor", tags);
            writer.AppendWritePoint(entry.Id, 5000L, "v", FieldValue.FromDouble(5.0));
            writer.AppendWritePoint(entry.Id, 1000L, "v", FieldValue.FromDouble(1.0));
            writer.AppendWritePoint(entry.Id, 3000L, "v", FieldValue.FromDouble(3.0));
            writer.Sync();
        }

        var catalog = new SeriesCatalog();
        var memTable = new MemTable();
        memTable.ReplayFrom(WalReplay.ReplayInto(walPath, catalog));

        var bucket = memTable.TryGet(new SonnetDB.Model.SeriesFieldKey(entry.Id, "v"));
        Assert.NotNull(bucket);

        var snap = bucket.Snapshot();
        Assert.Equal(3, snap.Length);

        // Should be sorted by timestamp
        Assert.Equal(1000L, snap.Span[0].Timestamp);
        Assert.Equal(3000L, snap.Span[1].Timestamp);
        Assert.Equal(5000L, snap.Span[2].Timestamp);

        // LastLsn should be the last WritePoint LSN
        // CreateSeries = lsn 1, WritePoint 1 = lsn 2, WritePoint 2 = lsn 3, WritePoint 3 = lsn 4
        Assert.Equal(4L, memTable.LastLsn);
        Assert.Equal(2L, memTable.FirstLsn);
    }

    [Fact]
    public void WalToMemTable_Reset_ClearsAndAllowsReuse()
    {
        string walPath = TempFile();

        using (var writer = WalWriter.Open(walPath))
        {
            writer.AppendWritePoint(1UL, 1000L, "v", FieldValue.FromDouble(1.0));
            writer.Sync();
        }

        var memTable = new MemTable();
        memTable.Append(1UL, 1000L, "v", FieldValue.FromDouble(1.0), 1L);

        Assert.Equal(1, memTable.SeriesCount);
        Assert.Equal(1L, memTable.PointCount);

        memTable.Reset();

        Assert.Equal(0, memTable.SeriesCount);
        Assert.Equal(0L, memTable.PointCount);
        Assert.Equal(long.MinValue, memTable.FirstLsn);
        Assert.Equal(long.MinValue, memTable.LastLsn);

        // Can be reused after reset
        memTable.Append(2UL, 2000L, "w", FieldValue.FromDouble(2.0), 10L);
        Assert.Equal(1, memTable.SeriesCount);
        Assert.Equal(10L, memTable.FirstLsn);
    }
}
