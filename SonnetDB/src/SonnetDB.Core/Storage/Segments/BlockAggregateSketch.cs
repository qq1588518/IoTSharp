using SonnetDB.Model;
using SonnetDB.Query.Functions.Aggregates;
using SonnetDB.Storage.Format;

namespace SonnetDB.Storage.Segments;

/// <summary>
/// 单个 block 的扩展聚合 sketch 快照。
/// </summary>
internal sealed class BlockAggregateSketch
{
    public BlockAggregateSketch(
        int blockIndex,
        uint blockCrc32,
        long valueCount,
        TDigest? tDigest,
        HyperLogLog? hyperLogLog)
    {
        BlockIndex = blockIndex;
        BlockCrc32 = blockCrc32;
        ValueCount = valueCount;
        TDigest = tDigest;
        HyperLogLog = hyperLogLog;
    }

    public int BlockIndex { get; }

    public uint BlockCrc32 { get; }

    public long ValueCount { get; }

    public TDigest? TDigest { get; }

    public HyperLogLog? HyperLogLog { get; }

    public static bool TryBuild(
        int blockIndex,
        uint blockCrc32,
        FieldType fieldType,
        ReadOnlySpan<DataPoint> points,
        out BlockAggregateSketch sketch)
    {
        sketch = null!;
        if (points.IsEmpty || fieldType is not (FieldType.Float64 or FieldType.Int64 or FieldType.Boolean))
            return false;

        var digest = new TDigest();
        var hll = new HyperLogLog();
        long count = 0;
        for (int i = 0; i < points.Length; i++)
        {
            if (!points[i].Value.TryGetNumeric(out double value) || double.IsNaN(value))
                continue;

            digest.Add(value);
            hll.Add(value);
            count++;
        }

        if (count == 0)
            return false;

        sketch = new BlockAggregateSketch(blockIndex, blockCrc32, count, digest, hll);
        return true;
    }
}
