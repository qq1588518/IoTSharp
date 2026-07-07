using SonnetDB.Model;
using SonnetDB.Wal;
using Xunit;

namespace SonnetDB.Core.Tests.Wal;

/// <summary>
/// WAL 集成测试，模拟崩溃恢复场景。
/// </summary>
public sealed class WalIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public WalIntegrationTests()
    {
        _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string TempFile() => System.IO.Path.Combine(_tempDir, System.IO.Path.GetRandomFileName() + ".SDBWAL");

    [Fact]
    public void CrashRecovery_50RecordsSynced_AllReadable()
    {
        string path = TempFile();

        // Step 1: Write 50 records, sync
        using (var writer = WalWriter.Open(path))
        {
            for (int i = 0; i < 50; i++)
                writer.AppendWritePoint(1UL, 1000L + i, "v", FieldValue.FromDouble(i));
            writer.Sync();
        }

        // Step 2: Replay - should read all 50 records
        var records1 = new List<WalRecord>();
        using (var reader = WalReader.Open(path))
            records1.AddRange(reader.Replay());

        Assert.Equal(50, records1.Count);

        // Step 3: Open for append and write 20 more
        using (var writer2 = WalWriter.Open(path))
        {
            Assert.Equal(51L, writer2.NextLsn); // continues from 51
            for (int i = 0; i < 20; i++)
                writer2.AppendWritePoint(1UL, 2000L + i, "v", FieldValue.FromDouble(100 + i));
            writer2.Sync();
        }

        // Step 4: Replay again - should read 70 records total
        var records2 = new List<WalRecord>();
        using (var reader = WalReader.Open(path))
            records2.AddRange(reader.Replay());

        Assert.Equal(70, records2.Count);

        // Verify LSNs are monotonically increasing and continuous
        for (int i = 0; i < records2.Count; i++)
        {
            Assert.Equal(i + 1L, records2[i].Lsn);
        }
    }
}
