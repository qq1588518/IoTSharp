using SonnetDB.Catalog;
using SonnetDB.Query;
using SonnetDB.Query.Functions;
using SonnetDB.Query.Functions.Aggregates;
using SonnetDB.Sql.Ast;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Query.Functions;

public sealed class ExtendedAggregateAccumulatorTests
{
    private static readonly MeasurementSchema _schema = MeasurementSchema.Create(
        "cpu",
        new[]
        {
            new MeasurementColumn("host", MeasurementColumnRole.Tag, FieldType.String),
            new MeasurementColumn("usage", MeasurementColumnRole.Field, FieldType.Float64),
            new MeasurementColumn("label", MeasurementColumnRole.Field, FieldType.String),
            new MeasurementColumn("embedding", MeasurementColumnRole.Field, FieldType.Vector, 3),
        });

    [Fact]
    public void Welford_Stddev_MatchesNumPyReference()
    {
        var values = new[] { 2.0, 4.0, 4.0, 4.0, 5.0, 5.0, 7.0, 9.0 };
        var w = new WelfordAccumulator();
        foreach (var v in values) w.Add(v);

        // 样本方差 = 32/7 = 4.571428...; 样本标准差 = 2.13808...
        Assert.Equal(4.0 / 1.0 * 8.0 / 7.0, w.SampleVariance, precision: 9);
        Assert.Equal(Math.Sqrt(w.SampleVariance), w.SampleStdDev);
    }

    [Fact]
    public void Welford_MergeIsAssociative()
    {
        var values = new[] { 1.0, 2.5, 3.7, 4.2, 5.9, 6.1, 7.8, 8.4, 9.0, 10.2 };
        var single = new WelfordAccumulator();
        foreach (var v in values) single.Add(v);

        var a = new WelfordAccumulator();
        var b = new WelfordAccumulator();
        for (int i = 0; i < 4; i++) a.Add(values[i]);
        for (int i = 4; i < values.Length; i++) b.Add(values[i]);
        a.Merge(b);

        Assert.Equal(single.Count, a.Count);
        Assert.Equal(single.Mean, a.Mean, precision: 12);
        Assert.Equal(single.M2, a.M2, precision: 9);
    }

    [Fact]
    public void StddevAccumulator_BelowTwoPoints_ReturnsNull()
    {
        var acc = new StddevAccumulator();
        Assert.Null(acc.Finalize());
        acc.Add(42);
        Assert.Null(acc.Finalize());
        acc.Add(50);
        Assert.NotNull(acc.Finalize());
    }

    [Fact]
    public void SpreadAccumulator_ReportsMaxMinusMin()
    {
        var acc = new SpreadAccumulator();
        foreach (var v in new[] { 3.0, 1.0, 7.0, 4.0, 9.0, 2.0 }) acc.Add(v);
        Assert.Equal(8.0, (double)acc.Finalize()!);
    }

    [Fact]
    public void SpreadAccumulator_Merge_PicksGlobalMinMax()
    {
        var a = new SpreadAccumulator();
        var b = new SpreadAccumulator();
        foreach (var v in new[] { 5.0, 6.0, 7.0 }) a.Add(v);
        foreach (var v in new[] { 1.0, 9.0 }) b.Add(v);
        a.Merge(b);
        Assert.Equal(8.0, (double)a.Finalize()!);
        Assert.Equal(5L, a.Count);
    }

    [Fact]
    public void ModeAccumulator_ReturnsSmallestOnTie()
    {
        var acc = new ModeAccumulator();
        foreach (var v in new[] { 3.0, 1.0, 1.0, 3.0, 2.0 }) acc.Add(v);
        Assert.Equal(1.0, (double)acc.Finalize()!);
    }

    [Fact]
    public void TDigest_Quantile_ApproximatesPercentile()
    {
        var rng = new Random(42);
        var digest = new TDigest(compression: 200);
        var samples = new double[10_000];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = rng.NextDouble() * 1000;
            digest.Add(samples[i]);
        }

        Array.Sort(samples);
        double TruePercentile(double q) => samples[(int)(q * samples.Length)];

