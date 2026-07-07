using SonnetDB.Engine;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Query;
using SonnetDB.Storage.Segments;
using SonnetDB.Wal;
using Xunit;

namespace SonnetDB.Core.Tests.Engine;

/// <summary>
/// <see cref="Tsdb.Delete"/> 相关端到端测试。
/// </summary>
public sealed class TsdbDeleteTests : IDisposable
{
    private readonly string _tempDir;

    public TsdbDeleteTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static Point MakePoint(string measure, long ts, string field, double value) =>
        Point.Create(measure, ts,
            new Dictionary<string, string> { ["host"] = "srv1" },
            new Dictionary<string, FieldValue> { [field] = FieldValue.FromDouble(value) });

    private static IReadOnlyList<DataPoint> QueryAll(Tsdb db, ulong seriesId, string field)
    {
        var q = new PointQuery(seriesId, field, new TimeRange(long.MinValue, long.MaxValue));
        return db.Query.Execute(q).ToList().AsReadOnly();
    }

    private static TsdbOptions MakeOptions(
        string dir,
        TombstoneCheckpointOptions? tombstoneCheckpoint = null) =>
        new TsdbOptions
        {
            RootDirectory = dir,
            WalBufferSize = 64 * 1024,
            FlushPolicy = new MemTableFlushPolicy { MaxPoints = 10_000_000, MaxBytes = 256 * 1024 * 1024 },
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
            SyncWalOnEveryWrite = false,
            TombstoneCheckpoint = tombstoneCheckpoint ?? TombstoneCheckpointOptions.Default,
        };

    [Fact]
    public void Delete_InMemory_FiltersPoints()
    {
        using var db = Tsdb.Open(MakeOptions(_tempDir));

        // Write 1000 points ts 0..999
        for (int i = 0; i < 1000; i++)
            db.Write(MakePoint("cpu", i, "usage", i));

        var entry = db.Catalog.Snapshot().First();
        ulong seriesId = entry.Id;

        // Delete range [100, 200]
        db.Delete(seriesId, "usage", 100L, 200L);

        var points = QueryAll(db, seriesId, "usage");

        // 1000 - 101 (100..200 inclusive) = 899
        Assert.Equal(899, points.Count);
        Assert.All(points, p => Assert.False(p.Timestamp >= 100 && p.Timestamp <= 200));
    }

    [Fact]
    public void Delete_AfterWriteToSameRange_HidesNewPoints()
    {
        using var db = Tsdb.Open(MakeOptions(_tempDir));

        var entry = db.Catalog.GetOrAdd("cpu", new Dictionary<string, string> { ["host"] = "srv1" });
        ulong seriesId = entry.Id;

        // Delete range first
        db.Delete(seriesId, "usage", 100L, 200L);

        // Write points inside the deleted range
        for (int i = 100; i <= 200; i++)
            db.Write(MakePoint("cpu", i, "usage", i));

        var points = QueryAll(db, seriesId, "usage");

        // All newly written points inside [100, 200] should be hidden
        Assert.Empty(points);
    }

    [Fact]
    public void Delete_MultipleNonOverlappingWindows_CumulativeEffect()
    {
        using var db = Tsdb.Open(MakeOptions(_tempDir));

        for (int i = 0; i < 500; i++)
            db.Write(MakePoint("cpu", i, "usage", i));

        var entry = db.Catalog.Snapshot().First();
        ulong seriesId = entry.Id;

        db.Delete(seriesId, "usage", 0L, 49L);    // 50 points
        db.Delete(seriesId, "usage", 100L, 149L); // 50 points

        var points = QueryAll(db, seriesId, "usage");

        Assert.Equal(400, points.Count);
        Assert.All(points, p => Assert.False(p.Timestamp >= 0 && p.Timestamp <= 49));
        Assert.All(points, p => Assert.False(p.Timestamp >= 100 && p.Timestamp <= 149));
    }

    [Fact]
    public void Delete_AfterFlush_FilterStillApplied()
    {
        using var db = Tsdb.Open(MakeOptions(_tempDir));

        for (int i = 0; i < 200; i++)
            db.Write(MakePoint("cpu", i, "usage", i));

        var entry = db.Catalog.Snapshot().First();
        ulong seriesId = entry.Id;

        db.Delete(seriesId, "usage", 50L, 100L);

        db.FlushNow(); // Flush to segment

        var points = QueryAll(db, seriesId, "usage");

        // 200 - 51 (50..100 inclusive) = 149
        Assert.Equal(149, points.Count);
        Assert.All(points, p => Assert.False(p.Timestamp >= 50 && p.Timestamp <= 100));
    }

    [Fact]
    public void Delete_BeforeFlush_ThenWriteMore_StillFiltered()
    {
        using var db = Tsdb.Open(MakeOptions(_tempDir));

        for (int i = 0; i < 200; i++)
            db.Write(MakePoint("cpu", i, "usage", i));

        var entry = db.Catalog.Snapshot().First();
        ulong seriesId = entry.Id;

        db.FlushNow(); // Flush first 200 points to segment

        db.Delete(seriesId, "usage", 0L, 99L); // Delete from segment data

        // Write more points
        for (int i = 200; i < 300; i++)
            db.Write(MakePoint("cpu", i, "usage", i));

        var points = QueryAll(db, seriesId, "usage");

        // Points 100..199 (from segment) + 200..299 (from memtable) = 200 points
        Assert.Equal(200, points.Count);
    }

