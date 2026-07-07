using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using SonnetDB.Model;
using SonnetDB.Storage.Format;
using SonnetDB.Storage.Segments;

namespace SonnetDB.Benchmarks.Benchmarks;

[Config(typeof(CodecSpecializationBenchmarkConfig))]
[MemoryDiagnoser]
[BenchmarkCategory("CodecSpecialization")]
public class CodecSpecializationBenchmark
{
    private const long StartTimestampMs = 1_700_000_000_000L;

    private byte[] _timestampPayload = [];
    private byte[] _valuePayload = [];
    private BlockDescriptor _descriptor;
    private long _rangeFrom;
    private long _rangeTo;

    [Params(16_384)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var points = new DataPoint[Count];
        var timestamps = new long[Count];
        long timestamp = StartTimestampMs;

        for (int i = 0; i < Count; i++)
        {
            timestamp += 1_000L + (i % 17 == 0 ? 1L : 0L);
            timestamps[i] = timestamp;
            double value = 40.0d + Math.Sin(i * 0.013d) + (i % 31) * 0.001d;
            points[i] = new DataPoint(timestamp, FieldValue.FromDouble(value));
        }
        _rangeFrom = timestamps[Count / 4];
        _rangeTo = timestamps[(Count / 4) + (Count / 8) - 1];

        _timestampPayload = new byte[TimestampCodec.MeasureDeltaOfDelta(timestamps)];
        TimestampCodec.WriteDeltaOfDelta(timestamps, _timestampPayload);

        _valuePayload = new byte[ValuePayloadCodecV2.Measure(FieldType.Float64, points)];
        ValuePayloadCodecV2.Write(FieldType.Float64, points, _valuePayload);

        _descriptor = new BlockDescriptor
        {
            Count = Count,
            FieldType = FieldType.Float64,
            TimestampEncoding = BlockEncoding.DeltaTimestamp,
            ValueEncoding = BlockEncoding.DeltaValue,
            FieldName = "value",
        };
    }

    [Benchmark(Baseline = true, Description = "V2 composable timestamp/value decode")]
    public double V2_ComposableDecode()
        => Checksum(DecodeComposable());

    [Benchmark(Description = "BlockDecoder.Decode V2")]
    public double BlockDecoder_Decode()
        => Checksum(BlockDecoder.Decode(_descriptor, _timestampPayload, _valuePayload));

    [Benchmark(Description = "V2 composable range decode")]
    public double V2_ComposableDecodeRange()
        => Checksum(DecodeRangeComposable());

    [Benchmark(Description = "BlockDecoder.DecodeRange V2")]
    public double BlockDecoder_DecodeRange()
        => Checksum(BlockDecoder.DecodeRange(_descriptor, _timestampPayload, _valuePayload, _rangeFrom, _rangeTo));

    private DataPoint[] DecodeComposable()
    {
        var result = new DataPoint[Count];
        long[] rented = ArrayPool<long>.Shared.Rent(Count);
        try
        {
            Span<long> timestamps = rented.AsSpan(0, Count);
            TimestampCodec.ReadDeltaOfDelta(_timestampPayload, timestamps);
            for (int i = 0; i < Count; i++)
                result[i] = new DataPoint(timestamps[i], default);
        }
        finally
        {
            ArrayPool<long>.Shared.Return(rented);
        }

        var values = ValuePayloadCodecV2.Decode(FieldType.Float64, _valuePayload, Count);
        for (int i = 0; i < Count; i++)
            result[i] = new DataPoint(result[i].Timestamp, values[i]);

        return result;
    }

    private DataPoint[] DecodeRangeComposable()
    {
        long[] rented = ArrayPool<long>.Shared.Rent(Count);
        try
        {
            Span<long> timestamps = rented.AsSpan(0, Count);
            TimestampCodec.ReadDeltaOfDelta(_timestampPayload, timestamps);

            int start = LowerBound(timestamps, _rangeFrom);
            int end = UpperBound(timestamps, _rangeTo);
            int rangeCount = end - start;
            var result = new DataPoint[rangeCount];
            for (int i = 0; i < rangeCount; i++)
                result[i] = new DataPoint(timestamps[start + i], default);

            var values = ValuePayloadCodecV2.Decode(FieldType.Float64, _valuePayload, Count);
            for (int i = 0; i < rangeCount; i++)
                result[i] = new DataPoint(result[i].Timestamp, values[start + i]);
            return result;
        }
        finally
        {
            ArrayPool<long>.Shared.Return(rented);
        }
    }

    private static int LowerBound(ReadOnlySpan<long> timestamps, long value)
    {
        int lo = 0, hi = timestamps.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (timestamps[mid] < value)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    private static int UpperBound(ReadOnlySpan<long> timestamps, long value)
    {
        int lo = 0, hi = timestamps.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (timestamps[mid] <= value)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    private static double Checksum(ReadOnlySpan<DataPoint> points)
    {
        double checksum = points.Length;
        for (int i = 0; i < points.Length; i += 1024)
        {
            checksum += points[i].Timestamp * 0.000001d;
            checksum += points[i].Value.AsDouble();
        }
        return checksum;
    }

    private sealed class CodecSpecializationBenchmarkConfig : ManualConfig
    {
        public CodecSpecializationBenchmarkConfig()
        {
            AddJob(Job.ShortRun.WithId("CodecSpecializationShortRun"));
        }
    }
}
