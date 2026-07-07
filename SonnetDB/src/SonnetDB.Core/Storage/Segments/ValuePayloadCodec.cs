using System.Buffers;
using System.Text;
using SonnetDB.IO;
using SonnetDB.Model;
using SonnetDB.Storage.Format;

namespace SonnetDB.Storage.Segments;

/// <summary>
/// Raw（v1）值载荷编解码器：按 <see cref="FieldType"/> 编码 <see cref="DataPoint"/> 序列的值部分。
/// <para>
/// 编码规则（little-endian）：
/// <list type="bullet">
///   <item><description><see cref="FieldType.Float64"/>：每个点 8 字节（IEEE 754 LE double）。</description></item>
///   <item><description><see cref="FieldType.Int64"/>：每个点 8 字节（int64 LE）。</description></item>
///   <item><description><see cref="FieldType.Boolean"/>：每个点 1 字节（0=false，1=true）。</description></item>
///   <item><description><see cref="FieldType.String"/>：每个点先写 int32 LE 字节长度，再写 UTF-8 字节。</description></item>
///   <item><description><see cref="FieldType.Vector"/>：每个点 <c>dim × 4</c> 字节（dim×float32 LE，dim 由首个数据点确定，所有点必须一致；PR #58 c）。</description></item>
///   <item><description><see cref="FieldType.GeoPoint"/>：每个点 <c>lat(8) + lon(8)</c> 字节（float64 LE；PR #70）。</description></item>
/// </list>
/// </para>
/// </summary>
internal static class ValuePayloadCodec
{
    /// <summary>
    /// 计算指定类型和点集合的值载荷字节数（不含时间戳载荷）。
    /// </summary>
    /// <param name="fieldType">字段类型。</param>
    /// <param name="points">数据点只读内存。</param>
    /// <returns>值载荷总字节数。</returns>
    /// <exception cref="ArgumentOutOfRangeException">不支持的 FieldType。</exception>
    public static int MeasureValuePayload(FieldType fieldType, ReadOnlyMemory<DataPoint> points)
    {
        int count = points.Length;
        if (count == 0)
            return 0;

        return fieldType switch
        {
            FieldType.Float64 => count * 8,
            FieldType.Int64 => count * 8,
            FieldType.Boolean => count,
            FieldType.GeoPoint => count * 16,
            FieldType.Vector => MeasureVectorPayload(points),
            FieldType.String => MeasureStringPayload(points),
            _ => throw new ArgumentOutOfRangeException(nameof(fieldType), fieldType, null),
        };
    }

    /// <summary>
    /// 将数据点序列的值编码写入目标 <see cref="Span{T}"/>。
    /// </summary>
    /// <param name="fieldType">字段类型。</param>
    /// <param name="points">数据点只读内存（已排序）。</param>
    /// <param name="destination">目标字节缓冲区，大小应等于 <see cref="MeasureValuePayload"/> 的返回值。</param>
    /// <exception cref="ArgumentOutOfRangeException">不支持的 FieldType。</exception>
    public static void WritePayload(FieldType fieldType, ReadOnlyMemory<DataPoint> points, Span<byte> destination)
    {
        if (points.Length == 0)
            return;

        var writer = new SpanWriter(destination);

        switch (fieldType)
        {
            case FieldType.Float64:
                foreach (var dp in points.Span)
                    writer.WriteDouble(dp.Value.AsDouble());
                break;

            case FieldType.Int64:
                foreach (var dp in points.Span)
                    writer.WriteInt64(dp.Value.AsLong());
                break;

            case FieldType.Boolean:
                foreach (var dp in points.Span)
                    writer.WriteByte(dp.Value.AsBool() ? (byte)1 : (byte)0);
                break;

            case FieldType.String:
                WriteStringPayload(ref writer, points);
                break;

            case FieldType.GeoPoint:
                foreach (var dp in points.Span)
                {
                    var p = dp.Value.AsGeoPoint();
                    writer.WriteDouble(p.Lat);
                    writer.WriteDouble(p.Lon);
                }
                break;

            case FieldType.Vector:
                WriteVectorPayload(ref writer, points);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(fieldType), fieldType, null);
        }
    }

    // ── 私有辅助 ──────────────────────────────────────────────────────────────

    private static int MeasureStringPayload(ReadOnlyMemory<DataPoint> points)
    {
        int total = 0;
        foreach (var dp in points.Span)
            total += 4 + Encoding.UTF8.GetByteCount(dp.Value.AsString());
        return total;
    }

    private static void WriteStringPayload(ref SpanWriter writer, ReadOnlyMemory<DataPoint> points)
    {
        foreach (var dp in points.Span)
        {
            string s = dp.Value.AsString();
            int byteCount = Encoding.UTF8.GetByteCount(s);
            byte[] utf8Buf = ArrayPool<byte>.Shared.Rent(Math.Max(byteCount, 1));
            try
            {
                Encoding.UTF8.GetBytes(s, utf8Buf);
                writer.WriteInt32(byteCount);
                writer.WriteBytes(utf8Buf.AsSpan(0, byteCount));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(utf8Buf);
            }
        }
    }

    private static int MeasureVectorPayload(ReadOnlyMemory<DataPoint> points)
    {
        var span = points.Span;
        int dim = span[0].Value.VectorDimension;
        for (int i = 1; i < span.Length; i++)
        {
            int d = span[i].Value.VectorDimension;
            if (d != dim)
                throw new InvalidOperationException(
                    $"Vector block 内所有数据点的 dim 必须一致：首个点 dim={dim}，第 {i} 个点 dim={d}。");
        }
        return span.Length * dim * sizeof(float);
    }

    private static void WriteVectorPayload(ref SpanWriter writer, ReadOnlyMemory<DataPoint> points)
    {
        var span = points.Span;
        int dim = span[0].Value.VectorDimension;
        for (int i = 0; i < span.Length; i++)
        {
            ReadOnlySpan<float> components = span[i].Value.AsVector().Span;
            if (components.Length != dim)
                throw new InvalidOperationException(
                    $"Vector block 内所有数据点的 dim 必须一致：首个点 dim={dim}，第 {i} 个点 dim={components.Length}。");
            // SpanWriter.WriteBytes 接受 ReadOnlySpan<byte>，使用 MemoryMarshal.AsBytes 做安全 reinterpret。
            writer.WriteBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(components));
        }
    }
}
