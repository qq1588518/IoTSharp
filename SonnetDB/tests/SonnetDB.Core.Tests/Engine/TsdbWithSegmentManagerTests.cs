using SonnetDB.Engine;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Engine;

/// <summary>
/// <see cref="Tsdb"/> + <see cref="SegmentManager"/> 端到端集成测试。
/// </summary>
public sealed class TsdbWithSegmentManagerTests : IDisposable
{
    private readonly string _tempDir;

    public TsdbWithSegmentManagerTests()
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
            FlushPolicy = new MemTableFlushPolicy { MaxPoints = 1_000_000, MaxBytes = 64 * 1024 * 1024 },
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
        };

    private static Point MakePoint(string measurement, long ts, double value) =>
        Point.Create(measurement, ts,
            new Dictionary<string, string> { ["host"] = "s1" },
            new Dictionary<string, FieldValue> { ["cpu"] = FieldValue.FromDouble(value) });

    // ── Open 已有 N 段的目录 ────────────────────────────────────────────────

    [Fact]
    public void Open_WithExistingSegments_SegmentCountEqualsN()
    {
        const int n = 3;

        // 先写入 n 个段
        using (var db = Tsdb.Open(MakeOptions()))
        {
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < 5; j++)
                {
                    db.Write(MakePoint("cpu", 1000L + i * 10000L + j, i + j));
                }
                db.FlushNow();
            }
        }

        // 重新打开
        using var db2 = Tsdb.Open(MakeOptions());
        Assert.Equal(n, db2.Segments.SegmentCount);
    }

    // ── FlushNow 后新段立即可查 ──────────────────────────────────────────────

    [Fact]
    public void FlushNow_NewSegmentImmediatelyQueryable()
    {
        using var db = Tsdb.Open(MakeOptions());

        Assert.Equal(0, db.Segments.SegmentCount);

        db.Write(MakePoint("m", 5000L, 1.0));
        db.Write(MakePoint("m", 5100L, 2.0));

        var result = db.FlushNow();
        Assert.NotNull(result);

        Assert.Equal(1, db.Segments.SegmentCount);

        // 通过 LookupCandidates 能查到刚 flush 的 Block
        var idx = db.Segments.Index;
        var series = db.Catalog.Snapshot().First();
        var candidates = idx.LookupCandidates(series.Id, 5000L, 5100L);
        Assert.NotEmpty(candidates);
    }

    [Fact]
    public void FlushNow_MultipleFlushes_SegmentCountIncreases()
    {
        using var db = Tsdb.Open(MakeOptions());

        for (int flush = 0; flush < 3; flush++)
        {
            for (int i = 0; i < 5; i++)
                db.Write(MakePoint("m", 1000L + flush * 10000L + i, flush + i));
            db.FlushNow();
        }

        Assert.Equal(3, db.Segments.SegmentCount);
    }

    // ── Dispose 后 SegmentManager 已 Dispose ─────────────────────────────────

    [Fact]
    public void Dispose_SegmentManagerIsDisposed()
    {
        SegmentManager capturedMgr;
        using (var db = Tsdb.Open(MakeOptions()))
        {
            db.Write(MakePoint("m", 1000L, 1.0));
            db.FlushNow();
            capturedMgr = db.Segments;
        }

        // After Tsdb.Dispose, the SegmentManager should be disposed too
        Assert.Throws<ObjectDisposedException>(() =>
        {
            string fakePath = Path.Combine(_tempDir, "fake.SDBSEG");
            capturedMgr.AddSegment(fakePath);
        });
    }

    // ── LookupCandidates 按字段查询 ──────────────────────────────────────────

    [Fact]
    public void LookupCandidates_AfterFlush_ReturnsCorrectBlocks()
    {
        using var db = Tsdb.Open(MakeOptions());

        long minTs = 10000L;
        long maxTs = 10900L;
        for (long ts = minTs; ts <= maxTs; ts += 100)
            db.Write(MakePoint("sensor", ts, (double)ts));

        db.FlushNow();

        var series = db.Catalog.Snapshot().First();
        var candidates = db.Segments.Index.LookupCandidates(series.Id, "cpu", minTs, maxTs);

        Assert.NotEmpty(candidates);
        Assert.All(candidates, c =>
        {
            Assert.Equal("cpu", c.FieldName);
            Assert.True(c.MinTimestamp <= maxTs);
            Assert.True(c.MaxTimestamp >= minTs);
        });
    }
}
