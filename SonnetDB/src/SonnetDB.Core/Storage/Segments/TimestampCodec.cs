using System.Buffers.Binary;

namespace SonnetDB.Storage.Segments;

/// <summary>
/// 时间戳载荷编码器：在 V1（原始 8 字节 LE）与 V2（delta-of-delta + zigzag varint）两种格式之间切换。
/// <para>
/// V2 格式（little-endian，按字节顺序）：
/// <code>
///   [0..8)        ts[0]                        // 原始 int64 LE，作为基准锚点
///   varint(zigzag(ts[1] - ts[0]))              // 第一阶差分（仅当 count >= 2）
///   varint(zigzag((ts[i] - ts[i-1]) - (ts[i-1] - ts[i-2])))  for i in [2, count)
/// </code>
/// </para>
/// <para>
/// 设计动机：时序数据通常按近似固定采样间隔产生，相邻一阶差分接近常量，
/// 二阶差分大部分为 0，varint 可压缩到 1 字节。
/// </para>
/// <para>
/// 假设：调用方保证时间戳已按写入顺序排列（与 <see cref="MemTableSeries"/> 一致），
/// 但允许出现负数的二阶差分（zigzag 处理）。
/// </para>
/// </summary>
internal static class TimestampCodec
{
    /// <summary>
    /// 计算把 <paramref name="timestamps"/> 写成 V2（delta-of-delta）后所需的字节数。
    /// </summary>
    /// <param name="timestamps">时间戳序列（毫秒 UTC）。</param>
    /// <returns>所需字节数；空序列返回 0。</returns>
    public static int MeasureDeltaOfDelta(ReadOnlySpan<long> timestamps)
    {
        int count = timestamps.Length;
        if (count == 0)
            return 0;

        int total = 8; // ts[0] raw
        if (count == 1)
            return total;

        long prevDelta = timestamps[1] - timestamps[0];
        total += MeasureVarint(ZigZagEncode(prevDelta));

        for (int i = 2; i < count; i++)
        {
            long delta = timestamps[i] - timestamps[i - 1];
            long dod = delta - prevDelta;
            total += MeasureVarint(ZigZagEncode(dod));
            prevDelta = delta;
        }
        return total;
    }

    /// <summary>
    /// 把 <paramref name="timestamps"/> 编码为 V2 格式写入 <paramref name="destination"/>。
    /// </summary>
    /// <param name="timestamps">时间戳序列（毫秒 UTC）。</param>
    /// <param name="destination">目标缓冲区，长度必须等于 <see cref="MeasureDeltaOfDelta"/> 返回值。</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> 长度不匹配时抛出。</exception>
    public static void WriteDeltaOfDelta(ReadOnlySpan<long> timestamps, Span<byte> destination)
    {
        int needed = MeasureDeltaOfDelta(timestamps);
        if (destination.Length != needed)
            throw new ArgumentException($"目标缓冲区长度 {destination.Length} 与所需长度 {needed} 不一致。", nameof(destination));

        int count = timestamps.Length;
        if (count == 0)
            return;

        BinaryPrimitives.WriteInt64LittleEndian(destination, timestamps[0]);
        int pos = 8;
        if (count == 1)
            return;

        long prevDelta = timestamps[1] - timestamps[0];
        pos += WriteVarint(destination[pos..], ZigZagEncode(prevDelta));

        for (int i = 2; i < count; i++)
        {
            long delta = timestamps[i] - timestamps[i - 1];
            long dod = delta - prevDelta;
            pos += WriteVarint(destination[pos..], ZigZagEncode(dod));
            prevDelta = delta;
        }
    }

    /// <summary>
    /// 解码 V2 格式时间戳载荷，写入 <paramref name="destination"/>。
    /// </summary>
    /// <param name="payload">V2 编码字节视图。</param>
    /// <param name="destination">目标 long 数组视图，长度等于点数。</param>
    /// <exception cref="InvalidDataException">载荷格式损坏（长度不足或 varint 越界）时抛出。</exception>
    public static void ReadDeltaOfDelta(ReadOnlySpan<byte> payload, Span<long> destination)
    {
        int count = destination.Length;
        if (count == 0)
            return;

        if (payload.Length < 8)
            throw new InvalidDataException("时间戳 V2 载荷长度不足以包含锚点。");

        long ts0 = BinaryPrimitives.ReadInt64LittleEndian(payload);
        destination[0] = ts0;
        int pos = 8;

        if (count == 1)
            return;

        long prevDelta = ZigZagDecode(ReadVarint(payload, ref pos));
        long ts = ts0 + prevDelta;
        destination[1] = ts;

        for (int i = 2; i < count; i++)
        {
            long dod = ZigZagDecode(ReadVarint(payload, ref pos));
            long delta = prevDelta + dod;
            ts += delta;
            destination[i] = ts;
            prevDelta = delta;
        }
    }

    /// <summary>
    /// 解码 V2 格式时间戳载荷，直接写入已分配的 <see cref="SonnetDB.Model.DataPoint"/> 目标视图。
    /// 值列保持默认值，供 <see cref="BlockDecoder"/> 随后原地填充。
    /// </summary>
    /// <param name="payload">V2 编码字节视图。</param>
    /// <param name="destination">目标数据点视图，长度等于点数。</param>
    /// <exception cref="InvalidDataException">载荷格式损坏（长度不足或 varint 越界）时抛出。</exception>
    public static void ReadDeltaOfDelta(ReadOnlySpan<byte> payload, Span<SonnetDB.Model.DataPoint> destination)
    {
        int count = destination.Length;
        if (count == 0)
            return;

        if (payload.Length < 8)
            throw new InvalidDataException("时间戳 V2 载荷长度不足以包含锚点。");

        long ts0 = BinaryPrimitives.ReadInt64LittleEndian(payload);
        destination[0] = new SonnetDB.Model.DataPoint(ts0, default);
        int pos = 8;

        if (count == 1)
            return;

        long prevDelta = ZigZagDecode(ReadVarint(payload, ref pos));
        long ts = ts0 + prevDelta;
        destination[1] = new SonnetDB.Model.DataPoint(ts, default);

        for (int i = 2; i < count; i++)
        {
            long dod = ZigZagDecode(ReadVarint(payload, ref pos));
            long delta = prevDelta + dod;
            ts += delta;
            destination[i] = new SonnetDB.Model.DataPoint(ts, default);
            prevDelta = delta;
        }
    }

    // ── ZigZag + varint ──────────────────────────────────────────────────────

    private static ulong ZigZagEncode(long value)
        => (ulong)((value << 1) ^ (value >> 63));

    private static long ZigZagDecode(ulong value)
        => (long)(value >> 1) ^ -(long)(value & 1);

    private static int MeasureVarint(ulong value)
    {
        int n = 1;
        while (value >= 0x80)
        {
            value >>= 7;
            n++;
        }
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
                throw new InvalidDataException("时间戳 V2 载荷 varint 越界。");
            byte b = payload[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;
            shift += 7;
            if (shift > 63)
                throw new InvalidDataException("时间戳 V2 载荷 varint 长度超过 10 字节。");
        }
    }
}
