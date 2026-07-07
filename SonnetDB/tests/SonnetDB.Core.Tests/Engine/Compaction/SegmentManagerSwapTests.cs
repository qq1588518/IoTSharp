using SonnetDB.Engine;
using SonnetDB.Engine.Compaction;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Engine.Compaction;

/// <summary>
/// <see cref="SegmentManager.SwapSegments"/> 单元测试：验证原子移除 + 加入段、Dispose 旧 reader、并发安全。
/// </summary>
public sealed class SegmentManagerSwapTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SegmentWriter _writer = new(new SegmentWriterOptions { FsyncOnCommit = false });

    public SegmentManagerSwapTests()
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

    // ── SwapSegments 后 SegmentCount 正确 ────────────────────────────────────

    [Fact]
    public void SwapSegments_RemoveTwoAddOne_SegmentCountOne()
    {
        // 写 seg1, seg2
        WriteSegment(1);
        WriteSegment(2);

        using var mgr = SegmentManager.Open(_tempDir);
        Assert.Equal(2, mgr.SegmentCount);

        // 写 seg3（合并结果）
        WriteSegment(3);

        var newReader = mgr.SwapSegments(new long[] { 1, 2 }, SegPath(3));

        Assert.Equal(1, mgr.SegmentCount);
        Assert.Single(mgr.Readers);
        Assert.Equal(3L, newReader.Header.SegmentId);
    }

    // ── SwapSegments 后旧 reader 已 Dispose ──────────────────────────────────

    [Fact]
    public void SwapSegments_OldReadersAreDisposed()
    {
        WriteSegment(1);
        WriteSegment(2);

        using var mgr = SegmentManager.Open(_tempDir);
        var oldReaders = mgr.Readers.ToList();

        WriteSegment(3);
        mgr.SwapSegments(new long[] { 1, 2 }, SegPath(3));

        // 访问 Disposed 的 reader 会抛异常（DecodeBlock 时）
        foreach (var old in oldReaders)
        {
            // SegmentReader.Dispose() 将 _bytes = null，调用 ReadBlock 时会抛 ObjectDisposedException
            Assert.Throws<ObjectDisposedException>(() => old.DecodeBlock(old.Blocks[0]));
        }
    }

    // ── SwapSegments 后索引更新 ───────────────────────────────────────────────

    [Fact]
    public void SwapSegments_IndexIsRebuildWithNewSegment()
    {
        WriteSegment(1, seriesId: 0xA1UL);
        WriteSegment(2, seriesId: 0xA2UL);

        using var mgr = SegmentManager.Open(_tempDir);

        // 新段包含两个 series
        var mt = new MemTable();
        for (int i = 0; i < 5; i++)
        {
            mt.Append(0xA1UL, 1000L + i, "v", FieldValue.FromDouble(i), i + 1L);
            mt.Append(0xA2UL, 1000L + i, "v", FieldValue.FromDouble(i), i + 100L);
        }
        _writer.WriteFrom(mt, 3L, SegPath(3));

        mgr.SwapSegments(new long[] { 1, 2 }, SegPath(3));

        var index = mgr.Index;
        Assert.Equal(1, index.SegmentCount);

        var refs = index.LookupCandidates(0xA1UL, "v", 0, long.MaxValue);
        Assert.NotEmpty(refs);
    }

    // ── 并发：50 查询线程 + 1 Swap 持续 2 秒 → 无异常 ─────────────────────

    [Fact]
    public async Task SwapSegments_ConcurrentReadsAndSwap_NoExceptions()
    {
        // 预写 3 个段
        WriteSegment(1, seriesId: 1UL);
        WriteSegment(2, seriesId: 1UL);
        WriteSegment(3, seriesId: 1UL);

        using var mgr = SegmentManager.Open(_tempDir);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        // 50 个只读任务：持续读取 Index 快照
        var readTasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var idx = mgr.Index;
                    _ = idx.SegmentCount;
                    var snap = mgr.Readers;
                    _ = snap.Count;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToList();

        // 1 个 Swap 任务
        long nextSegId = 10;
        var swapTask = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    long segId = Interlocked.Increment(ref nextSegId);
                    WriteSegment(segId, seriesId: 1UL);
                    string addedPath = SegPath(segId);

                    var current = mgr.Readers;
                    if (current.Count > 0)
                    {
                        long removeId = current[0].Header.SegmentId;
                        mgr.SwapSegments(new long[] { removeId }, addedPath);
                    }
                    Thread.Sleep(50);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        });

        await Task.WhenAll([.. readTasks, swapTask]);

        Assert.Empty(exceptions);
    }
}
