using System.Buffers.Binary;
using System.Numerics;
using SonnetDB.Engine;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Query;
using SonnetDB.Storage.Format;
using SonnetDB.Storage.Segments;
using Xunit;

namespace SonnetDB.Core.Tests.Query;

public sealed class NumericAggregateVectorTests : IDisposable
{
    private readonly string _tempDir;

    public NumericAggregateVectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sndb-simd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Aggregate_Float64Simd_MatchesScalarResult()
    {
        double[] values = Enumerable.Range(0, 257)
            .Select(i => (i % 29 - 14) * 0.5d)
            .ToArray();
        byte[] payload = BuildDoublePayload(values);

        var scalar = NumericAggregateVector.AggregateScalar(FieldType.Float64, payload, 3, values.Length - 5);
        var accelerated = NumericAggregateVector.Aggregate(FieldType.Float64, payload, 3, values.Length - 5, useSimd: true);

        Assert.Equal(scalar.Count, accelerated.Count);
        Assert.Equal(scalar.Sum, accelerated.Sum, precision: 10);
        Assert.Equal(scalar.Min, accelerated.Min);
        Assert.Equal(scalar.Max, accelerated.Max);

        if (NumericAggregateVector.IsSupported && values.Length >= Vector<double>.Count)
            Assert.True(NumericAggregateVector.TryAggregateSimd(FieldType.Float64, payload, 3, values.Length - 5, out _));
    }

    [Fact]
    public void Aggregate_Int64Simd_MatchesScalarResult()
    {
        long[] values = Enumerable.Range(0, 513)
            .Select(i => (long)((i % 37 - 18) * 1000 + i))
            .ToArray();
        byte[] payload = BuildInt64Payload(values);

        var scalar = NumericAggregateVector.AggregateScalar(FieldType.Int64, payload, 7, values.Length - 11);
        var accelerated = NumericAggregateVector.Aggregate(FieldType.Int64, payload, 7, values.Length - 11, useSimd: true);

        Assert.Equal(scalar.Count, accelerated.Count);
        Assert.Equal(scalar.Sum, accelerated.Sum);
        Assert.Equal(scalar.Min, accelerated.Min);
        Assert.Equal(scalar.Max, accelerated.Max);

        if (NumericAggregateVector.IsSupported && values.Length >= Vector<long>.Count)
            Assert.True(NumericAggregateVector.TryAggregateSimd(FieldType.Int64, payload, 7, values.Length - 11, out _));
    }

    [Fact]
    public void Aggregate_Float64WithNaN_FallsBackToScalarSemantics()
    {
        double[] values = [1.0d, 2.0d, double.NaN, -4.0d, 8.0d, 16.0d, 32.0d, 64.0d];
        byte[] payload = BuildDoublePayload(values);

        var scalar = NumericAggregateVector.AggregateScalar(FieldType.Float64, payload, 0, values.Length);
        var accelerated = NumericAggregateVector.Aggregate(FieldType.Float64, payload, 0, values.Length, useSimd: true);

        Assert.False(NumericAggregateVector.TryAggregateSimd(FieldType.Float64, payload, 0, values.Length, out _));
        Assert.Equal(scalar.Count, accelerated.Count);
        Assert.True(double.IsNaN(scalar.Sum));
        Assert.True(double.IsNaN(accelerated.Sum));
        Assert.Equal(scalar.Min, accelerated.Min);
        Assert.Equal(scalar.Max, accelerated.Max);
    }

    [Fact]
    public void QueryEngine_PartialRange_WithSimdOption_MatchesScalarOption()
    {
        string scalarRoot = Path.Combine(_tempDir, "scalar");
        string simdRoot = Path.Combine(_tempDir, "simd");

        using var scalarDb = OpenDb(scalarRoot, useSimd: false);
        using var simdDb = OpenDb(simdRoot, useSimd: true);

        for (int i = 0; i < 256; i++)
        {
            var point = MakePoint(i, (i % 41 - 20) * 0.25d);
            scalarDb.Write(point);
            simdDb.Write(point);
        }

        Assert.NotNull(scalarDb.FlushNow());
        Assert.NotNull(simdDb.FlushNow());

        var range = new TimeRange(17L, 231L);
        foreach (var aggregator in new[] { Aggregator.Sum, Aggregator.Min, Aggregator.Max, Aggregator.Count })
        {
            var scalar = scalarDb.Query.Execute(new AggregateQuery(SeriesId(scalarDb), "v", range, aggregator)).Single();
            var accelerated = simdDb.Query.Execute(new AggregateQuery(SeriesId(simdDb), "v", range, aggregator)).Single();

            Assert.Equal(scalar.Count, accelerated.Count);
            Assert.Equal(scalar.Value, accelerated.Value, precision: 10);
        }
    }

    private static Tsdb OpenDb(string root, bool useSimd)
    {
        Directory.CreateDirectory(root);
        return Tsdb.Open(new TsdbOptions
        {
            RootDirectory = root,
            BackgroundFlush = new BackgroundFlushOptions { Enabled = false },
            Compaction = new SonnetDB.Engine.Compaction.CompactionPolicy { Enabled = false },
            SegmentWriterOptions = new SegmentWriterOptions { FsyncOnCommit = false },
            FlushPolicy = new MemTableFlushPolicy { MaxPoints = 1_000_000, MaxBytes = 64 * 1024 * 1024 },
            UseSimdNumericAggregates = useSimd,
        });
    }

    private static Point MakePoint(long timestamp, double value)
    {
        return Point.Create(
            "m",
            timestamp,
            new Dictionary<string, string> { ["host"] = "h1" },
            new Dictionary<string, FieldValue> { ["v"] = FieldValue.FromDouble(value) });
    }

    private static ulong SeriesId(Tsdb db)
        => db.Catalog.Find("m", new Dictionary<string, string> { ["host"] = "h1" }).Single().Id;

    private static byte[] BuildDoublePayload(IReadOnlyList<double> values)
    {
        var bytes = new byte[values.Count * 8];
        for (int i = 0; i < values.Count; i++)
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(i * 8, 8), values[i]);
        return bytes;
    }

    private static byte[] BuildInt64Payload(IReadOnlyList<long> values)
    {
        var bytes = new byte[values.Count * 8];
        for (int i = 0; i < values.Count; i++)
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(i * 8, 8), values[i]);
        return bytes;
    }
}
