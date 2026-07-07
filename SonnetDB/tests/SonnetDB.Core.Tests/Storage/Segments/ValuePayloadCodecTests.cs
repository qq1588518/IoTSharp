using System.Buffers.Binary;
using System.Text;
using SonnetDB.Model;
using SonnetDB.Storage.Format;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Storage.Segments;

/// <summary>
/// <see cref="ValuePayloadCodec"/> 单元测试：验证 4 种 FieldType 的 Raw 编码 round-trip。
/// </summary>
public sealed class ValuePayloadCodecTests
{
    // ── Float64 ─────────────────────────────────────────────────────────────

    [Fact]
    public void Float64_RoundTrip_AllValuesEqual()
    {
        double[] values = [0.0, 1.5, -3.14, double.MaxValue, double.MinValue, double.NaN, double.PositiveInfinity];
        var points = MakePoints(values, FieldValue.FromDouble);

        int expectedLen = values.Length * 8;
        Assert.Equal(expectedLen, ValuePayloadCodec.MeasureValuePayload(FieldType.Float64, points));

        byte[] buf = new byte[expectedLen];
        ValuePayloadCodec.WritePayload(FieldType.Float64, points, buf);

        for (int i = 0; i < values.Length; i++)
        {
            double read = BinaryPrimitives.ReadDoubleLittleEndian(buf.AsSpan(i * 8, 8));
            // Compare bit patterns to handle NaN equality
            Assert.Equal(BitConverter.DoubleToInt64Bits(values[i]), BitConverter.DoubleToInt64Bits(read));
        }
    }

    [Fact]
    public void Float64_EmptyCollection_ReturnsZeroLength()
    {
        var empty = ReadOnlyMemory<DataPoint>.Empty;
        Assert.Equal(0, ValuePayloadCodec.MeasureValuePayload(FieldType.Float64, empty));
        // WritePayload with empty collection should not throw
        ValuePayloadCodec.WritePayload(FieldType.Float64, empty, Span<byte>.Empty);
    }

    // ── Int64 ────────────────────────────────────────────────────────────────

    [Fact]
    public void Int64_RoundTrip_AllValuesEqual()
    {
        long[] values = [0L, 1L, -1L, long.MaxValue, long.MinValue, 100_000_000L];
        var points = MakePoints(values, FieldValue.FromLong);

        int expectedLen = values.Length * 8;
        Assert.Equal(expectedLen, ValuePayloadCodec.MeasureValuePayload(FieldType.Int64, points));

        byte[] buf = new byte[expectedLen];
        ValuePayloadCodec.WritePayload(FieldType.Int64, points, buf);

        for (int i = 0; i < values.Length; i++)
        {
            long read = BinaryPrimitives.ReadInt64LittleEndian(buf.AsSpan(i * 8, 8));
            Assert.Equal(values[i], read);
        }
    }

    [Fact]
    public void Int64_EmptyCollection_ReturnsZeroLength()
    {
        var empty = ReadOnlyMemory<DataPoint>.Empty;
        Assert.Equal(0, ValuePayloadCodec.MeasureValuePayload(FieldType.Int64, empty));
        ValuePayloadCodec.WritePayload(FieldType.Int64, empty, Span<byte>.Empty);
    }

    // ── Boolean ──────────────────────────────────────────────────────────────

    [Fact]
    public void Boolean_RoundTrip_AllValuesEqual()
    {
        bool[] values = [true, false, true, true, false];
        var points = MakePoints(values, FieldValue.FromBool);

        int expectedLen = values.Length;
        Assert.Equal(expectedLen, ValuePayloadCodec.MeasureValuePayload(FieldType.Boolean, points));

        byte[] buf = new byte[expectedLen];
        ValuePayloadCodec.WritePayload(FieldType.Boolean, points, buf);

        for (int i = 0; i < values.Length; i++)
        {
            bool read = buf[i] != 0;
            Assert.Equal(values[i], read);
            Assert.True(buf[i] == 0 || buf[i] == 1, $"Boolean byte must be 0 or 1, got {buf[i]}");
        }
    }

