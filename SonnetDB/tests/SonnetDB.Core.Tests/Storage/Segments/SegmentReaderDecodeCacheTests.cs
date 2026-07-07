using SonnetDB.Engine;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Segments;

/// <summary>
/// <see cref="SegmentReader"/> block 解码缓存测试。
/// </summary>
public sealed class SegmentReaderDecodeCacheTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SegmentWriter _writer = new(new SegmentWriterOptions { FsyncOnCommit = false });

    public SegmentReaderDecodeCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void DecodeBlock_WithManyBlocks_EvictsWithinMemoryBudget()
    {
        string path = TempPath();
        const long BudgetBytes = 2048L;
        var memTable = BuildMultiBlockMemTable(seriesCount: 32, pointsPerSeries: 5);
        _writer.WriteFrom(memTable, 1L, path);

        using var reader = SegmentReader.Open(path, new SegmentReaderOptions
        {
            DecodeBlockCacheMaxBytes = BudgetBytes,
        });

        foreach (var block in reader.Blocks)
        {
            var decoded = reader.DecodeBlock(block);
            Assert.Equal(5, decoded.Length);
            Assert.True(
                reader.DecodeCacheCurrentBytes <= BudgetBytes,
                $"Decode cache exceeded budget: {reader.DecodeCacheCurrentBytes} > {BudgetBytes}.");
        }

        Assert.True(reader.DecodeCacheEntryCount > 0);
        Assert.True(reader.DecodeCacheEntryCount < reader.BlockCount);
        Assert.True(reader.DecodeCacheCurrentBytes <= BudgetBytes);
    }

    [Fact]
    public void Dispose_AfterDecodeBlock_ClearsDecodeCache()
    {
        string path = TempPath();
        var memTable = BuildMultiBlockMemTable(seriesCount: 2, pointsPerSeries: 5);
        _writer.WriteFrom(memTable, 1L, path);

        var reader = SegmentReader.Open(path, new SegmentReaderOptions
        {
            DecodeBlockCacheMaxBytes = 1024 * 1024,
        });
        reader.DecodeBlock(reader.Blocks[0]);

        Assert.True(reader.DecodeCacheCurrentBytes > 0);
        Assert.True(reader.DecodeCacheEntryCount > 0);

        reader.Dispose();

        Assert.Equal(0L, reader.DecodeCacheCurrentBytes);
        Assert.Equal(0, reader.DecodeCacheEntryCount);
        Assert.Throws<ObjectDisposedException>(() => reader.DecodeBlock(reader.Blocks[0]));
    }

    private string TempPath(string name = "decode-cache.SDBSEG")
        => Path.Combine(_tempDir, name);

    private static MemTable BuildMultiBlockMemTable(int seriesCount, int pointsPerSeries)
    {
        var memTable = new MemTable();
        long lsn = 1;
        for (int series = 0; series < seriesCount; series++)
        {
            ulong seriesId = (ulong)(series + 1);
            for (int point = 0; point < pointsPerSeries; point++)
            {
                memTable.Append(
                    seriesId,
                    1000L + point,
                    "v",
                    FieldValue.FromDouble(series * 100 + point),
                    lsn++);
            }
        }

        return memTable;
    }
}
