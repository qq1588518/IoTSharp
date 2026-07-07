using SonnetDB.Engine;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Query;
using SonnetDB.Storage.Segments;
using SonnetDB.Wal;
using Xunit;

namespace SonnetDB.Core.Tests.Engine;

/// <summary>
/// Checkpoint LSN 驱动的 WAL Replay 跳过端到端测试。
/// </summary>
public sealed class TsdbCheckpointReplayTests : IDisposable
{
    private readonly string _tempDir;

    public TsdbCheckpointReplayTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private TsdbOptions MakeOptions(MemTableFlushPolicy? flushPolicy = null) =>
        new TsdbOptions
        {
            RootDirectory = _tempDir,
            WalBufferSize = 64 * 1024,
            FlushPolicy = flushPolicy ?? new MemTableFlushPolicy
            {
                MaxPoints = 10_000_000,
                MaxBytes = 1024L * 1024 * 1024,
                MaxAge = TimeSpan.FromHours(24),
            },
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
            BackgroundFlush = new BackgroundFlushOptions { Enabled = false },
        };

    private static Point MakePoint(string measurement, long timestamp, double value) =>
        Point.Create(measurement, timestamp,
            new Dictionary<string, string> { ["host"] = "srv1" },
            new Dictionary<string, FieldValue> { ["val"] = FieldValue.FromDouble(value) });

    /// <summary>
    /// 场景：Open → 写 1000 点 → FlushNow（生成 segment + Checkpoint）
    /// → 再写 100 点 → 模拟崩溃（关 WAL，不 Dispose）
    /// 重新 Open：MemTable 仅 100 点（前 1000 已被 Checkpoint 跳过），CheckpointLsn 不为 0。
    /// </summary>
    [Fact]
    public void Reopen_AfterFlushAndCrash_OnlyReplaysSince_Checkpoint()
    {
        // 会话 1：写 1000 点，Flush，再写 100 点，模拟崩溃
        {
            using var db = Tsdb.Open(MakeOptions());

            for (int i = 0; i < 1000; i++)
                db.Write(MakePoint("cpu", 1000L + i, i));

            var flushResult = db.FlushNow();
            Assert.NotNull(flushResult);

            // Flush 后 CheckpointLsn 应已更新
            Assert.True(db.CheckpointLsn > 0);

            for (int i = 0; i < 100; i++)
                db.Write(MakePoint("cpu", 10000L + i, i));

            // 模拟崩溃：不 Dispose，直接关闭 WAL
            db.CrashSimulationCloseWal();
        }

        // 会话 2：重新 Open，验证只回放 100 点
        {
            using var db = Tsdb.Open(MakeOptions());

            // MemTable 仅含 100 点（前 1000 已被 WAL 截断跳过，落盘在 segment）
            Assert.Equal(100L, db.MemTable.PointCount);

            // 段中应有数据（前 1000 点落盘）
            Assert.True(db.Segments.SegmentCount >= 1);
        }
    }

    /// <summary>
    /// 场景：Flush 之后再次 Open，查询结果应正确（前 1000 在 segment，新 100 在 MemTable）。
    /// </summary>
    [Fact]
    public void Reopen_AfterFlushAndCrash_QueryReturnsAllPoints()
    {
        // 会话 1：写 1000 点，Flush，再写 100 点，模拟崩溃
        {
            using var db = Tsdb.Open(MakeOptions());

            for (int i = 0; i < 1000; i++)
                db.Write(MakePoint("cpu", 1000L + i, i));

            db.FlushNow();

            for (int i = 0; i < 100; i++)
                db.Write(MakePoint("cpu", 10000L + i, i));

            db.CrashSimulationCloseWal();
        }

        // 会话 2：查询应返回全部 1100 点
        {
            using var db = Tsdb.Open(MakeOptions());

            var query = new PointQuery(
                db.Catalog.Snapshot().First().Id,
                "val",
                new TimeRange(0, long.MaxValue));

            var pts = db.Query.Execute(query).ToList();

            Assert.Equal(1100, pts.Count);
        }
    }

