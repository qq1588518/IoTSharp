using SonnetDB.Query;
using Xunit;

namespace SonnetDB.Core.Tests.Query;

/// <summary>
/// <see cref="TimeRange"/> 单元测试。
/// </summary>
public sealed class TimeRangeTests
{
    // ── 构造 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ValidRange_Succeeds()
    {
        var r = new TimeRange(100L, 200L);
        Assert.Equal(100L, r.FromInclusive);
        Assert.Equal(200L, r.ToInclusive);
    }

    [Fact]
    public void Constructor_EqualBounds_Succeeds()
    {
        var r = new TimeRange(500L, 500L);
        Assert.Equal(500L, r.FromInclusive);
        Assert.Equal(500L, r.ToInclusive);
    }

    [Fact]
    public void Constructor_FromGreaterThanTo_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeRange(200L, 100L));
    }

    // ── 工厂方法 ──────────────────────────────────────────────────────────────

    [Fact]
    public void All_CoversFullRange()
    {
        var r = TimeRange.All;
        Assert.Equal(long.MinValue, r.FromInclusive);
        Assert.Equal(long.MaxValue, r.ToInclusive);
    }

    [Fact]
    public void From_SetsFromInclusive()
    {
        var r = TimeRange.From(1000L);
        Assert.Equal(1000L, r.FromInclusive);
        Assert.Equal(long.MaxValue, r.ToInclusive);
    }

    [Fact]
    public void Until_SetsToInclusive()
    {
        var r = TimeRange.Until(2000L);
        Assert.Equal(long.MinValue, r.FromInclusive);
        Assert.Equal(2000L, r.ToInclusive);
    }

    // ── Contains ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(100L, 200L, 100L, true)]   // 起点
    [InlineData(100L, 200L, 200L, true)]   // 终点
    [InlineData(100L, 200L, 150L, true)]   // 中间
    [InlineData(100L, 200L, 99L, false)]   // 小于起点
    [InlineData(100L, 200L, 201L, false)]  // 大于终点
    public void Contains_VariousTimestamps(long from, long to, long ts, bool expected)
    {
        var r = new TimeRange(from, to);
        Assert.Equal(expected, r.Contains(ts));
    }

    [Fact]
    public void Contains_BoundaryValues()
    {
        var r = TimeRange.All;
        Assert.True(r.Contains(long.MinValue));
        Assert.True(r.Contains(0L));
        Assert.True(r.Contains(long.MaxValue));
    }

    // ── Overlaps ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(100L, 200L, 150L, 250L, true)]   // 右侧交叉
    [InlineData(100L, 200L, 50L, 150L, true)]    // 左侧交叉
    [InlineData(100L, 200L, 100L, 200L, true)]   // 完全重叠
    [InlineData(100L, 200L, 120L, 180L, true)]   // 包含于内
    [InlineData(100L, 200L, 50L, 250L, true)]    // 完全包含外部
    [InlineData(100L, 200L, 201L, 300L, false)]  // 紧邻右侧不重叠
    [InlineData(100L, 200L, 0L, 99L, false)]     // 紧邻左侧不重叠
    [InlineData(100L, 200L, 200L, 300L, true)]   // 恰好在终点
    [InlineData(100L, 200L, 0L, 100L, true)]     // 恰好在起点
    public void Overlaps_VariousRanges(long from, long to, long min, long max, bool expected)
    {
        var r = new TimeRange(from, to);
        Assert.Equal(expected, r.Overlaps(min, max));
    }

    // ── 相等性 ────────────────────────────────────────────────────────────────

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var a = new TimeRange(100L, 200L);
        var b = new TimeRange(100L, 200L);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void Equality_DifferentValues_NotEqual()
    {
        var a = new TimeRange(100L, 200L);
        var b = new TimeRange(100L, 300L);
        Assert.NotEqual(a, b);
        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    public void GetHashCode_SameValues_SameHash()
    {
        var a = new TimeRange(100L, 200L);
        var b = new TimeRange(100L, 200L);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // ── ToString ──────────────────────────────────────────────────────────────

    [Fact]
    public void ToString_ReturnsExpectedFormat()
    {
        var r = new TimeRange(100L, 200L);
        Assert.Equal("[100, 200]", r.ToString());
    }

    [Fact]
    public void ToString_All_ReturnsMinMaxFormat()
    {
        var r = TimeRange.All;
        Assert.Equal($"[{long.MinValue}, {long.MaxValue}]", r.ToString());
    }
}
