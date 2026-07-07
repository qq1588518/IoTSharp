using SonnetDB.Model;
using Xunit;

namespace SonnetDB.Core.Tests.Model;

/// <summary>
/// <see cref="Point"/> 单元测试。
/// </summary>
public sealed class PointTests
{
    // ── Create 成功路径 ─────────────────────────────────────────────────────

    [Fact]
    public void Create_WithAllParameters_ReturnsPoint()
    {
        var tags = new Dictionary<string, string> { ["host"] = "server1" };
        var fields = new Dictionary<string, FieldValue> { ["cpu"] = FieldValue.FromDouble(0.8) };
        var point = Point.Create("metrics", 1000L, tags, fields);

        Assert.Equal("metrics", point.Measurement);
        Assert.Equal(1000L, point.Timestamp);
        Assert.Equal("server1", point.Tags["host"]);
        Assert.Equal(FieldValue.FromDouble(0.8), point.Fields["cpu"]);
    }

    [Fact]
    public void Create_WithNullTags_UsesEmptyDictionary()
    {
        var fields = new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromLong(1L) };
        var point = Point.Create("m", 0L, null, fields);
        Assert.Empty(point.Tags);
        // 同一实例（内部单例）被复用
        var point2 = Point.Create("m2", 0L, null, fields);
        Assert.Same(point.Tags, point2.Tags);
    }

    [Fact]
    public void Create_WithNullFields_ThrowsArgumentException()
    {
        // fields=null 且未提供任何 field → Count<1
        Assert.Throws<ArgumentException>(() => Point.Create("m", 0L, null, null));
    }

    [Fact]
    public void Create_WithZeroTimestamp_Succeeds()
    {
        var fields = new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromLong(0L) };
        var point = Point.Create("m", 0L, null, fields);
        Assert.Equal(0L, point.Timestamp);
    }

    // ── 校验失败路径 ────────────────────────────────────────────────────────

    [Fact]
    public void Create_EmptyMeasurement_ThrowsArgumentException()
    {
        var fields = new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromLong(1L) };
        Assert.Throws<ArgumentException>(() => Point.Create("", 0L, null, fields));
    }

    [Fact]
    public void Create_WhitespaceMeasurement_ThrowsArgumentException()
    {
        var fields = new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromLong(1L) };
        Assert.Throws<ArgumentException>(() => Point.Create("   ", 0L, null, fields));
    }

    [Fact]
    public void Create_MeasurementTooLong_ThrowsArgumentException()
    {
        var fields = new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromLong(1L) };
        Assert.Throws<ArgumentException>(() => Point.Create(new string('a', 256), 0L, null, fields));
    }

    [Theory]
    [InlineData(",")]
    [InlineData("=")]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\t")]
    [InlineData("\"")]
    public void Create_MeasurementWithReservedChar_ThrowsArgumentException(string reserved)
    {
        var fields = new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromLong(1L) };
        Assert.Throws<ArgumentException>(() => Point.Create("m" + reserved, 0L, null, fields));
    }

    [Fact]
    public void Create_EmptyTagKey_ThrowsArgumentException()
    {
        var tags = new Dictionary<string, string> { [""] = "v" };
        var fields = new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromLong(1L) };
        Assert.Throws<ArgumentException>(() => Point.Create("m", 0L, tags, fields));
    }

    [Fact]
    public void Create_EmptyTagValue_ThrowsArgumentException()
    {
        var tags = new Dictionary<string, string> { ["k"] = "" };
        var fields = new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromLong(1L) };
        Assert.Throws<ArgumentException>(() => Point.Create("m", 0L, tags, fields));
    }

    [Theory]
    [InlineData(",")]
    [InlineData("=")]
    [InlineData("\n")]
    public void Create_TagKeyWithReservedChar_ThrowsArgumentException(string reserved)
    {
        var tags = new Dictionary<string, string> { ["k" + reserved] = "v" };
        var fields = new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromLong(1L) };
        Assert.Throws<ArgumentException>(() => Point.Create("m", 0L, tags, fields));
    }

    [Fact]
    public void Create_EmptyFieldKey_ThrowsArgumentException()
    {
        var fields = new Dictionary<string, FieldValue> { [""] = FieldValue.FromLong(1L) };
        Assert.Throws<ArgumentException>(() => Point.Create("m", 0L, null, fields));
    }

    [Fact]
    public void Create_ZeroFields_ThrowsArgumentException()
    {
        var fields = new Dictionary<string, FieldValue>();
        Assert.Throws<ArgumentException>(() => Point.Create("m", 0L, null, fields));
    }

    [Fact]
    public void Create_NegativeTimestamp_ThrowsArgumentException()
    {
        var fields = new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromLong(1L) };
        Assert.Throws<ArgumentException>(() => Point.Create("m", -1L, null, fields));
    }

    // ── ToString ─────────────────────────────────────────────────────────────

    [Fact]
    public void ToString_ContainsMeasurementTagFieldTimestamp()
    {
        var tags = new Dictionary<string, string> { ["host"] = "srv" };
        var fields = new Dictionary<string, FieldValue> { ["cpu"] = FieldValue.FromDouble(0.5) };
        var point = Point.Create("metrics", 12345L, tags, fields);
        var s = point.ToString();

        Assert.Contains("metrics", s);
        Assert.Contains("host", s);
        Assert.Contains("srv", s);
        Assert.Contains("cpu", s);
        Assert.Contains("12345", s);
    }
}
