using System.Runtime.InteropServices;
using SonnetDB.Catalog;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Format;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Segments;

/// <summary>
/// <see cref="SegmentWriter"/> 集成测试：SeriesCatalog → MemTable → SegmentWriter 完整链路。
/// </summary>
public sealed class SegmentWriterIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public SegmentWriterIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string TempPath(string name = "segment.SDBSEG") =>
        Path.Combine(_tempDir, name);

    // ── 完整链路测试 ─────────────────────────────────────────────────────────

    [Fact]
    public void FullPipeline_5SeriesMixedTypes_AllInvariantsPass()
    {
        var catalog = new SeriesCatalog();
        var memTable = new MemTable();

        // 注册 5 个 series，每个写不同类型的点
        var entry1 = catalog.GetOrAdd("cpu", new Dictionary<string, string> { ["host"] = "srv1" });
        var entry2 = catalog.GetOrAdd("memory", new Dictionary<string, string> { ["host"] = "srv1" });
        var entry3 = catalog.GetOrAdd("disk", new Dictionary<string, string> { ["host"] = "srv1" });
        var entry4 = catalog.GetOrAdd("network", new Dictionary<string, string> { ["host"] = "srv1" });
        var entry5 = catalog.GetOrAdd("status", new Dictionary<string, string> { ["host"] = "srv1" });

        // entry1: 1000 Float64 points
        for (int i = 0; i < 1000; i++)
            memTable.Append(entry1.Id, 1000L + i * 10, "usage", FieldValue.FromDouble(i * 0.1), i + 1L);

        // entry2: 1000 Int64 points
        for (int i = 0; i < 1000; i++)
            memTable.Append(entry2.Id, 2000L + i * 10, "used_bytes", FieldValue.FromLong(i * 1024L), 1000L + i + 1L);

        // entry3: 1000 Boolean points
        for (int i = 0; i < 1000; i++)
            memTable.Append(entry3.Id, 3000L + i * 10, "full", FieldValue.FromBool(i % 2 == 0), 2000L + i + 1L);

        // entry4: 500 String points
        for (int i = 0; i < 500; i++)
            memTable.Append(entry4.Id, 4000L + i * 10, "state", FieldValue.FromString($"state{i}"), 3000L + i + 1L);

        // entry5: 500 Float64 points, two fields
        for (int i = 0; i < 500; i++)
        {
            memTable.Append(entry5.Id, 5000L + i * 10, "rx", FieldValue.FromDouble(i * 100.0), 3500L + i * 2L);
            memTable.Append(entry5.Id, 5000L + i * 10, "tx", FieldValue.FromDouble(i * 50.0), 3500L + i * 2L + 1L);
        }

        Assert.Equal(4500L, memTable.PointCount);

        string path = TempPath();
        var writer = new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false });
        var result = writer.WriteFrom(memTable, 1L, path);

        // BlockCount = 5 fields (1+1+1+1+2)
        Assert.Equal(6, result.BlockCount);
        Assert.True(result.TotalBytes > 0);
        Assert.True(result.MinTimestamp <= result.MaxTimestamp);

        SegmentWriterTests.AssertSegmentInvariants(path);
    }

    [Fact]
    public void FullPipeline_TotalCountMatchesMemTablePointCount()
    {
        var catalog = new SeriesCatalog();
        var memTable = new MemTable();

        var entry = catalog.GetOrAdd("sensor", new Dictionary<string, string> { ["id"] = "1" });

        const int pointCount = 5000;
        long expectedMin = long.MaxValue, expectedMax = long.MinValue;

        for (int i = 0; i < pointCount; i++)
        {
            long ts = 1000L + i * 7;
            if (ts < expectedMin) expectedMin = ts;
            if (ts > expectedMax) expectedMax = ts;
            memTable.Append(entry.Id, ts, "value", FieldValue.FromDouble(i * 0.5), i + 1L);
        }

        string path = TempPath();
        var writer = new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false });
        var result = writer.WriteFrom(memTable, 1L, path);

        Assert.Equal(1, result.BlockCount);
        Assert.Equal(expectedMin, result.MinTimestamp);
        Assert.Equal(expectedMax, result.MaxTimestamp);

        // Verify BlockHeader.Count == pointCount
        byte[] bytes = File.ReadAllBytes(path);
        var blockHeader = MemoryMarshal.Read<BlockHeader>(bytes.AsSpan(FormatSizes.SegmentHeaderSize, FormatSizes.BlockHeaderSize));
        Assert.Equal(pointCount, blockHeader.Count);

        SegmentWriterTests.AssertSegmentInvariants(path);
    }

    // ── 崩溃模拟测试 ─────────────────────────────────────────────────────────

    [Fact]
    public void CrashSimulation_DuringBlockWrite_TempFileDeleted_TargetNotCreated()
    {
        string path = TempPath("crash_target.SDBSEG");

        var memTable = new MemTable();
        for (int i = 0; i < 100; i++)
            memTable.Append(1UL, i * 100L, "v", FieldValue.FromDouble(i), i + 1L);

        // Trigger crash when we start writing the first block (offset = SegmentHeaderSize)
        var options = new SegmentWriterOptions
        {
            FsyncOnCommit = false,
            FailAt = offset =>
            {
                if (offset == FormatSizes.SegmentHeaderSize)
                    throw new InvalidOperationException("Simulated crash during block write.");
            },
        };

        var writer = new SegmentWriter(options);
        Assert.Throws<InvalidOperationException>(() => writer.WriteFrom(memTable, 1L, path));

        // Temp file must be cleaned up
        Assert.False(File.Exists(path + ".tmp"), "Temp file must be deleted after crash.");

        // Target file must not be created
        Assert.False(File.Exists(path), "Target file must not exist after crash.");
    }

    [Fact]
    public void CrashSimulation_TargetExistedBefore_OldVersionPreserved()
    {
        string path = TempPath("preserve_target.SDBSEG");

        // Write a valid first version
        var writer1 = new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false });
        var memTableV1 = new MemTable();
        memTableV1.Append(1UL, 1000L, "v", FieldValue.FromDouble(1.0), 1L);
        writer1.WriteFrom(memTableV1, 1L, path);

        long v1Size = new FileInfo(path).Length;
        Assert.True(File.Exists(path));

        // Now attempt to write a new version with a crash in the middle
        var memTableV2 = new MemTable();
        for (int i = 0; i < 200; i++)
            memTableV2.Append(2UL, i * 100L, "v", FieldValue.FromDouble(i), i + 1L);

        bool crashTriggered = false;
        var options = new SegmentWriterOptions
        {
            FsyncOnCommit = false,
            FailAt = offset =>
            {
                if (offset == FormatSizes.SegmentHeaderSize && !crashTriggered)
                {
                    crashTriggered = true;
                    throw new InvalidOperationException("Simulated crash.");
                }
            },
        };

        var writer2 = new SegmentWriter(options);
        Assert.Throws<InvalidOperationException>(() => writer2.WriteFrom(memTableV2, 2L, path));

        // Temp file must be gone
        Assert.False(File.Exists(path + ".tmp"));

        // Target file must be the original version (unchanged)
        Assert.True(File.Exists(path));
        Assert.Equal(v1Size, new FileInfo(path).Length);

        byte[] bytes = File.ReadAllBytes(path);
        var segHeader = MemoryMarshal.Read<SegmentHeader>(bytes.AsSpan(0, FormatSizes.SegmentHeaderSize));
        Assert.Equal(1L, segHeader.SegmentId);

        SegmentWriterTests.AssertSegmentInvariants(path);
    }

    [Fact]
    public void CrashSimulation_DuringSecondBlock_FirstBlockDataPreservedInOldVersion()
    {
        string path = TempPath("two_blocks.SDBSEG");

        // Write an old version with 1 block
        var writerOld = new SegmentWriter(new SegmentWriterOptions { FsyncOnCommit = false });
        var mtOld = new MemTable();
        mtOld.Append(1UL, 1000L, "v", FieldValue.FromDouble(1.0), 1L);
        writerOld.WriteFrom(mtOld, 1L, path);

        // Write new version with 2 blocks, crash during second block
        var mt = new MemTable();
        for (int i = 0; i < 10; i++)
            mt.Append(1UL, i * 100L, "v", FieldValue.FromDouble(i), i + 1L);
        for (int i = 0; i < 10; i++)
            mt.Append(2UL, i * 100L, "v", FieldValue.FromDouble(i), 100L + i);

        // Crash when we're about to write the second block
        // The first block = 64 (SegHeader) + 64 (BlockHeader) + fieldNameLen + tsLen + valLen
        // = 64 + 64 + 1 + 80 + 80 = 289 bytes approximately
        bool secondBlockTrigger = false;
        var options = new SegmentWriterOptions
        {
            FsyncOnCommit = false,
            FailAt = offset =>
            {
                if (offset > FormatSizes.SegmentHeaderSize && !secondBlockTrigger)
                {
                    secondBlockTrigger = true;
                    throw new InvalidOperationException("Crash on second block.");
                }
            },
        };

        var writer2 = new SegmentWriter(options);
        Assert.Throws<InvalidOperationException>(() => writer2.WriteFrom(mt, 2L, path));

        // Target should still be the old version
        Assert.False(File.Exists(path + ".tmp"));
        Assert.True(File.Exists(path));

        byte[] bytes = File.ReadAllBytes(path);
        var segHeader = MemoryMarshal.Read<SegmentHeader>(bytes.AsSpan(0, FormatSizes.SegmentHeaderSize));
        Assert.Equal(1L, segHeader.SegmentId);
        Assert.Equal(1, segHeader.BlockCount);

        SegmentWriterTests.AssertSegmentInvariants(path);
    }
}
