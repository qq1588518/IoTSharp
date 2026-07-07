using SonnetDB.Catalog;
using SonnetDB.Model;
using SonnetDB.Storage.Format;
using SonnetDB.Wal;
using Xunit;

namespace SonnetDB.Core.Tests.Wal;

/// <summary>
/// <see cref="WalReplay.ReplayIntoWithCheckpoint"/> 的单元测试。
/// </summary>
public sealed class WalReplayCheckpointTests : IDisposable
{
    private readonly string _tempDir;

    public WalReplayCheckpointTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string TempFile() => Path.Combine(_tempDir, Path.GetRandomFileName() + ".SDBWAL");

    /// <summary>
    /// 写 100 条 WritePoint（LSN 2..101）+ 1 条 Checkpoint(50) + 50 条 WritePoint（LSN 103..152）。
    /// 期望：CheckpointLsn==50，WritePoints 仅包含 LSN > 50 的记录（100 条）。
    /// </summary>
    [Fact]
    public void ReplayIntoWithCheckpoint_WithCheckpoint_SkipsEarlierWritePoints()
    {
        string path = TempFile();
        var tags = new Dictionary<string, string> { ["host"] = "srv1" };
        var preCatalog = new SeriesCatalog();
        var entry = preCatalog.GetOrAdd("cpu", tags);

        using (var writer = WalWriter.Open(path))
        {
            // CreateSeries（LSN 1）
            writer.AppendCreateSeries(entry.Id, "cpu", tags);

            // 100 条 WritePoint（LSN 2..101）
            for (int i = 0; i < 100; i++)
                writer.AppendWritePoint(entry.Id, 1000L + i, "usage", FieldValue.FromDouble(i));

            // 1 条 Checkpoint（LSN 102，checkpointLsn=50）
            writer.AppendCheckpoint(50);

            // 50 条 WritePoint（LSN 103..152）
            for (int i = 100; i < 150; i++)
                writer.AppendWritePoint(entry.Id, 2000L + i, "usage", FieldValue.FromDouble(i));

            writer.Sync();
        }

        var catalog = new SeriesCatalog();
        var result = WalReplay.ReplayIntoWithCheckpoint(path, catalog);

        Assert.Equal(50, result.CheckpointLsn);
        Assert.Equal(152, result.LastLsn);
        // LSN > 50 的 WritePoint：LSN 51..101（51 条）+ LSN 103..152（50 条）= 101 条
        Assert.Equal(101, result.WritePoints.Count);
        Assert.All(result.WritePoints, wp => Assert.True(wp.Lsn > 50));
        Assert.Equal(1, catalog.Count);
    }

    /// <summary>
    /// 多个 Checkpoint 记录时，应取最大值。
    /// </summary>
    [Fact]
    public void ReplayIntoWithCheckpoint_MultipleCheckpoints_TakesMax()
    {
        string path = TempFile();
        var tags = new Dictionary<string, string> { ["host"] = "srv1" };
        var preCatalog = new SeriesCatalog();
        var entry = preCatalog.GetOrAdd("sensor", tags);

        using (var writer = WalWriter.Open(path))
        {
            writer.AppendCreateSeries(entry.Id, "sensor", tags);

            for (int i = 0; i < 20; i++)
                writer.AppendWritePoint(entry.Id, 1000L + i, "temp", FieldValue.FromDouble(i));

            writer.AppendCheckpoint(10); // 第一个 Checkpoint

            for (int i = 20; i < 40; i++)
                writer.AppendWritePoint(entry.Id, 2000L + i, "temp", FieldValue.FromDouble(i));

            writer.AppendCheckpoint(30); // 第二个（更大）Checkpoint

            for (int i = 40; i < 50; i++)
                writer.AppendWritePoint(entry.Id, 3000L + i, "temp", FieldValue.FromDouble(i));

            writer.Sync();
        }

        var catalog = new SeriesCatalog();
        var result = WalReplay.ReplayIntoWithCheckpoint(path, catalog);

        Assert.Equal(30, result.CheckpointLsn);
        // 仅返回 LSN > 30 的 WritePoint
        Assert.All(result.WritePoints, wp => Assert.True(wp.Lsn > 30));
    }

