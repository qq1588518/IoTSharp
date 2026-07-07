using SonnetDB.Engine;
using SonnetDB.Model;
using SonnetDB.Wal;
using Xunit;

namespace SonnetDB.Core.Tests.Engine;

/// <summary>
/// <see cref="WalTruncator"/> 单元测试。
/// </summary>
public sealed class WalTruncatorTests : IDisposable
{
    private readonly string _tempDir;

    public WalTruncatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string WalPath() => Path.Combine(_tempDir, "active.SDBWAL");

#pragma warning disable CS0618 // 测试已废弃的 WalTruncator.SwapAndTruncate 接口，保留以验证向后兼容行为
    [Fact]
    public void SwapAndTruncate_DeletesOldWal_CreatesNewWal()
    {
        string walPath = WalPath();

        // 写入几条记录
        using (var writer = WalWriter.Open(walPath))
        {
            writer.AppendWritePoint(1UL, 1000L, "f", FieldValue.FromDouble(1.0));
            writer.AppendWritePoint(1UL, 2000L, "f", FieldValue.FromDouble(2.0));
            writer.Sync();

            // 执行 SwapAndTruncate
            long nextLsn = writer.NextLsn;
            var newWriter = WalTruncator.SwapAndTruncate(writer, walPath, nextLsn, 64 * 1024);
            try
            {
                // 旧 WAL 不存在（已删除），新 WAL 已创建
                Assert.True(File.Exists(walPath));

                // 确认归档文件已被删除
                string archivePattern = $"{Path.GetFileName(walPath)}.archived-{nextLsn:X16}";
                string archivePath = Path.Combine(_tempDir, archivePattern);
                Assert.False(File.Exists(archivePath));

                // 新 WAL 回放应返回空集
                using var reader = WalReader.Open(walPath);
                var records = reader.Replay().ToList();
                Assert.Empty(records);

                // 新 WAL 的 FirstLsn 应等于 nextLsn
                Assert.Equal(nextLsn, reader.FirstLsn);
            }
            finally
            {
                newWriter.Dispose();
            }
        }
    }

    [Fact]
    public void SwapAndTruncate_KeepArchive_PreservesArchiveFile()
    {
        string walPath = WalPath();

        using (var writer = WalWriter.Open(walPath))
        {
            writer.AppendWritePoint(1UL, 1000L, "f", FieldValue.FromDouble(1.0));
            writer.Sync();

            long nextLsn = writer.NextLsn;
            var newWriter = WalTruncator.SwapAndTruncate(writer, walPath, nextLsn, 64 * 1024, keepArchive: true);
            try
            {
                // 归档文件应该存在
                string archiveName = $"{Path.GetFileName(walPath)}.archived-{nextLsn:X16}";
                string archivePath = Path.Combine(_tempDir, archiveName);
                Assert.True(File.Exists(archivePath));
            }
            finally
            {
                newWriter.Dispose();
            }
        }
    }

    [Fact]
    public void SwapAndTruncate_NewWal_HasCorrectFirstLsn()
    {
        string walPath = WalPath();

        // 写 5 条记录
        long nextLsn;
        using (var writer = WalWriter.Open(walPath, startLsn: 10))
        {
            for (int i = 0; i < 5; i++)
                writer.AppendWritePoint(1UL, 1000L + i, "f", FieldValue.FromDouble(i));
            nextLsn = writer.NextLsn;

            var newWriter = WalTruncator.SwapAndTruncate(writer, walPath, nextLsn, 64 * 1024);
            try
            {
                // 新 WAL 应从 nextLsn 开始
                Assert.Equal(nextLsn, newWriter.NextLsn);

                using var reader = WalReader.Open(walPath);
                Assert.Equal(nextLsn, reader.FirstLsn);
            }
            finally
            {
                newWriter.Dispose();
            }
        }
    }

    [Fact]
    public void SwapAndTruncate_NewWalReplay_ReturnsEmpty()
    {
        string walPath = WalPath();

        using (var writer = WalWriter.Open(walPath))
        {
            writer.AppendWritePoint(1UL, 1000L, "f", FieldValue.FromDouble(42.0));
            writer.AppendCheckpoint(1L);

            long nextLsn = writer.NextLsn;
            var newWriter = WalTruncator.SwapAndTruncate(writer, walPath, nextLsn, 64 * 1024);
            try
            {
                using var reader = WalReader.Open(walPath);
                var records = reader.Replay().ToList();
                Assert.Empty(records);
            }
            finally
            {
                newWriter.Dispose();
            }
        }
    }

    [Fact]
    public void SwapAndTruncate_WritesToNewWal_Succeeds()
    {
        string walPath = WalPath();

        using (var writer = WalWriter.Open(walPath))
        {
            writer.AppendWritePoint(1UL, 1000L, "f", FieldValue.FromDouble(1.0));
            long nextLsn = writer.NextLsn;

            var newWriter = WalTruncator.SwapAndTruncate(writer, walPath, nextLsn, 64 * 1024);
            try
            {
                // 向新 WAL 写入应该成功
                long lsn = newWriter.AppendWritePoint(2UL, 2000L, "g", FieldValue.FromDouble(2.0));
                Assert.Equal(nextLsn, lsn);
                newWriter.Sync();
            }
            finally
            {
                newWriter.Dispose();
            }
        }
    }

    [Fact]
    public void SwapAndTruncate_NullCurrentWriter_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            WalTruncator.SwapAndTruncate(null!, WalPath(), 1L, 64 * 1024));
    }

    [Fact]
    public void SwapAndTruncate_NullActiveWalPath_ThrowsArgumentNull()
    {
        string walPath = WalPath();
        using var writer = WalWriter.Open(walPath);
        Assert.Throws<ArgumentNullException>(() =>
            WalTruncator.SwapAndTruncate(writer, null!, 1L, 64 * 1024));
    }
#pragma warning restore CS0618
}
