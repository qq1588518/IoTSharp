using SonnetDB.Model;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Model;

/// <summary>
/// <see cref="FieldValue"/> 单元测试。
/// </summary>
public sealed class FieldValueTests
{
    // ── 工厂方法与 As*() ────────────────────────────────────────────────────

    [Fact]
    public void FromDouble_AsDouble_ReturnsCorrectValue()
    {
        var fv = FieldValue.FromDouble(3.14);
        Assert.Equal(FieldType.Float64, fv.Type);
        Assert.Equal(3.14, fv.AsDouble());
    }

    [Fact]
    public void FromLong_AsLong_ReturnsCorrectValue()
    {
        var fv = FieldValue.FromLong(42L);
        Assert.Equal(FieldType.Int64, fv.Type);
        Assert.Equal(42L, fv.AsLong());
    }

    [Fact]
    public void FromBool_True_AsBool_ReturnsTrue()
    {
        var fv = FieldValue.FromBool(true);
        Assert.Equal(FieldType.Boolean, fv.Type);
        Assert.True(fv.AsBool());
    }

    [Fact]
    public void FromBool_False_AsBool_ReturnsFalse()
    {
        var fv = FieldValue.FromBool(false);
        Assert.Equal(FieldType.Boolean, fv.Type);
        Assert.False(fv.AsBool());
    }

    [Fact]
    public void FromString_AsString_ReturnsCorrectValue()
    {
        var fv = FieldValue.FromString("hello");
        Assert.Equal(FieldType.String, fv.Type);
        Assert.Equal("hello", fv.AsString());
    }

    // ── 类型不匹配时的异常 ──────────────────────────────────────────────────

    [Fact]
    public void AsDouble_WhenNotFloat64_ThrowsInvalidOperationException()
        => Assert.Throws<InvalidOperationException>(() => FieldValue.FromLong(1L).AsDouble());

    [Fact]
    public void AsLong_WhenNotInt64_ThrowsInvalidOperationException()
        => Assert.Throws<InvalidOperationException>(() => FieldValue.FromDouble(1.0).AsLong());

    [Fact]
    public void AsBool_WhenNotBoolean_ThrowsInvalidOperationException()
        => Assert.Throws<InvalidOperationException>(() => FieldValue.FromDouble(1.0).AsBool());

    [Fact]
    public void AsString_WhenNotString_ThrowsInvalidOperationException()
        => Assert.Throws<InvalidOperationException>(() => FieldValue.FromDouble(1.0).AsString());

    // ── Equals / GetHashCode ────────────────────────────────────────────────

