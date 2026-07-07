using SonnetDB.Engine;
using SonnetDB.Engine.Compaction;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Engine.Compaction;

/// <summary>
/// <see cref="CompactionPlanner"/> 单元测试：验证 Size-Tiered 策略的 tier 划分与计划产出。
/// </summary>
public sealed class CompactionPlannerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SegmentWriter _writer = new(new SegmentWriterOptions { FsyncOnCommit = false });

    public CompactionPlannerTests()
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

    /// <summary>写入一个空段（仅 SegmentHeader + Footer，128 字节）。</summary>
    private SegmentReader WriteEmpty(long segId)
    {
        string path = SegPath(segId);
        _writer.WriteFrom(new MemTable(), segId, path);
        return SegmentReader.Open(path, new SegmentReaderOptions { VerifyIndexCrc = false, VerifyBlockCrc = false });
    }

    /// <summary>写入有 N 个点的段（通过控制点数来撑大文件）。</summary>
    private SegmentReader WriteWithPoints(long segId, int pointCount, ulong seriesId = 0x1UL)
    {
        var mt = new MemTable();
        for (int i = 0; i < pointCount; i++)
            mt.Append(seriesId, 1000L + i, "val", FieldValue.FromDouble(i), i + 1L);

        string path = SegPath(segId);
        _writer.WriteFrom(mt, segId, path);
        return SegmentReader.Open(path, new SegmentReaderOptions { VerifyIndexCrc = false, VerifyBlockCrc = false });
    }

    private static CompactionPolicy MakeTinyPolicy(int minTierSize = 4)
        => new CompactionPolicy
        {
            Enabled = true,
            MinTierSize = minTierSize,
            TierSizeRatio = 4,
            FirstTierMaxBytes = 512, // 很小，便于用少量点控制 tier
        };

    // ── 边界：无 reader ──────────────────────────────────────────────────────

    [Fact]
    public void Plan_ZeroReaders_NoPlans()
    {
        var plans = CompactionPlanner.Plan([], MakeTinyPolicy());
        Assert.Empty(plans);
    }

    [Fact]
    public void Plan_OneReader_NoPlans()
    {
        using var r1 = WriteEmpty(1);
        var plans = CompactionPlanner.Plan([r1], MakeTinyPolicy());
        Assert.Empty(plans);
    }

    [Fact]
    public void Plan_ThreeReadersSameTier_NoPlans()
    {
        using var r1 = WriteEmpty(1);
        using var r2 = WriteEmpty(2);
        using var r3 = WriteEmpty(3);
        var plans = CompactionPlanner.Plan([r1, r2, r3], MakeTinyPolicy());
        Assert.Empty(plans);
    }

    // ── 4 段同 tier → 1 个 Plan ──────────────────────────────────────────────

    [Fact]
    public void Plan_FourReadersSameTier_OnePlanWithFourIds()
    {
        // 写 4 个空段（128 字节 < 512，都在 tier 0）
        using var r1 = WriteEmpty(1);
        using var r2 = WriteEmpty(2);
        using var r3 = WriteEmpty(3);
        using var r4 = WriteEmpty(4);

        var plans = CompactionPlanner.Plan([r1, r2, r3, r4], MakeTinyPolicy());

        Assert.Single(plans);
        Assert.Equal(4, plans[0].SourceSegmentIds.Count);
        Assert.Equal([1L, 2L, 3L, 4L], plans[0].SourceSegmentIds);
    }

    // ── 8 段同 tier → 2 个 Plan ──────────────────────────────────────────────

    [Fact]
    public void Plan_EightReadersSameTier_TwoPlans()
    {
        var readers = Enumerable.Range(1, 8)
            .Select(i => WriteEmpty(i))
            .ToArray();

        try
        {
            var plans = CompactionPlanner.Plan(readers, MakeTinyPolicy());
            Assert.Equal(2, plans.Count);
            Assert.Equal(4, plans[0].SourceSegmentIds.Count);
            Assert.Equal(4, plans[1].SourceSegmentIds.Count);
        }
        finally
        {
            foreach (var r in readers) r.Dispose();
        }
    }

    // ── 段大小跨 tier → 不跨 tier 合并 ─────────────────────────────────────

    [Fact]
    public void Plan_ReadersAcrossDifferentTiers_DoNotMixTiers()
    {
        // tier 0: 空段 (128 B < 512 B)；tier 1: 有足够点的段（> 512 B）
        // 写 4 个 tier-0 段 + 4 个 tier-1 段
        var tier0 = Enumerable.Range(1, 4).Select(i => WriteEmpty(i)).ToArray();
        var tier1 = Enumerable.Range(5, 4).Select(i => WriteWithPoints(i, 30)).ToArray();
        var all = (SegmentReader[])[.. tier0, .. tier1];

        // 确认 tier-1 的段确实比 firstTierMaxBytes 大
        Assert.All(tier1, r => Assert.True(r.FileLength > 512,
            $"期望 FileLength > 512，实际 {r.FileLength}"));

        try
        {
            var plans = CompactionPlanner.Plan(all, MakeTinyPolicy());
            Assert.Equal(2, plans.Count);

            var tier0Ids = new HashSet<long> { 1, 2, 3, 4 };
            var tier1Ids = new HashSet<long> { 5, 6, 7, 8 };
            foreach (var plan in plans)
            {
                bool allTier0 = plan.SourceSegmentIds.All(id => tier0Ids.Contains(id));
                bool allTier1 = plan.SourceSegmentIds.All(id => tier1Ids.Contains(id));
                Assert.True(allTier0 || allTier1, "Plan 混入了不同 tier 的段");
            }
        }
        finally
        {
            foreach (var r in all) r.Dispose();
        }
    }

    // ── Enabled=false → 无 Plan ──────────────────────────────────────────────

    [Fact]
    public void Plan_Disabled_NoPlans()
    {
        var readers = Enumerable.Range(1, 8).Select(i => WriteEmpty(i)).ToArray();
        try
        {
            var policy = new CompactionPolicy { Enabled = false };
            var plans = CompactionPlanner.Plan(readers, policy);
            Assert.Empty(plans);
        }
        finally
        {
            foreach (var r in readers) r.Dispose();
        }
    }
}
