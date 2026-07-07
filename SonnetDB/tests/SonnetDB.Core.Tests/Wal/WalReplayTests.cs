using SonnetDB.Catalog;
using SonnetDB.Model;
using SonnetDB.Wal;
using Xunit;

namespace SonnetDB.Core.Tests.Wal;

/// <summary>
/// <see cref="WalReplay"/> 单元测试。
/// </summary>
public sealed class WalReplayTests : IDisposable
{
    private readonly string _tempDir;

    public WalReplayTests()
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
    public void ReplayInto_TwoSeries_CatalogAndPoints()
    {
        string path = TempFile();
        var tags1 = new Dictionary<string, string> { ["host"] = "srv1" };
        var tags2 = new Dictionary<string, string> { ["host"] = "srv2" };

        // Pre-compute expected series IDs so the WAL can be written with correct IDs
        var precomputedCatalog = new SeriesCatalog();
        var entry1 = precomputedCatalog.GetOrAdd("cpu", tags1);
        var entry2 = precomputedCatalog.GetOrAdd("memory", tags2);

        using (var writer = WalWriter.Open(path))
        {
            writer.AppendCreateSeries(entry1.Id, "cpu", tags1);
            for (int i = 0; i < 5; i++)
                writer.AppendWritePoint(entry1.Id, 1000L + i, "usage", FieldValue.FromDouble(i));
            writer.AppendCreateSeries(entry2.Id, "memory", tags2);
            for (int i = 0; i < 3; i++)
                writer.AppendWritePoint(entry2.Id, 2000L + i, "free", FieldValue.FromLong(1000L * i));
            writer.Sync();
        }

        var catalog = new SeriesCatalog();
        var points = WalReplay.ReplayInto(path, catalog).ToList();

        Assert.Equal(2, catalog.Count);
        Assert.Equal(8, points.Count);
        Assert.All(points.Take(5), p => Assert.Equal(entry1.Id, p.SeriesId));
        Assert.All(points.Skip(5), p => Assert.Equal(entry2.Id, p.SeriesId));
    }

    [Fact]
    public void ReplayInto_MismatchedSeriesId_ThrowsInvalidDataException()
    {
        string path = TempFile();
        var tags = new Dictionary<string, string> { ["host"] = "srv1" };

        // Write with wrong SeriesId (not matching the computed value)
        using (var writer = WalWriter.Open(path))
        {
            writer.AppendCreateSeries(0xDEADBEEFDEADBEEFUL, "cpu", tags); // wrong ID
            writer.Sync();
        }

        var catalog = new SeriesCatalog();
        Assert.Throws<InvalidDataException>(() => WalReplay.ReplayInto(path, catalog).ToList());
    }
}