    [Fact]
    public void Equals_SameTypeAndValue_ReturnsTrue()
    {
        var a = FieldValue.FromDouble(1.0);
        var b = FieldValue.FromDouble(1.0);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentTypes_ReturnsFalse()
    {
        var a = FieldValue.FromLong(1L);
        var b = FieldValue.FromDouble(1.0);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equals_SameStringValue_ReturnsTrue()
    {
        var a = FieldValue.FromString("test");
        var b = FieldValue.FromString("test");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentStringValues_ReturnsFalse()
    {
        var a = FieldValue.FromString("a");
        var b = FieldValue.FromString("b");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void EqualityOperator_Works()
    {
        var a = FieldValue.FromLong(99L);
        var b = FieldValue.FromLong(99L);
        Assert.True(a == b);
        Assert.False(a != b);
    }

    // ── Double 特殊值 ───────────────────────────────────────────────────────

    [Fact]
    public void FromDouble_NaN_RoundTrip()
    {
        var fv = FieldValue.FromDouble(double.NaN);
        Assert.True(double.IsNaN(fv.AsDouble()));
    }

    [Fact]
    public void FromDouble_PositiveInfinity_RoundTrip()
    {
        var fv = FieldValue.FromDouble(double.PositiveInfinity);
        Assert.Equal(double.PositiveInfinity, fv.AsDouble());
    }

    [Fact]
    public void FromDouble_NegativeInfinity_RoundTrip()
    {
        var fv = FieldValue.FromDouble(double.NegativeInfinity);
        Assert.Equal(double.NegativeInfinity, fv.AsDouble());
    }

    [Fact]
    public void FromDouble_NegativeZero_RoundTrip()
    {
        var fv = FieldValue.FromDouble(-0.0);
        // -0.0 和 +0.0 在 IEEE 754 位模式上不同，但数值相等
        Assert.Equal(0.0, fv.AsDouble());
        Assert.True(double.IsNegative(fv.AsDouble()));
    }

    [Fact]
    public void Equals_NaNValues_AreEqual()
    {
        // 两个 NaN 的位模式相同，所以按 FieldValue 的相等性应相等
        var a = FieldValue.FromDouble(double.NaN);
        var b = FieldValue.FromDouble(double.NaN);
        Assert.Equal(a, b);
    }

    // ── TryGetNumeric ───────────────────────────────────────────────────────

    [Fact]
    public void TryGetNumeric_Float64_ReturnsTrue()
    {
        var fv = FieldValue.FromDouble(2.5);
        Assert.True(fv.TryGetNumeric(out double val));
        Assert.Equal(2.5, val);
    }

    [Fact]
    public void TryGetNumeric_Int64_ReturnsTrue_AndConverts()
    {
        var fv = FieldValue.FromLong(10L);
        Assert.True(fv.TryGetNumeric(out double val));
        Assert.Equal(10.0, val);
    }

    [Fact]
    public void TryGetNumeric_Boolean_True_Returns1()
    {
        var fv = FieldValue.FromBool(true);
        Assert.True(fv.TryGetNumeric(out double val));
        Assert.Equal(1.0, val);
    }

    [Fact]
    public void TryGetNumeric_Boolean_False_Returns0()
    {
        var fv = FieldValue.FromBool(false);
        Assert.True(fv.TryGetNumeric(out double val));
        Assert.Equal(0.0, val);
    }

    [Fact]
    public void TryGetNumeric_String_ReturnsFalse()
    {
        var fv = FieldValue.FromString("abc");
        Assert.False(fv.TryGetNumeric(out _));
    }

    // ── FromString(null!) ───────────────────────────────────────────────────

    [Fact]
    public void FromString_Null_ThrowsArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => FieldValue.FromString(null!));

    // ── ToString ────────────────────────────────────────────────────────────

    [Fact]
    public void ToString_Float64_ReturnsFormattedDouble()
    {
        var s = FieldValue.FromDouble(1.5).ToString();
        Assert.Contains("1.5", s);
    }

    [Fact]
    public void ToString_Boolean_True_ReturnsTrue()
        => Assert.Equal("true", FieldValue.FromBool(true).ToString());

    [Fact]
    public void ToString_Boolean_False_ReturnsFalse()
        => Assert.Equal("false", FieldValue.FromBool(false).ToString());

    [Fact]
    public void ToString_String_ReturnsStringValue()
        => Assert.Equal("hello", FieldValue.FromString("hello").ToString());

    // ── Vector（PR #58 a） ───────────────────────────────────────────────────

    [Fact]
    public void FromVector_Array_AsVector_RoundTrip()
    {
        var src = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };
        var fv = FieldValue.FromVector(src);
        Assert.Equal(FieldType.Vector, fv.Type);
        Assert.Equal(4, fv.VectorDimension);
        Assert.True(fv.AsVector().Span.SequenceEqual(src));
    }

    [Fact]
    public void FromVector_ReadOnlyMemory_AsVector_RoundTrip()
    {
        ReadOnlyMemory<float> src = new float[] { 1f, 2f, 3f };
        var fv = FieldValue.FromVector(src);
        Assert.Equal(3, fv.VectorDimension);
        Assert.True(fv.AsVector().Span.SequenceEqual(src.Span));
    }

    [Fact]
    public void FromVector_NullArray_Throws()
        => Assert.Throws<ArgumentNullException>(() => FieldValue.FromVector((float[])null!));

    [Fact]
    public void FromVector_EmptyMemory_Throws()
        => Assert.Throws<ArgumentException>(() => FieldValue.FromVector(ReadOnlyMemory<float>.Empty));

    [Fact]
    public void FromVector_EmptyArray_Throws()
        => Assert.Throws<ArgumentException>(() => FieldValue.FromVector(Array.Empty<float>()));

    [Fact]
    public void Vector_Equals_SameContent_ReturnsTrue()
    {
        var a = FieldValue.FromVector(new float[] { 1f, 2f, 3f });
        var b = FieldValue.FromVector(new float[] { 1f, 2f, 3f });
        Assert.True(a.Equals(b));
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Vector_Equals_DifferentContent_ReturnsFalse()
    {
        var a = FieldValue.FromVector(new float[] { 1f, 2f, 3f });
        var b = FieldValue.FromVector(new float[] { 1f, 2f, 4f });
        Assert.False(a.Equals(b));
        Assert.True(a != b);
    }

    [Fact]
    public void Vector_Equals_DifferentDimension_ReturnsFalse()
    {
        var a = FieldValue.FromVector(new float[] { 1f, 2f });
        var b = FieldValue.FromVector(new float[] { 1f, 2f, 0f });
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Vector_AsDouble_Throws()
    {
        var fv = FieldValue.FromVector(new float[] { 1f });
        Assert.Throws<InvalidOperationException>(() => fv.AsDouble());
    }

    [Fact]
    public void Vector_VectorDimension_OnNonVector_Throws()
    {
        var fv = FieldValue.FromDouble(1.0);
        Assert.Throws<InvalidOperationException>(() => fv.VectorDimension);
    }

    [Fact]
    public void Vector_TryGetNumeric_ReturnsFalse()
    {
        var fv = FieldValue.FromVector(new float[] { 1f, 2f });
        Assert.False(fv.TryGetNumeric(out _));
    }

    [Fact]
    public void Vector_ToString_ShowsDimAndPrefix()
    {
        var fv = FieldValue.FromVector(new float[] { 0.5f, 1.5f });
        var s = fv.ToString();
        Assert.StartsWith("vector(2)[", s);
        Assert.Contains("0.5", s);
        Assert.Contains("1.5", s);
        Assert.EndsWith("]", s);
    }

    [Fact]
    public void Vector_ToString_LongVector_TruncatesWithEllipsis()
    {
        var arr = new float[16];
        for (int i = 0; i < arr.Length; i++) arr[i] = i;
        var s = FieldValue.FromVector(arr).ToString();
        Assert.StartsWith("vector(16)[", s);
        Assert.Contains(",...", s);
    }
}