    /// <summary>
    /// 场景：正常关闭（Dispose）后重新 Open，MemTable 应为空（会话 1 的 Dispose 会 Flush 所有剩余数据）。
    /// </summary>
    [Fact]
    public void Reopen_AfterNormalClose_MemTableIsEmpty()
    {
        // 会话 1：写 50 点，正常关闭（Dispose 会触发 Flush）
        {
            using var db = Tsdb.Open(MakeOptions());

            for (int i = 0; i < 50; i++)
                db.Write(MakePoint("sensor", 1000L + i, i));
        }

        // 会话 2：重新 Open 后 MemTable 应为空（会话 1 的 Dispose 已 Flush 所有数据）
        {
            using var db = Tsdb.Open(MakeOptions());
            Assert.Equal(0L, db.MemTable.PointCount);
        }
    }

    /// <summary>
    /// CheckpointLsn 在 FlushNow 之后应更新为 Flush 前的 LastLsn。
    /// </summary>
    [Fact]
    public void CheckpointLsn_UpdatedAfterFlushNow()
    {
        using var db = Tsdb.Open(MakeOptions());

        Assert.Equal(0L, db.CheckpointLsn);

        for (int i = 0; i < 10; i++)
            db.Write(MakePoint("cpu", 1000L + i, i));

        long lsnBefore = db.MemTable.LastLsn;

        db.FlushNow();

        Assert.True(db.CheckpointLsn > 0);
        Assert.Equal(lsnBefore, db.CheckpointLsn);
    }

    [Fact]
    public void Reopen_AfterFlushAndWalRecycle_LoadsDurableCheckpointLsn()
    {
        long expectedCheckpointLsn;
        {
            var db = Tsdb.Open(MakeOptions());
            for (int i = 0; i < 10; i++)
                db.Write(MakePoint("cpu", 1000L + i, i));

            expectedCheckpointLsn = db.MemTable.LastLsn;
            db.FlushNow();
            Assert.True(File.Exists(WalSegmentLayout.CheckpointPath(TsdbPaths.WalDir(_tempDir))));
            db.CrashSimulationCloseWal();
        }

        using var reopened = Tsdb.Open(MakeOptions());
        Assert.Equal(expectedCheckpointLsn, reopened.CheckpointLsn);
        Assert.Equal(0L, reopened.MemTable.PointCount);
    }

    [Fact]
    public void Reopen_WithCheckpointTempFileOnly_ReplaysUnflushedWal()
    {
        var db = Tsdb.Open(MakeOptions());
        for (int i = 0; i < 3; i++)
            db.Write(MakePoint("cpu", 1000L + i, i));

        db.CrashSimulationCloseWal();

        string checkpointPath = WalSegmentLayout.CheckpointPath(TsdbPaths.WalDir(_tempDir));
        WalCheckpointFile.Save(
            checkpointPath,
            new WalCheckpointState(100L, 1L, 1L, DateTime.UtcNow.Ticks));
        File.Move(checkpointPath, checkpointPath + WalCheckpointFile.TempSuffix, overwrite: true);

        using var reopened = Tsdb.Open(MakeOptions());
        Assert.Equal(0L, reopened.CheckpointLsn);
        Assert.Equal(3L, reopened.MemTable.PointCount);
    }

    [Fact]
    public void Reopen_WithCheckpointForMissingSegment_ReplaysUnflushedWal()
    {
        var db = Tsdb.Open(MakeOptions());
        for (int i = 0; i < 3; i++)
            db.Write(MakePoint("cpu", 1000L + i, i));

        db.CrashSimulationCloseWal();

        WalCheckpointFile.Save(
            WalSegmentLayout.CheckpointPath(TsdbPaths.WalDir(_tempDir)),
            new WalCheckpointState(100L, 42L, 4096L, DateTime.UtcNow.Ticks));

        using var reopened = Tsdb.Open(MakeOptions());
        Assert.Equal(0L, reopened.CheckpointLsn);
        Assert.Equal(3L, reopened.MemTable.PointCount);
    }
}
