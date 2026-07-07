using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using SonnetDB.Engine;
using SonnetDB.Engine.Compaction;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Query;
using SonnetDB.Sql.Execution;
using SonnetDB.Storage.Segments;

namespace SonnetDB.Benchmarks.Benchmarks;

/// <summary>
/// Milestone 15 PR #77：地理空间查询与轨迹聚合基准。
/// 默认覆盖 100k 轨迹点；如需启用 1M，请设置 <c>SONNETDB_GEO_BENCH_INCLUDE_1M=1</c>。
/// </summary>
[Config(typeof(GeoQueryConfig))]
[MemoryDiagnoser]
[BenchmarkCategory("Geo")]
public class GeoQueryBenchmark
{
    private const string Measurement = "vehicle";
    private const string PositionField = "position";
    private const string SpeedField = "speed";
    private const long StartTimestampMs = 1_700_000_000_000;
    private const double CenterLat = 31.2304;
    private const double CenterLon = 121.4737;
    private const double BBoxMinLat = 31.21;
    private const double BBoxMinLon = 121.45;
    private const double BBoxMaxLat = 31.25;
    private const double BBoxMaxLon = 121.50;
    private const int DeviceCount = 16;

    private Tsdb? _db;
    private string _rootDirectory = string.Empty;
    private ulong _firstSeriesId;
    private long _queryFrom;
    private long _queryTo;

    /// <summary>轨迹点数量。</summary>
    [ParamsSource(nameof(GetPointCounts))]
    public int PointCount { get; set; }

    /// <summary>返回默认与可选大规模数据集。</summary>
    public IEnumerable<int> GetPointCounts()
    {
        yield return 100_000;
        if (ShouldIncludeOneMillion())
            yield return 1_000_000;
    }

    /// <summary>生成轨迹点并落盘成 segment，使 geohash block 剪枝参与查询。</summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "sonnetdb-geo-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDirectory);
        _db = Tsdb.Open(CreateOptions(_rootDirectory));
        SqlExecutor.Execute(_db,
            "CREATE MEASUREMENT vehicle (device TAG, region TAG, position FIELD GEOPOINT, speed FIELD FLOAT)");

        var points = GeneratePoints(PointCount);
        _db.WriteMany(points);
        _db.FlushNow();

        _firstSeriesId = SeriesId.Compute(new SeriesKey(Measurement,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["device"] = "car_00",
                ["region"] = "shanghai",
            }));
        _queryFrom = StartTimestampMs;
        _queryTo = StartTimestampMs + PointCount;
    }

    /// <summary>清理临时数据库。</summary>
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _db?.Dispose();
        if (Directory.Exists(_rootDirectory))
            Directory.Delete(_rootDirectory, recursive: true);
    }

    /// <summary>执行圆形地理围栏过滤，典型用于车辆 / IoT 近邻查询。</summary>
    [Benchmark(Description = "SonnetDB geo_within 过滤")]
    public int GeoWithinFilter()
    {
        var result = ExecuteSelect(
            "SELECT time, position FROM vehicle " +
            "WHERE geo_within(position, 31.2304, 121.4737, 1500)");
        return result.Rows.Count;
    }

    /// <summary>执行矩形地理围栏聚合，触发 PR #76 geohash block 剪枝路径。</summary>
    [Benchmark(Description = "SonnetDB geo_bbox + count")]
    public double GeoBboxCount()
    {
        var result = ExecuteSelect(
            "SELECT count(position) FROM vehicle " +
            "WHERE geo_bbox(position, 31.21, 121.45, 31.25, 121.50)");
        return Convert.ToDouble(result.Rows[0][0]);
    }

    /// <summary>对单设备轨迹做总路程聚合。</summary>
    [Benchmark(Description = "SonnetDB trajectory_length SQL")]
    public double TrajectoryLengthSql()
    {
        var result = ExecuteSelect(
            "SELECT trajectory_length(position) FROM vehicle WHERE device = 'car_00'");
        return Convert.ToDouble(result.Rows[0][0]);
    }

    /// <summary>通过 QueryEngine 直接范围扫描单设备 GEOPOINT 字段，作为 SQL 聚合之外的基础路径参考。</summary>
    [Benchmark(Description = "SonnetDB GEOPOINT range scan")]
    public int GeoPointRangeScan()
    {
        int count = 0;
        foreach (var point in Db.Query.Execute(new PointQuery(_firstSeriesId, PositionField, new TimeRange(_queryFrom, _queryTo))))
        {
            if (point.Value.Type == Storage.Format.FieldType.GeoPoint)
                count++;
        }
        return count;
    }

    private SelectExecutionResult ExecuteSelect(string sql)
        => (SelectExecutionResult)SqlExecutor.Execute(Db, sql)!;

    private Tsdb Db => _db ?? throw new InvalidOperationException("Benchmark database is not initialized.");

    private static Point[] GeneratePoints(int pointCount)
    {
        var points = new Point[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            int deviceIndex = i % DeviceCount;
            double routeStep = i / (double)Math.Max(1, pointCount - 1);
            double phase = (i / (double)DeviceCount) * 0.017;
            double lat = CenterLat + Math.Sin(phase + deviceIndex * 0.37) * 0.08 + (routeStep - 0.5) * 0.03;
            double lon = CenterLon + Math.Cos(phase * 0.8 + deviceIndex * 0.23) * 0.08 + (routeStep - 0.5) * 0.04;
            double speed = 8 + (deviceIndex % 5) * 1.5 + Math.Sin(phase) * 2;
            string device = FormattableString.Invariant($"car_{deviceIndex:00}");
            points[i] = Point.Create(
                Measurement,
                StartTimestampMs + i,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["device"] = device,
                    ["region"] = "shanghai",
                },
                new Dictionary<string, FieldValue>(StringComparer.Ordinal)
                {
                    [PositionField] = FieldValue.FromGeoPoint(lat, lon),
                    [SpeedField] = FieldValue.FromDouble(speed),
                });
        }
        return points;
    }

    private static TsdbOptions CreateOptions(string rootDirectory) => new()
    {
        RootDirectory = rootDirectory,
        FlushPolicy = new MemTableFlushPolicy
        {
            MaxBytes = long.MaxValue,
            MaxPoints = long.MaxValue,
            MaxAge = TimeSpan.FromDays(1),
        },
        SegmentWriterOptions = new SegmentWriterOptions
        {
            FsyncOnCommit = false,
        },
        BackgroundFlush = new BackgroundFlushOptions { Enabled = false },
        Compaction = new CompactionPolicy { Enabled = false },
    };

    private static bool ShouldIncludeOneMillion()
        => string.Equals(
            Environment.GetEnvironmentVariable("SONNETDB_GEO_BENCH_INCLUDE_1M"),
            "1",
            StringComparison.OrdinalIgnoreCase);
}

internal sealed class GeoQueryConfig : ManualConfig
{
    public GeoQueryConfig()
    {
        AddJob(Job.ShortRun.WithId("GeoShortRun"));
    }
}



