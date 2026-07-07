using SonnetDB.Model;
using Xunit;

namespace SonnetDB.Core.Tests.Model;

/// <summary>
/// <see cref="DataPoint"/> 单元测试。
/// </summary>
public sealed class DataPointTests
{
    // ── record struct 相等性 ────────────────────────────────────────────────

    [Fact]
    public void Equality_SameTimestampAndValue_AreEqual()
    {
        var a = new DataPoint(100L, FieldValue.FromDouble(1.0));
        var b = new DataPoint(100L, FieldValue.FromDouble(1.0));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentTimestamps_AreNotEqual()
    {
        var a = new DataPoint(100L, FieldValue.FromDouble(1.0));
        var b = new DataPoint(200L, FieldValue.FromDouble(1.0));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_DifferentValues_AreNotEqual()
    {
        var a = new DataPoint(100L, FieldValue.FromDouble(1.0));
        var b = new DataPoint(100L, FieldValue.FromDouble(2.0));
        Assert.NotEqual(a, b);
    }

    // ── CompareTo ───────────────────────────────────────────────────────────

    [Fact]
    public void CompareTo_EarlierTimestamp_ReturnsNegative()
    {
        var a = new DataPoint(100L, FieldValue.FromLong(0));
        var b = new DataPoint(200L, FieldValue.FromLong(0));
        Assert.True(a.CompareTo(b) < 0);
    }

    [Fact]
    public void CompareTo_SameTimestamp_ReturnsZero()
    {
        var a = new DataPoint(100L, FieldValue.FromLong(0));
        var b = new DataPoint(100L, FieldValue.FromLong(1));
        Assert.Equal(0, a.CompareTo(b));
    }

    [Fact]
    public void CompareTo_LaterTimestamp_ReturnsPositive()
    {
        var a = new DataPoint(200L, FieldValue.FromLong(0));
        var b = new DataPoint(100L, FieldValue.FromLong(0));
        Assert.True(a.CompareTo(b) > 0);
    }

    // ── List<DataPoint>.Sort() ──────────────────────────────────────────────

    [Fact]
    public void Sort_SortsListByTimestampAscending()
    {
        var list = new List<DataPoint>
        {
            new DataPoint(300L, FieldValue.FromLong(3)),
            new DataPoint(100L, FieldValue.FromLong(1)),
            new DataPoint(200L, FieldValue.FromLong(2)),
        };

        list.Sort();

        Assert.Equal(100L, list[0].Timestamp);
        Assert.Equal(200L, list[1].Timestamp);
        Assert.Equal(300L, list[2].Timestamp);
    }
}
