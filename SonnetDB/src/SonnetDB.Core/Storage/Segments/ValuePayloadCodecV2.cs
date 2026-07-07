using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using SonnetDB.Model;
using SonnetDB.Storage.Format;

namespace SonnetDB.Storage.Segments;

/// <summary>
/// 值列 V2 编解码器。提供针对 <see cref="FieldType"/> 的差异化压缩：
/// <list type="bullet">
///   <item><description><see cref="FieldType.Float64"/>：简化版 Gorilla XOR 位流，前导/尾随零位段消除。</description></item>
///   <item><description><see cref="FieldType.Boolean"/>：游程长度编码（RLE），首字节为初值，后续 varint 为各段长度。</description></item>
///   <item><description><see cref="FieldType.String"/>：按出现顺序构建字典，先写字典区，再写每个点的 varint 字典索引。</description></item>
///   <item><description><see cref="FieldType.Int64"/>：保持原始 8B LE（与 V1 等同；本 PR 暂不压缩）。</description></item>
/// </list>
/// 编码 1 字节布局总和等于 BlockHeader 中记录的 ValuePayloadLength；解码端必须依据
/// <see cref="BlockDescriptor"/> 的 <c>ValueEncoding</c> 标志决定走 V1 / V2 路径。
/// </summary>
internal static class ValuePayloadCodecV2
{
    // ── Measure ──────────────────────────────────────────────────────────────

    /// <summary>计算 V2 编码所需字节数。</summary>
    public static int Measure(FieldType fieldType, ReadOnlyMemory<DataPoint> points)
    {
        if (points.Length == 0)
            return 0;

        return fieldType switch
        {
            FieldType.Float64 => MeasureFloat64(points.Span),
            FieldType.Int64 => points.Length * 8,
            FieldType.Boolean => MeasureBool(points.Span),
            FieldType.String => MeasureString(points.Span),
            _ => throw new ArgumentOutOfRangeException(nameof(fieldType), fieldType, null),
        };
    }

    // ── Encode ───────────────────────────────────────────────────────────────

