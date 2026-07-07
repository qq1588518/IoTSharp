using SonnetDB.Engine;
using SonnetDB.Engine.Retention;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Engine.Retention;

/// <summary>
/// <see cref="RetentionPlanner"/> 单元测试：验证各扫描场景下的计划产出。
/// </summary>
public sealed class RetentionPlannerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SegmentWriter _writer = new(new SegmentWriterOptions { FsyncOnCommit = false });

    public RetentionPlannerTests()
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

    /// <summary>
    /// 写入包含指定时间戳范围的段文件并打开 reader。
    /// </summary>
    private SegmentReader WriteSegment(long segId, long[] timestamps, ulong seriesId = 1UL, string field = "v")
    {
        var mt = new MemTable();
        for (int i = 0; i < timestamps.Length; i++)
            mt.Append(seriesId, timestamps[i], field, FieldValue.FromDouble(i), i + 1L);
        string path = SegPath(segId);
        _writer.WriteFrom(mt, segId, path);
        return SegmentReader.Open(path, new SegmentReaderOptions { VerifyIndexCrc = false, VerifyBlockCrc = false });
    }

    private static RetentionPolicy MakePolicy(long cutoffFromNow = 1000, long nowFnValue = 10000, int maxTombstones = 1024) =>
        new RetentionPolicy
        {
            Enabled = true,
            TtlInTimestampUnits = cutoffFromNow,
            NowFn = () => nowFnValue,
            MaxTombstonesPerRound = maxTombstones,
        };

    // ── 0 段 → 空 plan ────────────────────────────────────────────────────────

    [Fact]
    public void Plan_NoSegments_ReturnsEmptyPlan()
    {
        var policy = MakePolicy();
        var plan = RetentionPlanner.Plan([], new TombstoneTable(), policy);

        Assert.Equal(0, plan.Cutoff);
        Assert.Empty(plan.SegmentsToDrop);
        Assert.Empty(plan.TombstonesToInject);
    }

    // ── disabled policy → 空 plan ─────────────────────────────────────────────

    [Fact]
    public void Plan_DisabledPolicy_ReturnsEmptyPlan()
    {
        using var reader = WriteSegment(1, [100L, 200L]);
        var policy = new RetentionPolicy { Enabled = false };
        var plan = RetentionPlanner.Plan([reader], new TombstoneTable(), policy);

        Assert.Equal(0, plan.Cutoff);
        Assert.Empty(plan.SegmentsToDrop);
        Assert.Empty(plan.TombstonesToInject);
    }

    // ── 全部段 MaxTimestamp < cutoff → 全部进 SegmentsToDrop，无墓碑 ──────────

    [Fact]
    public void Plan_AllSegmentsExpired_AllGoToDrop()
    {
        // cutoff = 10000 - 1000 = 9000；段时间戳 100..300，MaxTimestamp=300 < 9000
        using var seg1 = WriteSegment(1, [100L, 200L, 300L]);
        using var seg2 = WriteSegment(2, [400L, 500L]);

        var policy = MakePolicy(cutoffFromNow: 1000, nowFnValue: 10000);
        var plan = RetentionPlanner.Plan([seg1, seg2], new TombstoneTable(), policy);

        Assert.Equal(9000L, plan.Cutoff);
        Assert.Equal(2, plan.SegmentsToDrop.Count);
        Assert.Contains(1L, plan.SegmentsToDrop);
        Assert.Contains(2L, plan.SegmentsToDrop);
        Assert.Empty(plan.TombstonesToInject);
    }

    // ── 全部段 MinTimestamp >= cutoff → 空 plan ────────────────────────────────

    [Fact]
    public void Plan_AllSegmentsFresh_EmptyPlan()
    {
        // cutoff = 9000；段时间戳从 9500 开始，MinTimestamp=9500 >= 9000
        using var seg1 = WriteSegment(1, [9500L, 9600L]);
        using var seg2 = WriteSegment(2, [9700L, 9800L]);

        var policy = MakePolicy(cutoffFromNow: 1000, nowFnValue: 10000);
        var plan = RetentionPlanner.Plan([seg1, seg2], new TombstoneTable(), policy);

        Assert.Equal(9000L, plan.Cutoff);
        Assert.Empty(plan.SegmentsToDrop);
        Assert.Empty(plan.TombstonesToInject);
    }

    // ── 混合：1 段全过期 + 1 段部分过期 + 1 段全新鲜 ─────────────────────────

    [Fact]
    public void Plan_Mixed_DropOneInjectOneFreshIgnored()
    {
        // cutoff = 9000
        // seg1: [100..300] 全过期 → drop
        // seg2: [8000..9500] 部分过期（8000 < 9000 <= 9500）→ 注入墓碑
        // seg3: [9200..9800] 全新鲜 → 跳过
        using var seg1 = WriteSegment(1, [100L, 200L, 300L]);
        using var seg2 = WriteSegment(2, [8000L, 8500L, 9000L, 9500L]);
        using var seg3 = WriteSegment(3, [9200L, 9800L]);

        var policy = MakePolicy(cutoffFromNow: 1000, nowFnValue: 10000);
        var plan = RetentionPlanner.Plan([seg1, seg2, seg3], new TombstoneTable(), policy);

        Assert.Equal(9000L, plan.Cutoff);

        // seg1 全过期
        Assert.Single(plan.SegmentsToDrop);
        Assert.Contains(1L, plan.SegmentsToDrop);

        // seg2 部分过期：注入 (seriesId=1, field="v", From=MinValue, To=8999)
        Assert.Single(plan.TombstonesToInject);
        var inject = plan.TombstonesToInject[0];
        Assert.Equal(1UL, inject.SeriesId);
        Assert.Equal("v", inject.FieldName);
        Assert.Equal(long.MinValue, inject.FromTimestamp);
        Assert.Equal(8999L, inject.ToTimestamp);
    }

    // ── 已存在等价墓碑 → 跳过 ────────────────────────────────────────────────

    [Fact]
    public void Plan_ExistingEquivalentTombstone_Skipped()
    {
        // cutoff = 9000；seg2 部分过期
        using var seg2 = WriteSegment(2, [8000L, 8500L, 9000L, 9500L]);

        // 已有等价墓碑：From=MinValue, To=8999（覆盖 block.MinTimestamp=8000 且 To >= cutoff-1=8999）
        var tombstones = new TombstoneTable();
        tombstones.Add(new Tombstone(1UL, "v", long.MinValue, 8999L, 1L));

        var policy = MakePolicy(cutoffFromNow: 1000, nowFnValue: 10000);
        var plan = RetentionPlanner.Plan([seg2], tombstones, policy);

        // 应跳过注入（已有等价墓碑）
        Assert.Empty(plan.TombstonesToInject);
        Assert.Empty(plan.SegmentsToDrop);
    }

    // ── MaxTombstonesPerRound=2 限流：本轮仅 2 条 ─────────────────────────────

    [Fact]
    public void Plan_MaxTombstonesPerRound_Truncated()
    {
        // cutoff = 9000；3 个段各有不同 series，都部分过期
        // 每段用不同 seriesId，确保各产一条墓碑
        using var seg1 = WriteSegment(1, [8000L, 9500L], seriesId: 1UL);
        using var seg2 = WriteSegment(2, [8100L, 9600L], seriesId: 2UL);
        using var seg3 = WriteSegment(3, [8200L, 9700L], seriesId: 3UL);

        var policy = new RetentionPolicy
        {
            Enabled = true,
            TtlInTimestampUnits = 1000,
            NowFn = () => 10000,
            MaxTombstonesPerRound = 2,
        };

        var plan = RetentionPlanner.Plan([seg1, seg2, seg3], new TombstoneTable(), policy);

        // 只产出 2 条
        Assert.Equal(2, plan.TombstonesToInject.Count);
    }

    // ── 同 (SeriesId, FieldName) 在多段中只产一条墓碑 ─────────────────────────

    [Fact]
    public void Plan_SameSeriesFieldInMultipleSegments_OneTombstone()
    {
        // cutoff = 9000；同一 seriesId+field 出现在两个部分过期段中
        using var seg1 = WriteSegment(1, [8000L, 9500L], seriesId: 1UL);
        using var seg2 = WriteSegment(2, [8100L, 9600L], seriesId: 1UL); // 同 series

        var policy = MakePolicy(cutoffFromNow: 1000, nowFnValue: 10000);
        var plan = RetentionPlanner.Plan([seg1, seg2], new TombstoneTable(), policy);

        // 去重后只有一条
        Assert.Single(plan.TombstonesToInject);
    }

    // ── cutoff 计算：TtlInTimestampUnits 覆盖 Ttl ────────────────────────────

    [Fact]
    public void Plan_TtlInTimestampUnits_OverridesTtl()
    {
        using var seg = WriteSegment(1, [100L, 200L]);

        // 设置 Ttl=30天但 TtlInTimestampUnits=1000（毫秒），NowFn=10000 → cutoff=9000
        var policy = new RetentionPolicy
        {
            Enabled = true,
            Ttl = TimeSpan.FromDays(30),
            TtlInTimestampUnits = 1000L,
            NowFn = () => 10000,
        };

        var plan = RetentionPlanner.Plan([seg], new TombstoneTable(), policy);
        Assert.Equal(9000L, plan.Cutoff);
        Assert.Contains(1L, plan.SegmentsToDrop);
    }
}
