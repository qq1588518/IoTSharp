using SonnetDB.Engine;
using SonnetDB.Sql;
using SonnetDB.Sql.Execution;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Sql;

/// <summary>
/// PR #52 — Tier 2 扩展聚合（stddev / variance / spread / mode / median / percentile / pXX /
/// distinct_count / tdigest_agg / histogram）的 SQL 端到端集成测试。
/// </summary>
public sealed class SqlExecutorExtendedAggregateTests : IDisposable
{
    private readonly string _root;

    public SqlExecutorExtendedAggregateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sndb-extagg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private TsdbOptions Options() => new() { RootDirectory = _root };

    private TsdbOptions ManualFlushOptions() => new()
    {
        RootDirectory = _root,
        BackgroundFlush = new BackgroundFlushOptions { Enabled = false },
        Compaction = new SonnetDB.Engine.Compaction.CompactionPolicy { Enabled = false },
        SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
    };

    private static Tsdb OpenWithSchema(TsdbOptions options)
    {
        var db = Tsdb.Open(options);
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT cpu (host TAG, usage FIELD FLOAT)");
        return db;
    }

    private static SelectExecutionResult Select(Tsdb db, string sql)
        => Assert.IsType<SelectExecutionResult>(SqlExecutor.Execute(db, sql));

    private static void Seed(Tsdb db, params double[] values)
    {
        var lines = new List<string>(values.Length);
        for (int i = 0; i < values.Length; i++)
            lines.Add($"({i + 1}, 'h1', {values[i].ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, usage) VALUES " + string.Join(", ", lines));
    }

    [Fact]
    public void Stddev_AndVariance_MatchReferenceFormulas()
    {
        using var db = OpenWithSchema(Options());
        Seed(db, 2, 4, 4, 4, 5, 5, 7, 9);

        var r = Select(db, "SELECT stddev(usage), variance(usage) FROM cpu");

        Assert.Single(r.Rows);
        // 样本方差 = M2 / (n-1)；M2 = Σ(xᵢ-μ)²。μ = 5。
        // 离差 = (-3)²+(-1)²+(-1)²+(-1)²+0+0+2²+4² = 9+1+1+1+0+0+4+16 = 32; 方差=32/7≈4.5714286
        Assert.Equal(4.5714286, (double)r.Rows[0][1]!, precision: 4);
        Assert.Equal(Math.Sqrt(4.5714286), (double)r.Rows[0][0]!, precision: 4);
    }

    [Fact]
    public void Spread_ReturnsMaxMinusMin()
    {
        using var db = OpenWithSchema(Options());
        Seed(db, 3, 1, 7, 4, 9, 2);

        var r = Select(db, "SELECT spread(usage) FROM cpu");
        Assert.Equal(8.0, (double)r.Rows[0][0]!);
    }

    [Fact]
    public void Mode_ReturnsSmallestValueOnTie()
    {
        using var db = OpenWithSchema(Options());
        Seed(db, 3, 1, 1, 3, 2);

        var r = Select(db, "SELECT mode(usage) FROM cpu");
        Assert.Equal(1.0, (double)r.Rows[0][0]!);
    }

    [Fact]
    public void Median_AndP50_AreEquivalent()
    {
        using var db = OpenWithSchema(Options());
        Seed(db, 1, 2, 3, 4, 5, 6, 7, 8, 9);

        var r = Select(db, "SELECT median(usage), p50(usage) FROM cpu");
        double median = (double)r.Rows[0][0]!;
        double p50 = (double)r.Rows[0][1]!;
        Assert.Equal(median, p50, precision: 6);
        Assert.InRange(median, 4.5, 5.5); // 真值=5
    }

    [Fact]
    public void Percentile_WithExplicitQ_MatchesPXX()
    {
        using var db = OpenWithSchema(Options());
        var values = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();
        Seed(db, values);

        var r = Select(db, "SELECT percentile(usage, 95), p95(usage) FROM cpu");
        double p1 = (double)r.Rows[0][0]!;
        double p2 = (double)r.Rows[0][1]!;
        Assert.Equal(p1, p2, precision: 6);
        Assert.InRange(p1, 90, 99);
    }

    [Fact]
    public void DistinctCount_ApproximatesCardinality()
    {
        using var db = OpenWithSchema(Options());
        var values = Enumerable.Range(0, 1000)
            .Select(i => (double)(i % 250)) // 250 个不同值，每个出现 4 次
            .ToArray();
        Seed(db, values);

        var r = Select(db, "SELECT distinct_count(usage) FROM cpu");
        long est = (long)r.Rows[0][0]!;
        Assert.InRange(est, 240, 260); // ±4% 容忍
    }

