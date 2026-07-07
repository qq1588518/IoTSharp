using SonnetDB.Engine;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Engine;

/// <summary>
/// <see cref="SegmentManager.DropSegments"/> 单元测试：验证原子移除、Dispose、索引更新及并发安全。
/// </summary>
public sealed class SegmentManagerDropTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SegmentWriter _writer = new(new SegmentWriterOptions { FsyncOnCommit = false });

    public SegmentManagerDropTests()
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

    private string WriteSegment(long segId, ulong seriesId = 1UL, int count = 10)
    {
        var mt = new MemTable();
        for (int i = 0; i < count; i++)
            mt.Append(seriesId, 1000L + i, "v", FieldValue.FromDouble(i), i + 1L);
        string path = SegPath(segId);
        _writer.WriteFrom(mt, segId, path);
        return path;
    }

    // ── DropSegments 后 SegmentCount 减少 ────────────────────────────────────

    [Fact]
    public void DropSegments_Multiple_SegmentCountDecreases()
    {
        WriteSegment(1);
        WriteSegment(2);
        WriteSegment(3);

        using var mgr = SegmentManager.Open(_tempDir);
        Assert.Equal(3, mgr.SegmentCount);

        var dropped = mgr.DropSegments([1L, 2L]);

        Assert.Equal(1, mgr.SegmentCount);
        Assert.Equal(2, dropped.Count);
    }

    // ── DropSegments 返回被移除的 reader（已 Disposed）────────────────────────

    [Fact]
    public void DropSegments_ReturnedReadersAreDisposed()
    {
        WriteSegment(1);
        WriteSegment(2);

        using var mgr = SegmentManager.Open(_tempDir);
        var dropped = mgr.DropSegments([1L, 2L]);

        Assert.Equal(2, dropped.Count);
        foreach (var reader in dropped)
            Assert.Throws<ObjectDisposedException>(() => reader.DecodeBlock(reader.Blocks[0]));
    }

    // ── DropSegments 后索引不含被移除段 ──────────────────────────────────────

    [Fact]
    public void DropSegments_RemovedSegmentsNotInIndex()
    {
        WriteSegment(1, seriesId: 0xAAUL);
        WriteSegment(2, seriesId: 0xBBUL);
        WriteSegment(3, seriesId: 0xCCUL);

        using var mgr = SegmentManager.Open(_tempDir);

        mgr.DropSegments([1L, 2L]);

        var index = mgr.Index;
        Assert.Equal(1, index.SegmentCount);

        // seg1 和 seg2 的 series 应无候选
        Assert.Empty(index.LookupCandidates(0xAAUL, "v", 0, long.MaxValue));
        Assert.Empty(index.LookupCandidates(0xBBUL, "v", 0, long.MaxValue));
        // seg3 仍在索引中
        Assert.NotEmpty(index.LookupCandidates(0xCCUL, "v", 0, long.MaxValue));
    }

    // ── DropSegments 空列表 → 不变 ───────────────────────────────────────────

    [Fact]
    public void DropSegments_EmptyList_NoChange()
    {
        WriteSegment(1);
        WriteSegment(2);

        using var mgr = SegmentManager.Open(_tempDir);
        var dropped = mgr.DropSegments([]);

        Assert.Equal(2, mgr.SegmentCount);
        Assert.Empty(dropped);
    }

    // ── DropSegments 不存在的 ID → 忽略 ─────────────────────────────────────

    [Fact]
    public void DropSegments_NonExistentId_Ignored()
    {
        WriteSegment(1);

        using var mgr = SegmentManager.Open(_tempDir);
        var dropped = mgr.DropSegments([99L, 100L]);

        Assert.Equal(1, mgr.SegmentCount);
        Assert.Empty(dropped);
    }

    // ── DropSegments 在 Dispose 后抛 ────────────────────────────────────────

    [Fact]
    public void DropSegments_AfterDispose_Throws()
    {
        WriteSegment(1);
        var mgr = SegmentManager.Open(_tempDir);
        mgr.Dispose();

        Assert.Throws<ObjectDisposedException>(() => mgr.DropSegments([1L]));
    }

    // ── 并发：50 个查询线程 + 1 个 Drop 持续 2 秒 → 无异常 ─────────────────

    [Fact]
    public async Task DropSegments_ConcurrentReadsAndDrop_NoExceptions()
    {
        // 预写多个段
        for (int i = 1; i <= 10; i++)
            WriteSegment(i, seriesId: (ulong)i);

        using var mgr = SegmentManager.Open(_tempDir);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        // 50 个只读任务
        var readTasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    _ = mgr.Index.SegmentCount;
                    _ = mgr.Readers.Count;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToList();

        // 1 个 Drop 任务：每次写新段后 Drop 最旧段
        long nextSegId = 20;
        var dropTask = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var current = mgr.Readers;
                    if (current.Count > 0)
                    {
                        long removeId = current[0].Header.SegmentId;
                        mgr.DropSegments([removeId]);
                    }

                    // 补一个新段进去（短暂延迟让读线程也有机会获得 CPU 时间）
                    long segId = Interlocked.Increment(ref nextSegId);
                    WriteSegment(segId, seriesId: (ulong)segId);
                    mgr.AddSegment(SegPath(segId));

                    Thread.Sleep(30);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        });

        await Task.WhenAll([.. readTasks, dropTask]);

        Assert.Empty(exceptions);
    }
}
