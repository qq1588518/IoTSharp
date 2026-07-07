using SonnetDB.Model;
using SonnetDB.Wal;
using Xunit;

namespace SonnetDB.Core.Tests.Wal;

/// <summary>
/// <see cref="WalWriter.AppendDelete"/> 和 <see cref="WalReader"/> Delete 记录的测试。
/// </summary>
public sealed class WalDeleteRecordTests : IDisposable
{
    private readonly string _tempDir;

    public WalDeleteRecordTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string TempFile() => Path.Combine(_tempDir, Path.GetRandomFileName() + ".SDBWAL");

    [Fact]
    public void AppendDelete_RoundTrip_RecordReadBack()
    {
        string path = TempFile();
        using (var writer = WalWriter.Open(path))
        {
            writer.AppendDelete(42UL, "temperature", 1000L, 2000L);
            writer.Sync();
        }

        using var reader = WalReader.Open(path);
        var records = reader.Replay().ToList();

        var deleteRecord = records.OfType<DeleteRecord>().Single();
        Assert.Equal(42UL, deleteRecord.SeriesId);
        Assert.Equal("temperature", deleteRecord.FieldName);
        Assert.Equal(1000L, deleteRecord.FromTimestamp);
        Assert.Equal(2000L, deleteRecord.ToTimestamp);
    }

    [Fact]
    public void AppendDelete_ChineseFieldName_RoundTrip()
    {
        string path = TempFile();
        string chineseFieldName = "温度字段";

        using (var writer = WalWriter.Open(path))
        {
            writer.AppendDelete(99UL, chineseFieldName, 500L, 600L);
            writer.Sync();
        }

        using var reader = WalReader.Open(path);
        var deleteRecord = reader.Replay().OfType<DeleteRecord>().Single();

        Assert.Equal(chineseFieldName, deleteRecord.FieldName);
        Assert.Equal(99UL, deleteRecord.SeriesId);
        Assert.Equal(500L, deleteRecord.FromTimestamp);
        Assert.Equal(600L, deleteRecord.ToTimestamp);
    }

    [Fact]
    public void AppendDelete_LsnIsMonotonicallyIncreasing()
    {
        string path = TempFile();
        using var writer = WalWriter.Open(path);

        long lsn1 = writer.AppendWritePoint(1UL, 1000L, "f", FieldValue.FromDouble(1.0));
        long lsn2 = writer.AppendDelete(1UL, "f", 100L, 200L);
        long lsn3 = writer.AppendWritePoint(1UL, 2000L, "f", FieldValue.FromDouble(2.0));

        Assert.True(lsn1 < lsn2);
        Assert.True(lsn2 < lsn3);
    }

    [Fact]
    public void AppendDelete_MixedWithWritePoints_AllReadBack()
    {
        string path = TempFile();
        using (var writer = WalWriter.Open(path))
        {
            writer.AppendWritePoint(1UL, 100L, "temp", FieldValue.FromDouble(25.0));
            writer.AppendDelete(1UL, "temp", 50L, 150L);
            writer.AppendWritePoint(2UL, 200L, "pressure", FieldValue.FromDouble(1013.25));
            writer.AppendDelete(2UL, "pressure", 100L, 300L);
            writer.Sync();
        }

        using var reader = WalReader.Open(path);
        var records = reader.Replay().ToList();

        var writePoints = records.OfType<WritePointRecord>().ToList();
        var deletes = records.OfType<DeleteRecord>().ToList();

        Assert.Equal(2, writePoints.Count);
        Assert.Equal(2, deletes.Count);
        Assert.Equal(1UL, deletes[0].SeriesId);
        Assert.Equal(2UL, deletes[1].SeriesId);
    }

    [Fact]
    public void WalSegmentSet_AppendDelete_ReplayWithCheckpoint_IncludesDeleteRecords()
    {
        string walDir = Path.Combine(_tempDir, "wal");
        using var walSet = WalSegmentSet.Open(walDir);

        walSet.AppendWritePoint(1UL, 1000L, "f", FieldValue.FromDouble(1.0));
        long deleteLsn = walSet.AppendDelete(1UL, "f", 100L, 200L);
        walSet.Sync();

        var catalog = new SonnetDB.Catalog.SeriesCatalog();
        var result = walSet.ReplayWithCheckpoint(catalog);

        Assert.Single(result.DeleteRecords);
        Assert.Equal(1UL, result.DeleteRecords[0].SeriesId);
        Assert.Equal("f", result.DeleteRecords[0].FieldName);
        Assert.Equal(100L, result.DeleteRecords[0].FromTimestamp);
        Assert.Equal(200L, result.DeleteRecords[0].ToTimestamp);
        Assert.Equal(deleteLsn, result.DeleteRecords[0].Lsn);
    }

    [Fact]
    public void WalSegmentSet_DeleteAfterCheckpoint_IncludedInReplay()
    {
        string walDir = Path.Combine(_tempDir, "wal");
        using var walSet = WalSegmentSet.Open(walDir);

        // Write some data and checkpoint
        walSet.AppendWritePoint(1UL, 1000L, "f", FieldValue.FromDouble(1.0));
        long cpLsn = walSet.AppendCheckpoint(1L);
        walSet.Roll();

        // Now append a delete AFTER checkpoint
        long deleteLsn = walSet.AppendDelete(1UL, "f", 100L, 200L);
        walSet.Sync();

        var catalog = new SonnetDB.Catalog.SeriesCatalog();
        var result = walSet.ReplayWithCheckpoint(catalog);

        // The checkpoint LSN should be set
        Assert.True(result.CheckpointLsn > 0);

        // The delete record (which is > checkpointLsn) should be included
        Assert.Single(result.DeleteRecords);
        Assert.Equal(deleteLsn, result.DeleteRecords[0].Lsn);
        Assert.True(result.DeleteRecords[0].Lsn > result.CheckpointLsn);
    }
}
