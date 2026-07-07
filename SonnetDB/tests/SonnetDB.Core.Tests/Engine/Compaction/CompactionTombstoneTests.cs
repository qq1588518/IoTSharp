using SonnetDB.Engine;
using SonnetDB.Engine.Compaction;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Query;
using SonnetDB.Storage.Format;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Engine.Compaction;

/// <summary>
/// <see cref="SegmentCompactor"/> + <see cref="TombstoneTable"/> 的集成测试：验证 Compaction 时物理删除被墓碑覆盖的数据点。
/// </summary>
public sealed class CompactionTombstoneTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SegmentWriter _writer = new(new SegmentWriterOptions { FsyncOnCommit = false });
    private readonly SegmentCompactor _compactor = new(new SegmentWriterOptions { FsyncOnCommit = false });
    private readonly SegmentReaderOptions _readerOpts = new() { VerifyIndexCrc = true, VerifyBlockCrc = true };

    public CompactionTombstoneTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, TsdbPaths.SegmentsDirName));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string SegPath(long segId) => TsdbPaths.SegmentPath(_tempDir, segId);

    private SegmentReader WriteSegment(long segId, ulong seriesId, string field,
        long startTs, int count)
    {
        var mt = new MemTable();
        for (int i = 0; i < count; i++)
            mt.Append(seriesId, startTs + i, field, FieldValue.FromDouble(startTs + i), i + 1L);
        string path = SegPath(segId);
        _writer.WriteFrom(mt, segId, path);
        return SegmentReader.Open(path, _readerOpts);
    }

    private static DataPoint[] ReadAllPoints(string segPath, ulong seriesId, string field)
    {
        using var reader = SegmentReader.Open(segPath, new SegmentReaderOptions());
        var index = SegmentIndex.Build(reader, reader.Header.SegmentId);
        var blocks = index.GetBlocks(seriesId, field, long.MinValue, long.MaxValue);
        return blocks.SelectMany(b => reader.DecodeBlock(b)).OrderBy(p => p.Timestamp).ToArray();
    }

    [Fact]
    public void Execute_WithTombstone_PhysicallyRemovesCoveredPoints()
    {
        // 2 segments × 100 points each, plus a tombstone covering 50 of them
        ulong series = 0xABCDUL;
        const string field = "val";

        using var r1 = WriteSegment(1, series, field, startTs: 0, count: 100);
        using var r2 = WriteSegment(2, series, field, startTs: 100, count: 100);

        var tombstones = new TombstoneTable();
        tombstones.Add(new Tombstone(series, field, 50L, 99L, 1L)); // 50 points

        var plan = new CompactionPlan(0, new long[] { 1, 2 }.AsReadOnly());
        var readerDict = new Dictionary<long, SegmentReader> { [1] = r1, [2] = r2 };
        string outPath = Path.Combine(_tempDir, "out.SDBSEG");

        var result = _compactor.Execute(plan, readerDict, 100, outPath, tombstones);

        Assert.True(File.Exists(outPath));

        var points = ReadAllPoints(outPath, series, field);
        Assert.Equal(150, points.Length); // 200 - 50

        Assert.All(points, p => Assert.False(p.Timestamp >= 50 && p.Timestamp <= 99));
    }

    [Fact]
    public void Execute_TombstoneCoveredAllPoints_NoBlockGenerated()
    {
        ulong series = 0xDEADUL;
        const string field = "temp";

        using var r1 = WriteSegment(1, series, field, startTs: 0, count: 100);

        var tombstones = new TombstoneTable();
        tombstones.Add(new Tombstone(series, field, 0L, 99L, 1L)); // All 100 points

        var plan = new CompactionPlan(0, new long[] { 1 }.AsReadOnly());
        var readerDict = new Dictionary<long, SegmentReader> { [1] = r1 };
        string outPath = Path.Combine(_tempDir, "out.SDBSEG");

        _compactor.Execute(plan, readerDict, 100, outPath, tombstones);

        using var outReader = SegmentReader.Open(outPath, _readerOpts);
        var outIndex = SegmentIndex.Build(outReader, outReader.Header.SegmentId);
        var blocks = outIndex.GetBlocks(series, field, long.MinValue, long.MaxValue);

        // No blocks for the covered series/field
        Assert.Empty(blocks);
    }

    [Fact]
    public void Execute_TombstonePartialCoverage_CorrectPointsRemain()
    {
        ulong series = 0x1234UL;
        const string field = "pressure";

        using var r1 = WriteSegment(1, series, field, startTs: 0, count: 200);

        var tombstones = new TombstoneTable();
        tombstones.Add(new Tombstone(series, field, 0L, 99L, 1L)); // First 100 points

        var plan = new CompactionPlan(0, new long[] { 1 }.AsReadOnly());
        var readerDict = new Dictionary<long, SegmentReader> { [1] = r1 };
        string outPath = Path.Combine(_tempDir, "out.SDBSEG");

        _compactor.Execute(plan, readerDict, 100, outPath, tombstones);

        var points = ReadAllPoints(outPath, series, field);

        Assert.Equal(100, points.Length);
        Assert.All(points, p => Assert.True(p.Timestamp >= 100 && p.Timestamp <= 199));
    }

    [Fact]
    public void Execute_NoTombstones_AllPointsKept()
    {
        ulong series = 0x5678UL;
        const string field = "vol";

        using var r1 = WriteSegment(1, series, field, startTs: 0, count: 50);
        using var r2 = WriteSegment(2, series, field, startTs: 50, count: 50);

        var tombstones = new TombstoneTable(); // empty

        var plan = new CompactionPlan(0, new long[] { 1, 2 }.AsReadOnly());
        var readerDict = new Dictionary<long, SegmentReader> { [1] = r1, [2] = r2 };
        string outPath = Path.Combine(_tempDir, "out.SDBSEG");

        _compactor.Execute(plan, readerDict, 100, outPath, tombstones);

        var points = ReadAllPoints(outPath, series, field);
        Assert.Equal(100, points.Length);
    }

    [Fact]
    public void EndToEnd_TsdbWriteDeleteCompact_SegmentDataReduced()
    {
        // Use a separate temp dir for end-to-end test
        string e2eDir = Path.Combine(_tempDir, "e2e");
        Directory.CreateDirectory(e2eDir);

        var e2eOptions = new TsdbOptions
        {
            RootDirectory = e2eDir,
            WalBufferSize = 64 * 1024,
            FlushPolicy = new MemTableFlushPolicy { MaxPoints = 10_000_000, MaxBytes = 256 * 1024 * 1024 },
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
            SyncWalOnEveryWrite = false,
        };

        ulong seriesId;

        using (var db = Tsdb.Open(e2eOptions))
        {
            for (int i = 0; i < 300; i++)
            {
                var point = Point.Create("cpu", i,
                    new Dictionary<string, string> { ["host"] = "s1" },
                    new Dictionary<string, FieldValue> { ["usage"] = FieldValue.FromDouble(i) });
                db.Write(point);
            }

            var entry = db.Catalog.Snapshot().First();
            seriesId = entry.Id;

            db.Delete(seriesId, "usage", 100L, 199L); // Delete 100 points

            db.FlushNow(); // Flush to segment
        }

        // Verify via QueryEngine after reopening
        using (var db = Tsdb.Open(e2eOptions))
        {
            var q = new PointQuery(seriesId, "usage", new TimeRange(long.MinValue, long.MaxValue));
            var points = db.Query.Execute(q).ToList();

            Assert.Equal(200, points.Count);
            Assert.All(points, p => Assert.False(p.Timestamp >= 100 && p.Timestamp <= 199));
        }
    }
}