    /// <summary>把 <paramref name="points"/> 的值列以 V2 格式写入 <paramref name="destination"/>。</summary>
    public static void Write(FieldType fieldType, ReadOnlyMemory<DataPoint> points, Span<byte> destination)
    {
        int needed = Measure(fieldType, points);
        if (destination.Length != needed)
            throw new ArgumentException($"目标缓冲区长度 {destination.Length} 与所需长度 {needed} 不一致。", nameof(destination));

        if (points.Length == 0)
            return;

        switch (fieldType)
        {
            case FieldType.Float64:
                WriteFloat64(points.Span, destination);
                break;
            case FieldType.Int64:
                WriteInt64Raw(points.Span, destination);
                break;
            case FieldType.Boolean:
                WriteBool(points.Span, destination);
                break;
            case FieldType.String:
                WriteString(points.Span, destination);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fieldType), fieldType, null);
        }
    }

    // ── Decode ───────────────────────────────────────────────────────────────

    /// <summary>解码 V2 值载荷为 <see cref="FieldValue"/> 数组（顺序与原写入一致）。</summary>
    public static FieldValue[] Decode(FieldType fieldType, ReadOnlySpan<byte> payload, int count)
    {
        if (count == 0)
            return [];

        return fieldType switch
        {
            FieldType.Float64 => DecodeFloat64(payload, count),
            FieldType.Int64 => DecodeInt64Raw(payload, count),
            FieldType.Boolean => DecodeBool(payload, count),
            FieldType.String => DecodeString(payload, count),
            _ => throw new ArgumentOutOfRangeException(nameof(fieldType), fieldType, null),
        };
    }

    /// <summary>
    /// 解码 V2 值载荷并直接填充已包含时间戳的 <see cref="DataPoint"/> 目标视图。
    /// </summary>
    public static void DecodeInto(
        FieldType fieldType,
        ReadOnlySpan<byte> payload,
        int count,
        Span<DataPoint> destination)
    {
        if (count == 0)
            return;

        if (destination.Length < count)
            throw new ArgumentException("目标缓冲区长度小于点数。", nameof(destination));

        switch (fieldType)
        {
            case FieldType.Float64:
                DecodeFloat64Into(payload, count, destination);
                break;
            case FieldType.Int64:
                DecodeInt64RawInto(payload, count, destination);
                break;
            case FieldType.Boolean:
                DecodeBoolInto(payload, count, destination);
                break;
            case FieldType.String:
                DecodeStringInto(payload, count, destination);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fieldType), fieldType, null);
        }
    }

    /// <summary>
    /// 解码 V2 值载荷的指定逻辑区间，并直接填充已包含时间戳的目标视图。
    /// </summary>
    public static void DecodeRangeInto(
        FieldType fieldType,
        ReadOnlySpan<byte> payload,
        int totalCount,
        int start,
        int rangeCount,
        Span<DataPoint> destination)
    {
        if ((uint)start > (uint)totalCount || (uint)rangeCount > (uint)(totalCount - start))
            throw new ArgumentOutOfRangeException(nameof(start), "解码范围超出点数。");

        if (rangeCount == 0)
            return;

        if (destination.Length < rangeCount)
            throw new ArgumentException("目标缓冲区长度小于范围点数。", nameof(destination));

        switch (fieldType)
        {
            case FieldType.Float64:
                DecodeFloat64RangeInto(payload, totalCount, start, rangeCount, destination);
                break;
            case FieldType.Int64:
                DecodeInt64RawRangeInto(payload, totalCount, start, rangeCount, destination);
                break;
            case FieldType.Boolean:
                DecodeBoolRangeInto(payload, totalCount, start, rangeCount, destination);
                break;
            case FieldType.String:
                DecodeStringRangeInto(payload, totalCount, start, rangeCount, destination);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fieldType), fieldType, null);
        }
    }

    // ── Float64 (Gorilla XOR) ───────────────────────────────────────────────

    /// <remarks>
    /// 位流格式（高位优先）：
    /// 头 64 位 = 第一个 double 的 IEEE754 大端原值（写在缓冲区开始的 8 字节中）。
    /// 之后对每个新值 v[i]，xor = bits(v[i]) ^ bits(v[i-1])：
    ///   xor == 0 → 写一位 '0'。
    ///   xor != 0 → 写一位 '1'，6 位 leadingZeros (0..63)，6 位 (meaningfulBits - 1) (0..63)，meaningful 位 xor 主体。
    /// 末尾不足一字节用 0 补齐（解码端通过 count 控制读取量）。
    /// </remarks>
    private static int MeasureFloat64(ReadOnlySpan<DataPoint> points)
    {
        int bits = 64; // 第一个值原样
        ulong prev = BitConverter.DoubleToUInt64Bits(points[0].Value.AsDouble());
        for (int i = 1; i < points.Length; i++)
        {
            ulong cur = BitConverter.DoubleToUInt64Bits(points[i].Value.AsDouble());
            ulong xor = cur ^ prev;
            if (xor == 0)
            {
                bits += 1;
            }
            else
            {
                int leadZ = BitOperations.LeadingZeroCount(xor);
                int trailZ = BitOperations.TrailingZeroCount(xor);
                int meaningful = 64 - leadZ - trailZ;
                bits += 1 + 6 + 6 + meaningful;
            }
            prev = cur;
        }
        return (bits + 7) / 8;
    }

    private static void WriteFloat64(ReadOnlySpan<DataPoint> points, Span<byte> destination)
    {
        // BitWriter 以 OR 方式写位，必须保证目标缓冲区初始为 0（ArrayPool 不归零）。
        destination.Clear();
        var writer = new BitWriter(destination);
        ulong prev = BitConverter.DoubleToUInt64Bits(points[0].Value.AsDouble());
        writer.WriteBits(prev, 64);

        for (int i = 1; i < points.Length; i++)
        {
            ulong cur = BitConverter.DoubleToUInt64Bits(points[i].Value.AsDouble());
            ulong xor = cur ^ prev;
            if (xor == 0)
            {
                writer.WriteBit(0);
            }
            else
            {
                int leadZ = BitOperations.LeadingZeroCount(xor);
                int trailZ = BitOperations.TrailingZeroCount(xor);
                int meaningful = 64 - leadZ - trailZ;
                writer.WriteBit(1);
                writer.WriteBits((ulong)leadZ, 6);
                writer.WriteBits((ulong)(meaningful - 1), 6);
                writer.WriteBits(xor >> trailZ, meaningful);
            }
            prev = cur;
        }
    }

    private static FieldValue[] DecodeFloat64(ReadOnlySpan<byte> payload, int count)
    {
        var reader = new BitReader(payload);
        ulong prev = reader.ReadBits(64);

        var result = new FieldValue[count];
        result[0] = FieldValue.FromDouble(BitConverter.UInt64BitsToDouble(prev));

        for (int i = 1; i < count; i++)
        {
            int control = reader.ReadBit();
            ulong cur;
            if (control == 0)
            {
                cur = prev;
            }
            else
            {
                int leadZ = (int)reader.ReadBits(6);
                int meaningful = (int)reader.ReadBits(6) + 1;
                int trailZ = 64 - leadZ - meaningful;
                if (trailZ < 0)
                    throw new InvalidDataException("Gorilla XOR 解码失败：leading + meaningful 越界。");
                ulong xor = reader.ReadBits(meaningful) << trailZ;
                cur = prev ^ xor;
            }
            result[i] = FieldValue.FromDouble(BitConverter.UInt64BitsToDouble(cur));
            prev = cur;
        }
        return result;
    }

    private static void DecodeFloat64Into(ReadOnlySpan<byte> payload, int count, Span<DataPoint> destination)
    {
        var reader = new BitReader(payload);
        ulong prev = reader.ReadBits(64);

        destination[0] = new DataPoint(
            destination[0].Timestamp,
            FieldValue.FromDouble(BitConverter.UInt64BitsToDouble(prev)));

        for (int i = 1; i < count; i++)
        {
            int control = reader.ReadBit();
            ulong cur;
            if (control == 0)
            {
                cur = prev;
            }
            else
            {
                int leadZ = (int)reader.ReadBits(6);
                int meaningful = (int)reader.ReadBits(6) + 1;
                int trailZ = 64 - leadZ - meaningful;
                if (trailZ < 0)
                    throw new InvalidDataException("Gorilla XOR 解码失败：leading + meaningful 越界。");
                ulong xor = reader.ReadBits(meaningful) << trailZ;
                cur = prev ^ xor;
            }

            destination[i] = new DataPoint(
                destination[i].Timestamp,
                FieldValue.FromDouble(BitConverter.UInt64BitsToDouble(cur)));
            prev = cur;
        }
    }

    private static void DecodeFloat64RangeInto(
        ReadOnlySpan<byte> payload,
        int totalCount,
        int start,
        int rangeCount,
        Span<DataPoint> destination)
    {
        var reader = new BitReader(payload);
        ulong prev = reader.ReadBits(64);
        int end = start + rangeCount;

        if (start == 0)
        {
            destination[0] = new DataPoint(
                destination[0].Timestamp,
                FieldValue.FromDouble(BitConverter.UInt64BitsToDouble(prev)));
        }

        for (int i = 1; i < totalCount; i++)
        {
            int control = reader.ReadBit();
            ulong cur;
            if (control == 0)
            {
                cur = prev;
            }
            else
            {
                int leadZ = (int)reader.ReadBits(6);
                int meaningful = (int)reader.ReadBits(6) + 1;
                int trailZ = 64 - leadZ - meaningful;
                if (trailZ < 0)
                    throw new InvalidDataException("Gorilla XOR 解码失败：leading + meaningful 越界。");
                ulong xor = reader.ReadBits(meaningful) << trailZ;
                cur = prev ^ xor;
            }

            if (i >= start && i < end)
            {
                int targetIndex = i - start;
                destination[targetIndex] = new DataPoint(
                    destination[targetIndex].Timestamp,
                    FieldValue.FromDouble(BitConverter.UInt64BitsToDouble(cur)));
            }
            prev = cur;
        }
    }

    // ── Int64 raw (passthrough) ─────────────────────────────────────────────

    private static void WriteInt64Raw(ReadOnlySpan<DataPoint> points, Span<byte> destination)
    {
        for (int i = 0; i < points.Length; i++)
            BinaryPrimitives.WriteInt64LittleEndian(destination[(i * 8)..], points[i].Value.AsLong());
    }

    private static FieldValue[] DecodeInt64Raw(ReadOnlySpan<byte> payload, int count)
    {
        var result = new FieldValue[count];
        for (int i = 0; i < count; i++)
            result[i] = FieldValue.FromLong(BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(i * 8, 8)));
        return result;
    }

    private static void DecodeInt64RawInto(ReadOnlySpan<byte> payload, int count, Span<DataPoint> destination)
    {
        for (int i = 0; i < count; i++)
        {
            long value = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(i * 8, 8));
            destination[i] = new DataPoint(destination[i].Timestamp, FieldValue.FromLong(value));
        }
    }

    private static void DecodeInt64RawRangeInto(
        ReadOnlySpan<byte> payload,
        int totalCount,
        int start,
        int rangeCount,
        Span<DataPoint> destination)
    {
        if ((long)totalCount * 8L > payload.Length)
            throw new InvalidDataException("Int64 V2 载荷长度不足。");

        for (int i = 0; i < rangeCount; i++)
        {
            long value = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice((start + i) * 8, 8));
            destination[i] = new DataPoint(destination[i].Timestamp, FieldValue.FromLong(value));
        }
    }

    // ── Boolean RLE ─────────────────────────────────────────────────────────

    /// <remarks>
    /// 字节布局：
    /// [0]      = 第一个游程的值（0 或 1）
    /// [1..]    = 连续 varint，每个 varint = 该段游程长度（>= 1），段值在 0 / 1 之间交替。
    /// 解码端按 count 累加直到达到 count 即结束。
    /// </remarks>
    private static int MeasureBool(ReadOnlySpan<DataPoint> points)
    {
        int total = 1;
        bool current = points[0].Value.AsBool();
        int run = 1;
        for (int i = 1; i < points.Length; i++)
        {
            bool v = points[i].Value.AsBool();
            if (v == current)
            {
                run++;
            }
            else
            {
                total += MeasureVarint((ulong)run);
                current = v;
                run = 1;
            }
        }
        total += MeasureVarint((ulong)run);
        return total;
    }

    private static void WriteBool(ReadOnlySpan<DataPoint> points, Span<byte> destination)
    {
        bool current = points[0].Value.AsBool();
        destination[0] = current ? (byte)1 : (byte)0;
        int pos = 1;
        int run = 1;
        for (int i = 1; i < points.Length; i++)
        {
            bool v = points[i].Value.AsBool();
            if (v == current)
            {
                run++;
            }
            else
            {
                pos += WriteVarint(destination[pos..], (ulong)run);
                current = v;
                run = 1;
            }
        }
        WriteVarint(destination[pos..], (ulong)run);
    }

    private static FieldValue[] DecodeBool(ReadOnlySpan<byte> payload, int count)
    {
        if (payload.Length < 1)
            throw new InvalidDataException("Bool RLE 载荷长度不足。");

        bool current = payload[0] != 0;
        int pos = 1;
        var result = new FieldValue[count];
        int written = 0;
        while (written < count)
        {
            int run = checked((int)ReadVarint(payload, ref pos));
            if (run <= 0 || written + run > count)
                throw new InvalidDataException($"Bool RLE 段长越界：run={run}, written={written}, count={count}。");
            for (int k = 0; k < run; k++)
                result[written + k] = FieldValue.FromBool(current);
            written += run;
            current = !current;
        }
        return result;
    }

    private static void DecodeBoolInto(ReadOnlySpan<byte> payload, int count, Span<DataPoint> destination)
    {
        if (payload.Length < 1)
            throw new InvalidDataException("Bool RLE 载荷长度不足。");

        bool current = payload[0] != 0;
        int pos = 1;
        int written = 0;
        while (written < count)
        {
            int run = checked((int)ReadVarint(payload, ref pos));
            if (run <= 0 || written + run > count)
                throw new InvalidDataException($"Bool RLE 段长越界：run={run}, written={written}, count={count}。");
            for (int k = 0; k < run; k++)
            {
                int index = written + k;
                destination[index] = new DataPoint(destination[index].Timestamp, FieldValue.FromBool(current));
            }
            written += run;
            current = !current;
        }
    }

    private static void DecodeBoolRangeInto(
        ReadOnlySpan<byte> payload,
        int totalCount,
        int start,
        int rangeCount,
        Span<DataPoint> destination)
    {
        if (payload.Length < 1)
            throw new InvalidDataException("Bool RLE 载荷长度不足。");

        bool current = payload[0] != 0;
        int pos = 1;
        int written = 0;
        int rangeEnd = start + rangeCount;
        while (written < totalCount)
        {
            int run = checked((int)ReadVarint(payload, ref pos));
            if (run <= 0 || written + run > totalCount)
                throw new InvalidDataException($"Bool RLE 段长越界：run={run}, written={written}, count={totalCount}。");

            int runEnd = written + run;
            int copyStart = Math.Max(written, start);
            int copyEnd = Math.Min(runEnd, rangeEnd);
            for (int sourceIndex = copyStart; sourceIndex < copyEnd; sourceIndex++)
            {
                int targetIndex = sourceIndex - start;
                destination[targetIndex] = new DataPoint(
                    destination[targetIndex].Timestamp,
                    FieldValue.FromBool(current));
            }

            written = runEnd;
            current = !current;
        }
    }

    // ── String dictionary ───────────────────────────────────────────────────

    /// <remarks>
    /// 字节布局：
    /// varint(dictSize)
    /// 重复 dictSize 次：varint(byteLen) + UTF-8 bytes
    /// 重复 count 次：varint(dictIndex)
    /// </remarks>
    private static int MeasureString(ReadOnlySpan<DataPoint> points)
    {
        var dict = BuildDict(points);
        int total = MeasureVarint((ulong)dict.Strings.Count);
        foreach (var bytes in dict.StringBytes)
        {
            total += MeasureVarint((ulong)bytes.Length);
            total += bytes.Length;
        }
        for (int i = 0; i < points.Length; i++)
            total += MeasureVarint((ulong)dict.Indices[i]);
        return total;
    }

    private static void WriteString(ReadOnlySpan<DataPoint> points, Span<byte> destination)
    {
        var dict = BuildDict(points);
        int pos = 0;
        pos += WriteVarint(destination[pos..], (ulong)dict.Strings.Count);
        for (int e = 0; e < dict.Strings.Count; e++)
        {
            byte[] bytes = dict.StringBytes[e];
            pos += WriteVarint(destination[pos..], (ulong)bytes.Length);
            bytes.AsSpan().CopyTo(destination[pos..]);
            pos += bytes.Length;
        }
        for (int i = 0; i < points.Length; i++)
            pos += WriteVarint(destination[pos..], (ulong)dict.Indices[i]);
    }

    private static FieldValue[] DecodeString(ReadOnlySpan<byte> payload, int count)
    {
        int pos = 0;
        int dictSize = checked((int)ReadVarint(payload, ref pos));
        if (dictSize < 0)
            throw new InvalidDataException("字符串字典大小为负。");

        var dict = new string[dictSize];
        for (int e = 0; e < dictSize; e++)
        {
            int len = checked((int)ReadVarint(payload, ref pos));
            if (len < 0 || pos + len > payload.Length)
                throw new InvalidDataException($"字符串字典条目越界：len={len}, pos={pos}, payload={payload.Length}。");
            dict[e] = Encoding.UTF8.GetString(payload.Slice(pos, len));
            pos += len;
        }

        var result = new FieldValue[count];
        for (int i = 0; i < count; i++)
        {
            int idx = checked((int)ReadVarint(payload, ref pos));
            if ((uint)idx >= (uint)dictSize)
                throw new InvalidDataException($"字符串字典索引越界：idx={idx}, dictSize={dictSize}。");
            result[i] = FieldValue.FromString(dict[idx]);
        }
        return result;
    }

    private static void DecodeStringInto(ReadOnlySpan<byte> payload, int count, Span<DataPoint> destination)
    {
        int pos = 0;
        int dictSize = checked((int)ReadVarint(payload, ref pos));
        if (dictSize < 0)
            throw new InvalidDataException("字符串字典大小为负。");

        var dict = new string[dictSize];
        for (int e = 0; e < dictSize; e++)
        {
            int len = checked((int)ReadVarint(payload, ref pos));
            if (len < 0 || pos + len > payload.Length)
                throw new InvalidDataException($"字符串字典条目越界：len={len}, pos={pos}, payload={payload.Length}。");
            dict[e] = Encoding.UTF8.GetString(payload.Slice(pos, len));
            pos += len;
        }

        for (int i = 0; i < count; i++)
        {
            int idx = checked((int)ReadVarint(payload, ref pos));
            if ((uint)idx >= (uint)dictSize)
                throw new InvalidDataException($"字符串字典索引越界：idx={idx}, dictSize={dictSize}。");
            destination[i] = new DataPoint(destination[i].Timestamp, FieldValue.FromString(dict[idx]));
        }
    }

    private static void DecodeStringRangeInto(
        ReadOnlySpan<byte> payload,
        int totalCount,
        int start,
        int rangeCount,
        Span<DataPoint> destination)
    {
        int pos = 0;
        int dictSize = checked((int)ReadVarint(payload, ref pos));
        if (dictSize < 0)
            throw new InvalidDataException("字符串字典大小为负。");

        var dict = new string[dictSize];
        for (int e = 0; e < dictSize; e++)
        {
            int len = checked((int)ReadVarint(payload, ref pos));
            if (len < 0 || pos + len > payload.Length)
                throw new InvalidDataException($"字符串字典条目越界：len={len}, pos={pos}, payload={payload.Length}。");
            dict[e] = Encoding.UTF8.GetString(payload.Slice(pos, len));
            pos += len;
        }

        int end = start + rangeCount;
        for (int i = 0; i < totalCount; i++)
        {
            int idx = checked((int)ReadVarint(payload, ref pos));
            if ((uint)idx >= (uint)dictSize)
                throw new InvalidDataException($"字符串字典索引越界：idx={idx}, dictSize={dictSize}。");

            if (i >= start && i < end)
            {
                int targetIndex = i - start;
                destination[targetIndex] = new DataPoint(
                    destination[targetIndex].Timestamp,
                    FieldValue.FromString(dict[idx]));
            }
        }
    }

    private readonly record struct StringDict(
        List<string> Strings,
        List<byte[]> StringBytes,
        int[] Indices);

    private static StringDict BuildDict(ReadOnlySpan<DataPoint> points)
    {
        var strings = new List<string>();
        var stringBytes = new List<byte[]>();
        var lookup = new Dictionary<string, int>(StringComparer.Ordinal);
        var indices = new int[points.Length];

        for (int i = 0; i < points.Length; i++)
        {
            string s = points[i].Value.AsString();
            if (!lookup.TryGetValue(s, out int idx))
            {
                idx = strings.Count;
                strings.Add(s);
                stringBytes.Add(Encoding.UTF8.GetBytes(s));
                lookup[s] = idx;
            }
            indices[i] = idx;
        }
        return new StringDict(strings, stringBytes, indices);
    }

    // ── varint helpers (与 TimestampCodec 同实现，保持单元独立) ─────────────

    private static int MeasureVarint(ulong value)
    {
        int n = 1;
        while (value >= 0x80) { value >>= 7; n++; }
        return n;
    }

    private static int WriteVarint(Span<byte> destination, ulong value)
    {
        int n = 0;
        while (value >= 0x80)
        {
            destination[n++] = (byte)(value | 0x80);
            value >>= 7;
        }
        destination[n++] = (byte)value;
        return n;
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> payload, ref int pos)
    {
        ulong result = 0;
        int shift = 0;
        while (true)
        {
            if (pos >= payload.Length)
                throw new InvalidDataException("V2 值载荷 varint 越界。");
            byte b = payload[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;
            shift += 7;
            if (shift > 63)
                throw new InvalidDataException("V2 值载荷 varint 长度超过 10 字节。");
        }
    }
}
