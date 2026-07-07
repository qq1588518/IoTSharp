using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Memory;

/// <summary>
/// <see cref="MemTableSeries"/> 运行期数值聚合（PR #50 新增）相关单元测试。
/// </summary>
public sealed class MemTableSeriesAggregateTests
{
    private static SeriesFieldKey MakeKey() =>
        new(0xABCD_1234_5678_0001UL, "v");

    [Fact]
    public void Float64_RunningAggregates_UpdateOnAppend()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.Float64);
        Assert.True(series.HasNumericAggregates);

        series.Append(1000L, FieldValue.FromDouble(1.5));
        series.Append(2000L, FieldValue.FromDouble(-3.0));
        series.Append(3000L, FieldValue.FromDouble(7.25));

        Assert.True(series.TryGetNumericAggregateSnapshot(
            out int count, out long minTs, out long maxTs,
            out double sum, out double min, out double max));

        Assert.Equal(3, count);
        Assert.Equal(1000L, minTs);
        Assert.Equal(3000L, maxTs);
        Assert.Equal(5.75, sum, precision: 12);
        Assert.Equal(-3.0, min, precision: 12);
        Assert.Equal(7.25, max, precision: 12);
    }

    [Fact]
    public void Int64_RunningAggregates_PreserveSumPrecision()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.Int64);
        series.Append(1000L, FieldValue.FromLong(100L));
        series.Append(2000L, FieldValue.FromLong(-50L));
        series.Append(3000L, FieldValue.FromLong(200L));

        Assert.True(series.TryGetNumericAggregateSnapshot(
            out _, out _, out _, out double sum, out double min, out double max));

        Assert.Equal(250.0, sum);
        Assert.Equal(-50.0, min);
        Assert.Equal(200.0, max);
    }

    [Fact]
    public void Boolean_RunningAggregates_CountTrueValues()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.Boolean);
        series.Append(1000L, FieldValue.FromBool(true));
        series.Append(2000L, FieldValue.FromBool(false));
        series.Append(3000L, FieldValue.FromBool(true));
        series.Append(4000L, FieldValue.FromBool(true));

        Assert.True(series.TryGetNumericAggregateSnapshot(
            out _, out _, out _, out double sum, out double min, out double max));

        Assert.Equal(3.0, sum);
        Assert.Equal(0.0, min);
        Assert.Equal(1.0, max);
    }

    [Fact]
    public void String_HasNoNumericAggregates()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.String);
        Assert.False(series.HasNumericAggregates);

        series.Append(1000L, FieldValue.FromString("hello"));

        Assert.False(series.TryGetNumericAggregateSnapshot(
            out int count, out _, out _, out _, out _, out _));
        Assert.Equal(1, count); // count 仍正确，仅聚合值无意义
    }

    [Fact]
    public void EmptySeries_ReturnsFalseForAggregateSnapshot()
    {
        var series = new MemTableSeries(MakeKey(), FieldType.Float64);

        Assert.False(series.TryGetNumericAggregateSnapshot(
            out int count, out _, out _, out _, out _, out _));
        Assert.Equal(0, count);
    }
}