    [Fact]
    public void Boolean_EmptyCollection_ReturnsZeroLength()
    {
        var empty = ReadOnlyMemory<DataPoint>.Empty;
        Assert.Equal(0, ValuePayloadCodec.MeasureValuePayload(FieldType.Boolean, empty));
        ValuePayloadCodec.WritePayload(FieldType.Boolean, empty, Span<byte>.Empty);
    }

    // ── String ───────────────────────────────────────────────────────────────

    [Fact]
    public void String_RoundTrip_AllValuesEqual()
    {
        string[] values = ["hello", "世界", "", "SonnetDB", "a", "测试123"];
        var points = MakePoints(values, FieldValue.FromString);

        int expectedLen = values.Sum(s => 4 + Encoding.UTF8.GetByteCount(s));
        Assert.Equal(expectedLen, ValuePayloadCodec.MeasureValuePayload(FieldType.String, points));

        byte[] buf = new byte[expectedLen];
        ValuePayloadCodec.WritePayload(FieldType.String, points, buf);

        int offset = 0;
        for (int i = 0; i < values.Length; i++)
        {
            int len = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(offset, 4));
            offset += 4;
            string decoded = Encoding.UTF8.GetString(buf, offset, len);
            Assert.Equal(values[i], decoded);
            offset += len;
        }
        Assert.Equal(expectedLen, offset);
    }

    [Fact]
    public void String_EmptyCollection_ReturnsZeroLength()
    {
        var empty = ReadOnlyMemory<DataPoint>.Empty;
        Assert.Equal(0, ValuePayloadCodec.MeasureValuePayload(FieldType.String, empty));
        ValuePayloadCodec.WritePayload(FieldType.String, empty, Span<byte>.Empty);
    }

    // ── MeasureValuePayload accuracy ─────────────────────────────────────────

    [Theory]
    [InlineData(FieldType.Float64, 10)]
    [InlineData(FieldType.Int64, 7)]
    [InlineData(FieldType.Boolean, 15)]
    public void MeasureValuePayload_MatchesActualBytesWritten(FieldType fieldType, int count)
    {
        ReadOnlyMemory<DataPoint> points = fieldType switch
        {
            FieldType.Float64 => MakePoints(Enumerable.Range(0, count).Select(i => (double)i).ToArray(), FieldValue.FromDouble),
            FieldType.Int64 => MakePoints(Enumerable.Range(0, count).Select(i => (long)i).ToArray(), FieldValue.FromLong),
            FieldType.Boolean => MakePoints(Enumerable.Range(0, count).Select(i => i % 2 == 0).ToArray(), FieldValue.FromBool),
            _ => throw new InvalidOperationException(),
        };

        int measured = ValuePayloadCodec.MeasureValuePayload(fieldType, points);
        byte[] buf = new byte[measured];
        ValuePayloadCodec.WritePayload(fieldType, points, buf.AsSpan());

        Assert.Equal(measured, buf.Length);
    }

    [Fact]
    public void MeasureValuePayload_String_MatchesActualBytesWritten()
    {
        string[] values = ["hello", "world", "测试", ""];
        var points = MakePoints(values, FieldValue.FromString);

        int measured = ValuePayloadCodec.MeasureValuePayload(FieldType.String, points);
        byte[] buf = new byte[measured];
        ValuePayloadCodec.WritePayload(FieldType.String, points, buf.AsSpan());

        Assert.Equal(measured, buf.Length);
    }

    // ── 辅助方法 ──────────────────────────────────────────────────────────────

    private static ReadOnlyMemory<DataPoint> MakePoints<T>(T[] values, Func<T, FieldValue> factory)
    {
        var arr = new DataPoint[values.Length];
        for (int i = 0; i < values.Length; i++)
            arr[i] = new DataPoint(i * 1000L, factory(values[i]));
        return arr;
    }
}