    [Fact]
    public void Delete_ByMeasurementAndTags_WorksCorrectly()
    {
        using var db = Tsdb.Open(MakeOptions(_tempDir));

        for (int i = 0; i < 100; i++)
            db.Write(MakePoint("cpu", i, "usage", i));

        var entry = db.Catalog.Snapshot().First();
        bool deleted = db.Delete("cpu", new Dictionary<string, string> { ["host"] = "srv1" }, "usage", 0L, 49L);

        Assert.True(deleted);

        var points = QueryAll(db, entry.Id, "usage");
        Assert.Equal(50, points.Count);
        Assert.All(points, p => Assert.True(p.Timestamp >= 50));
    }

    [Fact]
    public void Delete_ByMeasurementAndTags_SeriesNotExist_ReturnsFalse()
    {
        using var db = Tsdb.Open(MakeOptions(_tempDir));

        bool deleted = db.Delete("nonexistent", new Dictionary<string, string> { ["host"] = "srv1" }, "usage", 0L, 100L);

        Assert.False(deleted);
    }

    [Fact]
    public void CrashRecovery_TombstonesRestoredFromWal()
    {
        ulong seriesId;

        // Phase 1: Write + Delete, then crash (no flush)
        {
            var options = MakeOptions(_tempDir);
            using var db = Tsdb.Open(options);

            for (int i = 0; i < 200; i++)
                db.Write(MakePoint("cpu", i, "usage", i));

            var entry = db.Catalog.Snapshot().First();
            seriesId = entry.Id;

            db.Delete(seriesId, "usage", 50L, 100L);

            // Simulate crash: don't dispose gracefully
            db.CrashSimulationCloseWal();
        }

        // Phase 2: Reopen and verify tombstones are restored
        {
            var options = MakeOptions(_tempDir);
            using var db = Tsdb.Open(options);

            Assert.Equal(1, db.Tombstones.Count);
            Assert.True(db.Tombstones.IsCovered(seriesId, "usage", 75L));

            var points = QueryAll(db, seriesId, "usage");
            Assert.Equal(149, points.Count); // 200 - 51
        }
    }

    [Fact]
    public void CrashRecovery_ManifestPersistedOnFlush_WalDirDeletedThenReopen()
    {
        ulong seriesId;

        // Phase 1: Write + Delete + Flush (manifest gets written)
        {
            var options = MakeOptions(_tempDir);
            using var db = Tsdb.Open(options);

            for (int i = 0; i < 200; i++)
                db.Write(MakePoint("cpu", i, "usage", i));

            var entry = db.Catalog.Snapshot().First();
            seriesId = entry.Id;

            db.Delete(seriesId, "usage", 50L, 100L);
            db.FlushNow(); // This triggers manifest save as step 0
        }

        // Simulate loss of WAL by deleting WAL files (manifest should be the recovery source)
        string walDir = TsdbPaths.WalDir(_tempDir);
        foreach (var f in Directory.GetFiles(walDir))
            File.Delete(f);

        // Phase 2: Reopen - should load from manifest
        {
            var options = MakeOptions(_tempDir);
            using var db = Tsdb.Open(options);

            Assert.Equal(1, db.Tombstones.Count);
            Assert.True(db.Tombstones.IsCovered(seriesId, "usage", 75L));
        }
    }

    [Fact]
    public void CrashRecovery_PeriodicTombstoneCheckpointAfterManyDeletes_RestoresWithoutWalReplay()
    {
        ulong seriesId;
        var checkpoint = new TombstoneCheckpointOptions
        {
            Enabled = true,
            MaxDeletesSinceCheckpoint = 10,
            MaxInterval = TimeSpan.FromDays(1),
        };

        {
            using var db = Tsdb.Open(MakeOptions(_tempDir, checkpoint));

            for (int i = 0; i < 100; i++)
                db.Write(MakePoint("cpu", i, "usage", i));

            seriesId = db.Catalog.Snapshot().First().Id;
            db.FlushNow();

            for (int i = 0; i < 50; i++)
                db.Delete(seriesId, "usage", i, i);

            var manifest = TombstoneManifestCodec.Load(TsdbPaths.TombstoneManifestPath(_tempDir));
            Assert.Equal(50, manifest.Count);

            db.CrashSimulationCloseWal();
        }

        string walDir = TsdbPaths.WalDir(_tempDir);
        foreach (string file in Directory.GetFiles(walDir, "*.SDBWAL"))
            File.Delete(file);

        using var reopened = Tsdb.Open(MakeOptions(_tempDir, checkpoint));
        Assert.Equal(50, reopened.Tombstones.Count);
        Assert.True(reopened.Tombstones.IsCovered(seriesId, "usage", 25L));

        var points = QueryAll(reopened, seriesId, "usage");
        Assert.Equal(50, points.Count);
        Assert.All(points, static p => Assert.True(p.Timestamp >= 50));
    }

    [Fact]
    public void Delete_InvalidArguments_Throws()
    {
        using var db = Tsdb.Open(MakeOptions(_tempDir));

        Assert.Throws<ArgumentNullException>(() => db.Delete(1UL, null!, 0L, 100L));
        Assert.Throws<ArgumentException>(() => db.Delete(1UL, "", 0L, 100L));
        Assert.Throws<ArgumentOutOfRangeException>(() => db.Delete(1UL, "f", 200L, 100L));
    }
}