    [Fact]
    public void PercentileAndDistinctCount_AfterFlush_UseEmbeddedAggregateSketch()
    {
        using var db = OpenWithSchema(ManualFlushOptions());
        var values = Enumerable.Range(0, 200)
            .Select(i => (double)(i % 50))
            .ToArray();
        Seed(db, values);

        var flush = db.FlushNow();
        Assert.NotNull(flush);
        Assert.False(File.Exists(TsdbPaths.AggregateIndexPath(_root, flush.SegmentId)));

        var reader = Assert.Single(db.Segments.Readers);
        Assert.False(reader.AggregateSketchOffsetsLoaded);

        var r = Select(db, "SELECT percentile(usage, 95), p95(usage), distinct_count(usage) FROM cpu");

        Assert.Single(r.Rows);
        Assert.Equal((double)r.Rows[0][0]!, (double)r.Rows[0][1]!, precision: 6);
        Assert.InRange((double)r.Rows[0][0]!, 45d, 49d);
        Assert.InRange((long)r.Rows[0][2]!, 48L, 52L);
        Assert.True(reader.AggregateSketchOffsetsLoaded);
        Assert.True(reader.AggregateSketchOffsetsEmbedded);
    }

    [Fact]
    public void PercentileAndDistinctCount_WithoutExternalSidecar_StillUseEmbeddedSketch()
    {
        using var db = OpenWithSchema(ManualFlushOptions());
        var values = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();
        Seed(db, values);

        var flush = db.FlushNow();
        Assert.NotNull(flush);
        File.Delete(TsdbPaths.AggregateIndexPath(_root, flush.SegmentId));

        var r = Select(db, "SELECT p95(usage), distinct_count(usage) FROM cpu");

        Assert.Single(r.Rows);
        Assert.InRange((double)r.Rows[0][0]!, 90d, 99d);
        Assert.InRange((long)r.Rows[0][1]!, 98L, 102L);
        var reader = Assert.Single(db.Segments.Readers);
        Assert.True(reader.AggregateSketchOffsetsLoaded);
        Assert.True(reader.AggregateSketchOffsetsEmbedded);
    }

    [Fact]
    public void Histogram_ReturnsJsonBins()
    {
        using var db = OpenWithSchema(Options());
        Seed(db, 1, 5, 12, 18, 22, 25, 30.5);

        var r = Select(db, "SELECT histogram(usage, 10) FROM cpu");
        var json = (string)r.Rows[0][0]!;
        Assert.Contains("[0,10)", json);
        Assert.Contains("[10,20)", json);
        Assert.Contains("[20,30)", json);
        Assert.Contains("[30,40)", json);
    }

    [Fact]
    public void TDigestAgg_ReturnsJsonState()
    {
        using var db = OpenWithSchema(Options());
        Seed(db, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);

        var r = Select(db, "SELECT tdigest_agg(usage) FROM cpu");
        var json = (string)r.Rows[0][0]!;
        Assert.Contains("\"compression\":", json);
        Assert.Contains("\"count\":10", json);
        Assert.Contains("\"centroids\":[", json);
    }

    [Fact]
    public void GroupByTime_WithExtendedAggregates_ProducesPerBucket()
    {
        using var db = OpenWithSchema(Options());
        SqlExecutor.Execute(db,
            "INSERT INTO cpu (time, host, usage) VALUES " +
            "(0, 'h1', 1), (500, 'h1', 3), " +     // bucket [0,1000): values {1,3}
            "(1000, 'h1', 10), (1500, 'h1', 30)"); // bucket [1000,2000): values {10,30}

        var r = Select(db,
            "SELECT spread(usage), stddev(usage) FROM cpu GROUP BY time(1000ms)");

        Assert.Equal(2, r.Rows.Count);
        Assert.Equal(2.0, (double)r.Rows[0][0]!);                    // spread bucket0 = 3-1
        Assert.Equal(20.0, (double)r.Rows[1][0]!);                   // spread bucket1 = 30-10
        Assert.Equal(Math.Sqrt(2.0), (double)r.Rows[0][1]!, 4);      // stddev{1,3} = √2
        Assert.Equal(Math.Sqrt(200.0), (double)r.Rows[1][1]!, 4);    // stddev{10,30} = √200
    }

    [Fact]
    public void MixedLegacyAndExtendedAggregates_CoexistInSameSelect()
    {
        using var db = OpenWithSchema(Options());
        Seed(db, 1, 2, 3, 4, 5);

        var r = Select(db,
            "SELECT count(usage), avg(usage), stddev(usage), spread(usage) FROM cpu");
        Assert.Equal(5L, r.Rows[0][0]);
        Assert.Equal(3.0, (double)r.Rows[0][1]!);
        Assert.Equal(Math.Sqrt(2.5), (double)r.Rows[0][2]!, 6);  // {1..5} 样本方差 = 2.5
        Assert.Equal(4.0, (double)r.Rows[0][3]!);
    }

    [Fact]
    public void Stddev_OnSinglePoint_ReturnsNull()
    {
        using var db = OpenWithSchema(Options());
        Seed(db, 42);

        var r = Select(db, "SELECT stddev(usage) FROM cpu");
        Assert.Null(r.Rows[0][0]);
    }

    [Fact]
    public void Percentile_RejectsOutOfRangeQ()
    {
        using var db = OpenWithSchema(Options());
        Seed(db, 1, 2, 3);

        Assert.Throws<InvalidOperationException>(() =>
            Select(db, "SELECT percentile(usage, 150) FROM cpu"));
    }

    [Fact]
    public void Histogram_RejectsZeroBinWidth()
    {
        using var db = OpenWithSchema(Options());
        Seed(db, 1, 2, 3);

        Assert.Throws<InvalidOperationException>(() =>
            Select(db, "SELECT histogram(usage, 0) FROM cpu"));
    }
}
