using SonnetDB.Model;
using Xunit;

namespace SonnetDB.Core.Tests.Model;

/// <summary>
/// <see cref="SeriesKey"/> 与 <see cref="SeriesId"/> 单元测试。
/// </summary>
public sealed class SeriesKeyTests
{
    // ── 构造与规范化 ─────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NoTags_CanonicalEqualsMeasurement()
    {
        var key = new SeriesKey("cpu");
        Assert.Equal("cpu", key.Canonical);
        Assert.Equal("cpu", key.Measurement);
        Assert.Empty(key.Tags);
    }

    [Fact]
    public void Constructor_NullTags_TreatedAsEmpty()
    {
        var key = new SeriesKey("cpu", null);
        Assert.Equal("cpu", key.Canonical);
        Assert.Empty(key.Tags);
    }

    [Fact]
    public void Constructor_WithTags_CanonicalContainsMeasurementAndTags()
    {
        var tags = new Dictionary<string, string>
        {
            ["host"] = "server1",
            ["region"] = "us-east",
        };
        var key = new SeriesKey("metrics", tags);
        Assert.Equal("metrics,host=server1,region=us-east", key.Canonical);
    }

    [Fact]
    public void Constructor_InvalidMeasurement_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new SeriesKey(""));
        Assert.Throws<ArgumentException>(() => new SeriesKey("   "));
        Assert.Throws<ArgumentException>(() => new SeriesKey("a,b"));
    }

    // ── 标签排序 ─────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_TagsOutOfOrder_CanonicalIsOrderedByOrdinalKey()
    {
        var unordered = new Dictionary<string, string>
        {
            ["zone"] = "z1",
            ["host"] = "h1",
            ["app"] = "web",
        };
        var key = new SeriesKey("svc", unordered);
        Assert.Equal("svc,app=web,host=h1,zone=z1", key.Canonical);
    }

    [Fact]
    public void Constructor_UnorderedAndOrderedTags_ProduceSameCanonical()
    {
        var ordered = new Dictionary<string, string>
        {
            ["app"] = "web",
            ["host"] = "h1",
            ["zone"] = "z1",
        };
        var unordered = new Dictionary<string, string>
        {
            ["zone"] = "z1",
            ["app"] = "web",
            ["host"] = "h1",
        };
        var a = new SeriesKey("svc", ordered);
        var b = new SeriesKey("svc", unordered);
        Assert.Equal(a.Canonical, b.Canonical);
    }

    [Fact]
    public void Constructor_OrdinalSorting_CaseSensitive()
    {
        // 'Z' (0x5A) < 'a' (0x61) in Ordinal
        var tags = new Dictionary<string, string>
        {
            ["abc"] = "1",
            ["Abc"] = "2",
            ["ABc"] = "3",
        };
        var key = new SeriesKey("m", tags);
        // Ordinal order: "ABc" < "Abc" < "abc"
        Assert.Equal("m,ABc=3,Abc=2,abc=1", key.Canonical);
    }

    // ── FromPoint ────────────────────────────────────────────────────────────

    [Fact]
    public void FromPoint_ExtractsMeasurementAndTags()
    {
        var tags = new Dictionary<string, string> { ["host"] = "srv" };
        var fields = new Dictionary<string, FieldValue> { ["cpu"] = FieldValue.FromDouble(0.5) };
        var point = Point.Create("metrics", 1000L, tags, fields);

        var key = SeriesKey.FromPoint(point);
        Assert.Equal("metrics", key.Measurement);
        Assert.Equal("metrics,host=srv", key.Canonical);
    }

    [Fact]
    public void FromPoint_NullPoint_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SeriesKey.FromPoint(null!));
    }

    // ── 相等性 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Equality_SameMeasurementAndTags_AreEqual()
    {
        var a = new SeriesKey("cpu", new Dictionary<string, string> { ["host"] = "srv" });
        var b = new SeriesKey("cpu", new Dictionary<string, string> { ["host"] = "srv" });
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void Equality_DifferentMeasurement_AreNotEqual()
    {
        var a = new SeriesKey("cpu");
        var b = new SeriesKey("mem");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_DifferentTagValues_AreNotEqual()
    {
        var a = new SeriesKey("cpu", new Dictionary<string, string> { ["host"] = "srv1" });
        var b = new SeriesKey("cpu", new Dictionary<string, string> { ["host"] = "srv2" });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_UnorderedTagsProduceSameHash()
    {
        var a = new SeriesKey("cpu", new Dictionary<string, string>
        {
            ["host"] = "srv",
            ["region"] = "eu",
        });
        var b = new SeriesKey("cpu", new Dictionary<string, string>
        {
            ["region"] = "eu",
            ["host"] = "srv",
        });
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // ── ToString ─────────────────────────────────────────────────────────────

    [Fact]
    public void ToString_ReturnsCanonical()
    {
        var key = new SeriesKey("cpu", new Dictionary<string, string> { ["host"] = "srv" });
        Assert.Equal(key.Canonical, key.ToString());
    }

    // ── SeriesId 计算 ────────────────────────────────────────────────────────

    [Fact]
    public void SeriesId_Compute_IsDeterministic()
    {
        var key = new SeriesKey("cpu", new Dictionary<string, string> { ["host"] = "srv" });
        ulong id1 = SeriesId.Compute(key);
        ulong id2 = SeriesId.Compute(key);
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void SeriesId_Compute_UnorderedTagsProduceSameId()
    {
        var a = new SeriesKey("cpu", new Dictionary<string, string>
        {
            ["host"] = "srv",
            ["zone"] = "z1",
        });
        var b = new SeriesKey("cpu", new Dictionary<string, string>
        {
            ["zone"] = "z1",
            ["host"] = "srv",
        });
        Assert.Equal(SeriesId.Compute(a), SeriesId.Compute(b));
    }

    [Fact]
    public void SeriesId_Compute_DifferentMeasurements_ProduceDifferentIds()
    {
        var a = new SeriesKey("cpu");
        var b = new SeriesKey("mem");
        Assert.NotEqual(SeriesId.Compute(a), SeriesId.Compute(b));
    }

    [Fact]
    public void SeriesId_Compute_DifferentTagCombinations_ProduceDifferentIds()
    {
        var a = new SeriesKey("cpu", new Dictionary<string, string> { ["host"] = "srv1" });
        var b = new SeriesKey("cpu", new Dictionary<string, string> { ["host"] = "srv2" });
        Assert.NotEqual(SeriesId.Compute(a), SeriesId.Compute(b));
    }

    [Fact]
    public void SeriesId_Compute_EmptyTags_DoesNotCollideWithTagged()
    {
        var noTags = new SeriesKey("cpu");
        var withTags = new SeriesKey("cpu", new Dictionary<string, string> { ["host"] = "srv" });
        Assert.NotEqual(SeriesId.Compute(noTags), SeriesId.Compute(withTags));
    }

    [Fact]
    public void SeriesId_ComputeFromCanonical_MatchesComputeFromKey()
    {
        var key = new SeriesKey("metrics", new Dictionary<string, string>
        {
            ["app"] = "web",
            ["host"] = "h1",
        });
        ulong fromKey = SeriesId.Compute(key);
        ulong fromCanonical = SeriesId.ComputeFromCanonical(key.Canonical);
        Assert.Equal(fromKey, fromCanonical);
    }

    [Fact]
    public void SeriesId_ComputeFromCanonical_NullThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SeriesId.ComputeFromCanonical(null!));
    }

    // ── 边界条件 ─────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_SingleTag_CanonicalIsCorrect()
    {
        var key = new SeriesKey("temp", new Dictionary<string, string> { ["sensor"] = "A" });
        Assert.Equal("temp,sensor=A", key.Canonical);
    }

    [Fact]
    public void Constructor_ManyTags_AllAppearInOrder()
    {
        var tags = new Dictionary<string, string>
        {
            ["z"] = "26",
            ["a"] = "1",
            ["m"] = "13",
            ["b"] = "2",
        };
        var key = new SeriesKey("data", tags);
        Assert.Equal("data,a=1,b=2,m=13,z=26", key.Canonical);
    }

    [Fact]
    public void Constructor_MaxLengthMeasurement_Succeeds()
    {
        var measurement = new string('x', 255);
        var key = new SeriesKey(measurement);
        Assert.Equal(measurement, key.Measurement);
    }
}
