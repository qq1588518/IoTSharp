using SonnetDB.Model;
using Xunit;

namespace SonnetDB.Core.Tests.Model;

/// <summary>
/// <see cref="SeriesFieldKey"/> 单元测试。
/// </summary>
public sealed class SeriesFieldKeyTests
{
    // ── record struct 相等性 ────────────────────────────────────────────────

    [Fact]
    public void Equality_SameSeriesIdAndFieldName_AreEqual()
    {
        var a = new SeriesFieldKey(42UL, "cpu");
        var b = new SeriesFieldKey(42UL, "cpu");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentSeriesId_AreNotEqual()
    {
        var a = new SeriesFieldKey(1UL, "cpu");
        var b = new SeriesFieldKey(2UL, "cpu");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_DifferentFieldName_AreNotEqual()
    {
        var a = new SeriesFieldKey(1UL, "cpu");
        var b = new SeriesFieldKey(1UL, "mem");
        Assert.NotEqual(a, b);
    }

    // ── GetHashCode ──────────────────────────────────────────────────────────

    [Fact]
    public void GetHashCode_EqualInstances_SameHashCode()
    {
        var a = new SeriesFieldKey(99UL, "field");
        var b = new SeriesFieldKey(99UL, "field");
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // ── ToString ─────────────────────────────────────────────────────────────

    [Fact]
    public void ToString_Format_IsHexSeriesIdSlashFieldName()
    {
        var key = new SeriesFieldKey(0xABCDEF0123456789UL, "temperature");
        Assert.Equal("ABCDEF0123456789/temperature", key.ToString());
    }

    [Fact]
    public void ToString_ZeroSeriesId_IsPaddedWithZeros()
    {
        var key = new SeriesFieldKey(0UL, "v");
        Assert.Equal("0000000000000000/v", key.ToString());
    }
}
