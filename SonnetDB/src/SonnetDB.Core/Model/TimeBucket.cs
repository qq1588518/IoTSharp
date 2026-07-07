namespace SonnetDB.Model;

/// <summary>
/// 时间桶（time bucket）辅助方法。所有时间戳约定为 Unix epoch 毫秒。
/// </summary>
public static class TimeBucket
{
    /// <summary>
    /// 将时间戳向下对齐到指定 <paramref name="bucketSizeMs"/> 的桶起点。
    /// </summary>
    /// <param name="timestampMs">Unix epoch 毫秒时间戳。</param>
    /// <param name="bucketSizeMs">桶的大小（毫秒，必须 &gt; 0）。</param>
    /// <returns>对齐后的桶起点时间戳（毫秒）。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bucketSizeMs"/> ≤ 0。</exception>
    public static long Floor(long timestampMs, long bucketSizeMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bucketSizeMs, nameof(bucketSizeMs));
        return timestampMs - MathMod(timestampMs, bucketSizeMs);
    }

    /// <summary>
    /// 计算时间戳所在桶的 [start, end) 区间。
    /// </summary>
    /// <param name="timestampMs">Unix epoch 毫秒时间戳。</param>
    /// <param name="bucketSizeMs">桶的大小（毫秒，必须 &gt; 0）。</param>
    /// <returns>(<c>Start</c>, <c>EndExclusive</c>) 区间，满足 <c>End - Start == bucketSizeMs</c>。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bucketSizeMs"/> ≤ 0。</exception>
    public static (long Start, long EndExclusive) Range(long timestampMs, long bucketSizeMs)
    {
        var start = Floor(timestampMs, bucketSizeMs);
        return (start, start + bucketSizeMs);
    }

    /// <summary>
    /// 枚举 [<paramref name="fromMs"/>, <paramref name="toExclusiveMs"/>) 内的所有桶起点。
    /// </summary>
    /// <param name="fromMs">起始时间戳（毫秒，inclusive）。</param>
    /// <param name="toExclusiveMs">结束时间戳（毫秒，exclusive）。</param>
    /// <param name="bucketSizeMs">桶的大小（毫秒，必须 &gt; 0）。</param>
    /// <returns>按升序返回每个桶的起点时间戳。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bucketSizeMs"/> ≤ 0。</exception>
    public static IEnumerable<long> Enumerate(long fromMs, long toExclusiveMs, long bucketSizeMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bucketSizeMs, nameof(bucketSizeMs));

        var start = Floor(fromMs, bucketSizeMs);
        for (var bucket = start; bucket < toExclusiveMs; bucket += bucketSizeMs)
        {
            yield return bucket;
        }
    }

    /// <summary>
    /// 求 <paramref name="value"/> 除以 <paramref name="modulus"/> 的非负余数，
    /// 支持负时间戳（时区偏移等场景）。
    /// </summary>
    private static long MathMod(long value, long modulus)
    {
        var r = value % modulus;
        return r < 0 ? r + modulus : r;
    }
}
