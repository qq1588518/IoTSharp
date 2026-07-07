using SonnetDB.Model;
using Xunit;

namespace SonnetDB.Core.Tests.Model;

/// <summary>
/// <see cref="AggregateResult"/> 单元测试。
/// </summary>
public sealed class AggregateResultTests
{
    // ── 初始状态 ────────────────────────────────────────────────────────────

    [Fact]
    public void InitialState_CountIsZero()
        => Assert.Equal(0L, new AggregateResult().Count);

    [Fact]
    public void InitialState_SumIsZero()
        => Assert.Equal(0d, new AggregateResult().Sum);

    [Fact]
    public void InitialState_AvgIsZero()
        => Assert.Equal(0d, new AggregateResult().Avg);

    [Fact]
    public void InitialState_MinIsPositiveInfinity()
        => Assert.Equal(double.PositiveInfinity, new AggregateResult().Min);

    [Fact]
    public void InitialState_MaxIsNegativeInfinity()
        => Assert.Equal(double.NegativeInfinity, new AggregateResult().Max);

    // ── Add ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Add_SingleValue_SetsAllAggregates()
    {
        var agg = new AggregateResult();
        agg.Add(5.0);

        Assert.Equal(1L, agg.Count);
        Assert.Equal(5.0, agg.Sum);
        Assert.Equal(5.0, agg.Min);
        Assert.Equal(5.0, agg.Max);
        Assert.Equal(5.0, agg.Avg);
    }

    [Fact]
    public void Add_MultipleValues_CorrectAggregates()
    {
        var agg = new AggregateResult();
        agg.Add(1.0);
        agg.Add(3.0);
        agg.Add(2.0);

        Assert.Equal(3L, agg.Count);
        Assert.Equal(6.0, agg.Sum);
        Assert.Equal(1.0, agg.Min);
        Assert.Equal(3.0, agg.Max);
        Assert.Equal(2.0, agg.Avg);
    }

    // ── Merge ───────────────────────────────────────────────────────────────

    [Fact]
    public void Merge_TwoResults_EquivalentToAddingAllValues()
    {
        var a = new AggregateResult();
        a.Add(1.0);
        a.Add(4.0);

        var b = new AggregateResult();
        b.Add(2.0);
        b.Add(3.0);

        var combined = new AggregateResult();
        combined.Add(1.0);
        combined.Add(4.0);
        combined.Add(2.0);
        combined.Add(3.0);

        a.Merge(b);

        Assert.Equal(combined.Count, a.Count);
        Assert.Equal(combined.Sum, a.Sum);
        Assert.Equal(combined.Min, a.Min);
        Assert.Equal(combined.Max, a.Max);
        Assert.Equal(combined.Avg, a.Avg);
    }

    [Fact]
    public void Merge_EmptyOther_DoesNotChangeState()
    {
        var agg = new AggregateResult();
        agg.Add(5.0);

        var empty = new AggregateResult();
        agg.Merge(empty);

        Assert.Equal(1L, agg.Count);
        Assert.Equal(5.0, agg.Sum);
        Assert.Equal(5.0, agg.Min);
        Assert.Equal(5.0, agg.Max);
    }

    [Fact]
    public void Merge_NullOther_ThrowsArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => new AggregateResult().Merge(null!));

    // ── Reset ───────────────────────────────────────────────────────────────

    [Fact]
    public void Reset_AfterAdd_RestoresInitialState()
    {
        var agg = new AggregateResult();
        agg.Add(10.0);
        agg.Add(20.0);
        agg.Reset();

        Assert.Equal(0L, agg.Count);
        Assert.Equal(0d, agg.Sum);
        Assert.Equal(0d, agg.Avg);
        Assert.Equal(double.PositiveInfinity, agg.Min);
        Assert.Equal(double.NegativeInfinity, agg.Max);
    }
}
