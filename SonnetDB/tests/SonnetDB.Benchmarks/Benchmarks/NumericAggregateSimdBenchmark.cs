using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;
using SonnetDB.Query;
using SonnetDB.Storage.Format;

namespace SonnetDB.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("NumericAggregateSimd")]
public class NumericAggregateSimdBenchmark
{
    private byte[] _float64Payload = [];
    private byte[] _int64Payload = [];

    [Params(1_000_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _float64Payload = new byte[Count * 8];
        _int64Payload = new byte[Count * 8];

        for (int i = 0; i < Count; i++)
        {
            double doubleValue = (i % 1024) * 0.125d - 64d;
            long longValue = (i % 4096) - 2048L;
            BinaryPrimitives.WriteDoubleLittleEndian(_float64Payload.AsSpan(i * 8, 8), doubleValue);
            BinaryPrimitives.WriteInt64LittleEndian(_int64Payload.AsSpan(i * 8, 8), longValue);
        }
    }

    [Benchmark(Baseline = true, Description = "Float64 scalar sum/min/max/count")]
    public double Float64Scalar()
        => Checksum(NumericAggregateVector.AggregateScalar(
            FieldType.Float64,
            _float64Payload,
            0,
            Count));

    [Benchmark(Description = "Float64 SIMD sum/min/max/count")]
    public double Float64Simd()
        => Checksum(NumericAggregateVector.Aggregate(
            FieldType.Float64,
            _float64Payload,
            0,
            Count,
            useSimd: true));

    [Benchmark(Description = "Int64 scalar sum/min/max/count")]
    public double Int64Scalar()
        => Checksum(NumericAggregateVector.AggregateScalar(
            FieldType.Int64,
            _int64Payload,
            0,
            Count));

    [Benchmark(Description = "Int64 SIMD sum/min/max/count")]
    public double Int64Simd()
        => Checksum(NumericAggregateVector.Aggregate(
            FieldType.Int64,
            _int64Payload,
            0,
            Count,
            useSimd: true));

    private static double Checksum(NumericAggregateVectorResult result)
        => result.Sum + result.Min + result.Max + result.Count;
}