        // 1000 范围、compression=200 的 merging digest 在常用分位点上的绝对误差应 < 1%。
        Assert.Equal(TruePercentile(0.50), digest.Quantile(0.50), tolerance: 10.0);
        Assert.Equal(TruePercentile(0.90), digest.Quantile(0.90), tolerance: 10.0);
        Assert.Equal(TruePercentile(0.99), digest.Quantile(0.99), tolerance: 10.0);
    }

    [Fact]
    public void TDigest_Merge_ProducesSimilarQuantiles()
    {
        var rng = new Random(123);
        var combined = new TDigest(compression: 200);
        var part1 = new TDigest(compression: 200);
        var part2 = new TDigest(compression: 200);
        for (int i = 0; i < 5000; i++)
        {
            double v = rng.NextDouble() * 100;
            combined.Add(v);
            (i % 2 == 0 ? part1 : part2).Add(v);
        }
        part1.Merge(part2);

        // 合并后的 p95 应与单独构建的 digest 接近（容忍 2.0 的绝对误差）
        Assert.Equal(combined.Quantile(0.95), part1.Quantile(0.95), tolerance: 2.0);
    }

    [Fact]
    public void HyperLogLog_EstimatesDistinctCount_WithinErrorBound()
    {
        var hll = new HyperLogLog();
        const int distinct = 100_000;
        for (int i = 0; i < distinct; i++)
            hll.Add(i);

        long est = hll.Estimate();
        double error = Math.Abs(est - distinct) / (double)distinct;
        Assert.InRange(error, 0.0, 0.03); // 3% 上限（理论 σ ≈ 0.81%）
    }

    [Fact]
    public void HyperLogLog_Merge_EquivalentToSingle()
    {
        var single = new HyperLogLog();
        var a = new HyperLogLog();
        var b = new HyperLogLog();
        for (int i = 0; i < 50_000; i++)
        {
            single.Add(i);
            (i % 2 == 0 ? a : b).Add(i);
        }
        a.Merge(b);
        // 两路并行合并应得到与单路相同的结果（确定性算法）。
        Assert.Equal(single.Estimate(), a.Estimate());
    }

    [Fact]
    public void HistogramAccumulator_ProducesJsonBins()
    {
        var acc = new HistogramAccumulator(binWidth: 10);
        foreach (var v in new[] { 1.0, 5.0, 12.0, 18.0, 22.0, 25.0, 30.5 })
            acc.Add(v);
        var json = (string)acc.Finalize()!;
        Assert.Contains("[0,10)", json);
        Assert.Contains("[10,20)", json);
        Assert.Contains("[20,30)", json);
        Assert.Contains("[30,40)", json);
    }

    [Fact]
    public void CentroidAccumulator_Merge_ProducesPerDimensionMean()
    {
        var a = new CentroidAccumulator();
        var b = new CentroidAccumulator();
        a.Add(new float[] { 1, 2, 3 });
        a.Add(new float[] { 3, 4, 5 });
        b.Add(new float[] { 5, 6, 7 });
        a.Merge(b);

        var centroid = Assert.IsType<float[]>(a.Finalize());
        Assert.Equal(new[] { 3f, 4f, 5f }, centroid);
        Assert.Equal(3L, a.Count);
    }

    [Fact]
    public void CentroidFunction_RejectsNonVectorField()
    {
        var fn = new CentroidFunction();
        var call = new FunctionCallExpression(
            "centroid",
            new SqlExpression[] { new IdentifierExpression("usage") });
        Assert.Throws<InvalidOperationException>(() => fn.ResolveFieldName(call, _schema));
    }

    [Fact]
    public void PercentileFunction_RejectsOutOfRangeQ()
    {
        var fn = new PercentileFunction();
        var call = new FunctionCallExpression(
            "percentile",
            new SqlExpression[] { new IdentifierExpression("usage"), LiteralExpression.Integer(150) });
        Assert.Throws<InvalidOperationException>(() => fn.CreateAccumulator(call, _schema));
    }

    [Fact]
    public void HistogramFunction_RejectsNonPositiveBinWidth()
    {
        var fn = new HistogramFunction();
        var call = new FunctionCallExpression(
            "histogram",
            new SqlExpression[] { new IdentifierExpression("usage"), LiteralExpression.Float(0) });
        Assert.Throws<InvalidOperationException>(() => fn.CreateAccumulator(call, _schema));
    }

    [Fact]
    public void FunctionRegistry_RegistersAllExtendedAggregates()
    {
        foreach (var name in new[]
                 {
                      "stddev", "variance", "spread", "mode",
                      "median", "percentile", "p50", "p90", "p95", "p99",
                      "tdigest_agg", "distinct_count", "histogram", "centroid",
                  })
        {
            Assert.True(FunctionRegistry.TryGetAggregate(name, out var fn),
                $"未注册扩展聚合 '{name}'。");
            Assert.Null(fn.LegacyAggregator);
        }
    }
}