    /// <summary>
    /// 没有 Checkpoint 时，CheckpointLsn 应为 0，所有 WritePoint 均返回。
    /// </summary>
    [Fact]
    public void ReplayIntoWithCheckpoint_NoCheckpoint_CheckpointLsnIsZero_AllWritePointsReturned()
    {
        string path = TempFile();
        var tags = new Dictionary<string, string> { ["id"] = "s1" };
        var preCatalog = new SeriesCatalog();
        var entry = preCatalog.GetOrAdd("mem", tags);

        using (var writer = WalWriter.Open(path))
        {
            writer.AppendCreateSeries(entry.Id, "mem", tags);
            for (int i = 0; i < 30; i++)
                writer.AppendWritePoint(entry.Id, 1000L + i, "free", FieldValue.FromLong(i * 1000));
            writer.Sync();
        }

        var catalog = new SeriesCatalog();
        var result = WalReplay.ReplayIntoWithCheckpoint(path, catalog);

        Assert.Equal(0, result.CheckpointLsn);
        Assert.Equal(30, result.WritePoints.Count);
        Assert.Equal(1, catalog.Count);
    }

    /// <summary>
    /// 空 WAL（仅文件头）时，返回全零结果。
    /// </summary>
    [Fact]
    public void ReplayIntoWithCheckpoint_EmptyWal_ReturnsZeroResult()
    {
        string path = TempFile();

        // 创建空 WAL（只有文件头）
        using (var writer = WalWriter.Open(path))
        {
            writer.Sync();
        }

        var catalog = new SeriesCatalog();
        var result = WalReplay.ReplayIntoWithCheckpoint(path, catalog);

        Assert.Equal(0, result.CheckpointLsn);
        Assert.Equal(0, result.LastLsn);
        Assert.Empty(result.WritePoints);
        Assert.Equal(0, catalog.Count);
    }

    /// <summary>
    /// WalReader 截断容忍：最后一条记录损坏时，前面的记录应正常保留。
    /// </summary>
    [Fact]
    public void ReplayIntoWithCheckpoint_TruncatedWal_ToleratesCorruption()
    {
        string path = TempFile();
        var tags = new Dictionary<string, string> { ["host"] = "h1" };
        var preCatalog = new SeriesCatalog();
        var entry = preCatalog.GetOrAdd("cpu", tags);

        using (var writer = WalWriter.Open(path))
        {
            writer.AppendCreateSeries(entry.Id, "cpu", tags);
            for (int i = 0; i < 10; i++)
                writer.AppendWritePoint(entry.Id, 1000L + i, "usage", FieldValue.FromDouble(i));
            writer.Sync();
        }

        // 截断最后几个字节，模拟最后一条记录损坏（先移除可选 footer，避免只截断元数据）。
        long fileLen = new FileInfo(path).Length;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write))
        {
            fs.SetLength(fileLen - FormatSizes.WalLastLsnFooterSize - 5);
        }

        var catalog = new SeriesCatalog();
        var result = WalReplay.ReplayIntoWithCheckpoint(path, catalog);

        // 至少 9 条 WritePoint 被保留（最后一条可能损坏被丢弃）
        Assert.True(result.WritePoints.Count >= 9);
        Assert.Equal(0, result.CheckpointLsn);
    }

    /// <summary>
    /// Checkpoint 之后没有 WritePoint 时，返回空 WritePoints 列表。
    /// </summary>
    [Fact]
    public void ReplayIntoWithCheckpoint_CheckpointAtEnd_EmptyWritePoints()
    {
        string path = TempFile();
        var tags = new Dictionary<string, string> { ["host"] = "srv1" };
        var preCatalog = new SeriesCatalog();
        var entry = preCatalog.GetOrAdd("net", tags);

        using (var writer = WalWriter.Open(path))
        {
            writer.AppendCreateSeries(entry.Id, "net", tags);
            for (int i = 0; i < 20; i++)
                writer.AppendWritePoint(entry.Id, 1000L + i, "bytes", FieldValue.FromLong(i));

            // Checkpoint 设置为 21（LSN 1 是 CreateSeries，LSN 2..21 是 WritePoints）
            writer.AppendCheckpoint(21);
            writer.Sync();
        }

        var catalog = new SeriesCatalog();
        var result = WalReplay.ReplayIntoWithCheckpoint(path, catalog);

        // checkpoint LSN 为 21，WritePoint LSN 范围 2..21，全部被跳过
        Assert.Empty(result.WritePoints);
        Assert.Equal(21, result.CheckpointLsn);
        Assert.Equal(1, catalog.Count);
    }
}
