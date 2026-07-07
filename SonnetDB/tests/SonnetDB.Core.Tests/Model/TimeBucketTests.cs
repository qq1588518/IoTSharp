using SonnetDB.Model;
using Xunit;

namespace SonnetDB.Core.Tests.Model;

/// <summary>
/// <see cref="TimeBucket"/> 单元测试。
/// </summary>
public sealed class TimeBucketTests
{
    // ── Floor ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0L, 1000L, 0L)]          // 对齐到整秒桶起点
    [InlineData(999L, 1000L, 0L)]        // 桶内最后一毫秒
    [InlineData(1000L, 1000L, 1000L)]    // 正好在桶边界
    [InlineData(1500L, 1000L, 1000L)]    // 在第二个桶内
    [InlineData(60000L, 60000L, 60000L)] // 正好在分钟桶边界
    [InlineData(75000L, 60000L, 60000L)] // 分钟桶内
    public void Floor_AlignsToCorrectBucketStart(long ts, long bucketMs, long expected)
        => Assert.Equal(expected, TimeBucket.Floor(ts, bucketMs));

    [Fact]
    public void Floor_HourBucket_AlignsCorrectly()
    {
        const long oneHour = 3_600_000L;
        long ts = oneHour + 123_456L; // 1小时零一点
        Assert.Equal(oneHour, TimeBucket.Floor(ts, oneHour));
    }

    // ── Range ───────────────────────────────────────────────────────────────

    [Fact]
    public void Range_ContainsOriginalTimestamp()
    {
        var (start, end) = TimeBucket.Range(1500L, 1000L);
        Assert.True(start <= 1500L);
        Assert.True(end > 1500L);
    }

    [Fact]
    public void Range_WidthEqualsBucketSize()
    {
        const long bucketMs = 5000L;
        var (start, end) = TimeBucket.Range(7777L, bucketMs);
        Assert.Equal(bucketMs, end - start);
    }

    [Fact]
    public void Range_StartIsFloor()
    {
        var (start, _) = TimeBucket.Range(1500L, 1000L);
        Assert.Equal(TimeBucket.Floor(1500L, 1000L), start);
    }

    // ── Enumerate ───────────────────────────────────────────────────────────

    [Fact]
    public void Enumerate_OneSecondBuckets_CorrectCount()
    {
        // [0, 5000) with 1000ms buckets → 5 buckets: 0,1000,2000,3000,4000
        var buckets = TimeBucket.Enumerate(0L, 5000L, 1000L).ToList();
        Assert.Equal(5, buckets.Count);
        Assert.Equal(0L, buckets[0]);
        Assert.Equal(4000L, buckets[4]);
    }

    [Fact]
    public void Enumerate_FromMiddleOfBucket_IncludesFirstBucket()
    {
        // from=500 (inside bucket [0,1000)), to=3000, size=1000
        // Floor(500,1000)=0 → buckets: 0,1000,2000
        var buckets = TimeBucket.Enumerate(500L, 3000L, 1000L).ToList();
        Assert.Equal(3, buckets.Count);
        Assert.Equal(0L, buckets[0]);
        Assert.Equal(2000L, buckets[2]);
    }

    [Fact]
    public void Enumerate_ToExclusiveAtBucketBoundary_ExcludesThatBucket()
    {
        // [0, 3000) with 1000ms buckets → buckets: 0,1000,2000 (NOT 3000)
        var buckets = TimeBucket.Enumerate(0L, 3000L, 1000L).ToList();
        Assert.Equal(3, buckets.Count);
        Assert.DoesNotContain(3000L, buckets);
    }

    [Fact]
    public void Enumerate_EmptyRange_ReturnsEmpty()
    {
        var buckets = TimeBucket.Enumerate(1000L, 500L, 100L).ToList();
        Assert.Empty(buckets);
    }

    // ── bucketSizeMs <= 0 抛异常 ─────────────────────────────────────────────

    [Fact]
    public void Floor_ZeroBucketSize_ThrowsArgumentOutOfRangeException()
        => Assert.Throws<ArgumentOutOfRangeException>(() => TimeBucket.Floor(0L, 0L));

    [Fact]
    public void Floor_NegativeBucketSize_ThrowsArgumentOutOfRangeException()
        => Assert.Throws<ArgumentOutOfRangeException>(() => TimeBucket.Floor(0L, -1L));

    [Fact]
    public void Range_ZeroBucketSize_ThrowsArgumentOutOfRangeException()
        => Assert.Throws<ArgumentOutOfRangeException>(() => TimeBucket.Range(0L, 0L));

    [Fact]
    public void Enumerate_ZeroBucketSize_ThrowsArgumentOutOfRangeException()
        => Assert.Throws<ArgumentOutOfRangeException>(() => TimeBucket.Enumerate(0L, 1000L, 0L).ToList());

    [Fact]
    public void Enumerate_NegativeBucketSize_ThrowsArgumentOutOfRangeException()
        => Assert.Throws<ArgumentOutOfRangeException>(() => TimeBucket.Enumerate(0L, 1000L, -100L).ToList());
}
