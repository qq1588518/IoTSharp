using SonnetDB.Catalog;
using SonnetDB.Engine;
using SonnetDB.Model;
using SonnetDB.Query;
using SonnetDB.Sql.Execution;
using SonnetDB.Storage.Format;
using Xunit;

namespace SonnetDB.Core.Tests.Sql;

/// <summary>
/// Milestone 15 PR #70：GEOPOINT 类型、POINT 字面量与 lat/lon 标量函数测试。
/// </summary>
public sealed class SqlExecutorGeoPointTests : IDisposable
{
    private readonly string _root;

    public SqlExecutorGeoPointTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sndb-geo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private TsdbOptions Options() => new()
    {
        RootDirectory = _root,
        SegmentWriterOptions = new SonnetDB.Storage.Segments.SegmentWriterOptions { FsyncOnCommit = false },
    };

    [Fact]
    public void CreateMeasurement_WithGeoPointColumn_RegistersType()
    {
        using var db = Tsdb.Open(Options());

        var schema = Assert.IsType<MeasurementSchema>(SqlExecutor.Execute(db,
            "CREATE MEASUREMENT vehicle (device TAG, position FIELD GEOPOINT)"));

        var col = schema.TryGetColumn("position")!;
        Assert.Equal(FieldType.GeoPoint, col.DataType);
        Assert.Null(col.VectorDimension);
    }

    [Fact]
    public void Insert_PointLiteral_RoundTripsThroughEngine()
    {
        using var db = Tsdb.Open(Options());
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT vehicle (device TAG, position FIELD GEOPOINT)");
        SqlExecutor.Execute(db,
            "INSERT INTO vehicle (time, device, position) VALUES (1000, 'car-1', POINT(39.9042, 116.4074))");

        var seriesId = SeriesId.Compute(new SeriesKey("vehicle",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["device"] = "car-1" }));
        var points = db.Query.Execute(new PointQuery(seriesId, "position",
            new TimeRange(0, long.MaxValue))).ToList();

        var point = Assert.Single(points);
        Assert.Equal(FieldType.GeoPoint, point.Value.Type);
        Assert.Equal(new GeoPoint(39.9042, 116.4074), point.Value.AsGeoPoint());
    }

    [Fact]
    public void Select_LatLon_ReturnsCoordinates()
    {
        using var db = Tsdb.Open(Options());
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT vehicle (device TAG, position FIELD GEOPOINT)");
        SqlExecutor.Execute(db,
            "INSERT INTO vehicle (time, device, position) VALUES (1000, 'car-1', POINT(31.2304, 121.4737))");

        var result = Assert.IsType<SelectExecutionResult>(SqlExecutor.Execute(db,
            "SELECT lat(position), lon(position) FROM vehicle"));

        var row = Assert.Single(result.Rows);
        Assert.Equal(31.2304, Convert.ToDouble(row[0]), 6);
        Assert.Equal(121.4737, Convert.ToDouble(row[1]), 6);
    }

    [Fact]
    public void Select_GeoScalarFunctions_ReturnExpectedValues()
    {
        using var db = Tsdb.Open(Options());
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT route (device TAG, p1 FIELD GEOPOINT, p2 FIELD GEOPOINT)");
        SqlExecutor.Execute(db,
            "INSERT INTO route (time, device, p1, p2) VALUES " +
            "(1000, 'car-1', POINT(39.9042, 116.4074), POINT(31.2304, 121.4737))");

        var result = Assert.IsType<SelectExecutionResult>(SqlExecutor.Execute(db,
            "SELECT geo_distance(p1, p2), geo_bearing(p1, p2), " +
            "geo_within(p1, 39.9042, 116.4074, 1), " +
            "geo_bbox(p2, 31.0, 121.0, 32.0, 122.0), " +
            "geo_speed(p1, p2, 1000), ST_Distance(p1, p2), ST_DWithin(p1, 39.9042, 116.4074, 1) FROM route"));

        var row = Assert.Single(result.Rows);
        double distance = Convert.ToDouble(row[0]);
        Assert.InRange(distance, 1_060_000d, 1_080_000d);
        Assert.InRange(Convert.ToDouble(row[1]), 145d, 155d);
        Assert.True((bool)row[2]!);
        Assert.True((bool)row[3]!);
        Assert.InRange(Convert.ToDouble(row[4]), 1_060_000d, 1_080_000d);
        Assert.Equal(distance, Convert.ToDouble(row[5]), 6);
        Assert.True((bool)row[6]!);
    }

    [Fact]
    public void Select_GeoWithinOutsideRadius_ReturnsFalse()
    {
        using var db = Tsdb.Open(Options());
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT vehicle (device TAG, position FIELD GEOPOINT)");
        SqlExecutor.Execute(db,
            "INSERT INTO vehicle (time, device, position) VALUES (1000, 'car-1', POINT(31.2304, 121.4737))");

        var result = Assert.IsType<SelectExecutionResult>(SqlExecutor.Execute(db,
            "SELECT geo_within(position, 39.9042, 116.4074, 1000), ST_Within(position, 39.9042, 116.4074, 1000) FROM vehicle"));

        var row = Assert.Single(result.Rows);
        Assert.False((bool)row[0]!);
        Assert.False((bool)row[1]!);
    }

    [Fact]
    public void Select_WhereGeoWithin_FiltersRowsAfterFlush()
    {
        using var db = Tsdb.Open(Options());
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT vehicle (device TAG, position FIELD GEOPOINT)");
        SqlExecutor.Execute(db,
            "INSERT INTO vehicle (time, device, position) VALUES " +
            "(1000, 'car-1', POINT(39.9042, 116.4074)), " +
            "(2000, 'car-1', POINT(31.2304, 121.4737)), " +
            "(3000, 'car-1', POINT(22.5431, 114.0579))");
        db.FlushNow();

        var result = Assert.IsType<SelectExecutionResult>(SqlExecutor.Execute(db,
            "SELECT time, position FROM vehicle WHERE geo_within(position, 39.9042, 116.4074, 1000)"));

        var row = Assert.Single(result.Rows);
        Assert.Equal(1000L, row[0]);
        Assert.Equal(new GeoPoint(39.9042, 116.4074), Assert.IsType<GeoPoint>(row[1]));
    }

    [Fact]
    public void Select_WhereGeoBbox_FiltersAggregateAfterFlush()
    {
        using var db = Tsdb.Open(Options());
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT vehicle (device TAG, position FIELD GEOPOINT, speed FIELD FLOAT)");
        SqlExecutor.Execute(db,
            "INSERT INTO vehicle (time, device, position, speed) VALUES " +
            "(1000, 'car-1', POINT(39.9042, 116.4074), 10), " +
            "(2000, 'car-1', POINT(31.2304, 121.4737), 20), " +
            "(3000, 'car-1', POINT(22.5431, 114.0579), 30)");
        db.FlushNow();

        var result = Assert.IsType<SelectExecutionResult>(SqlExecutor.Execute(db,
            "SELECT count(position), trajectory_length(position) FROM vehicle WHERE geo_bbox(position, 30, 120, 32, 122)"));

        var row = Assert.Single(result.Rows);
        Assert.Equal(1d, Convert.ToDouble(row[0]));
        Assert.Equal(0d, Convert.ToDouble(row[1]));
    }

    [Fact]
    public void FlushAndReopen_GeoPointSegment_RoundTrips()
    {
        using (var db = Tsdb.Open(Options()))
        {
            SqlExecutor.Execute(db,
                "CREATE MEASUREMENT vehicle (device TAG, position FIELD GEOPOINT)");
            SqlExecutor.Execute(db,
                "INSERT INTO vehicle (time, device, position) VALUES " +
                "(1000, 'car-1', POINT(39.9042, 116.4074)), " +
                "(2000, 'car-1', POINT(31.2304, 121.4737))");
            db.FlushNow();
        }

        using var reopened = Tsdb.Open(Options());
        var seriesId = SeriesId.Compute(new SeriesKey("vehicle",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["device"] = "car-1" }));
        var points = reopened.Query.Execute(new PointQuery(seriesId, "position",
            new TimeRange(0, long.MaxValue))).ToList();

        Assert.Equal(2, points.Count);
        Assert.Equal(new GeoPoint(39.9042, 116.4074), points[0].Value.AsGeoPoint());
        Assert.Equal(new GeoPoint(31.2304, 121.4737), points[1].Value.AsGeoPoint());
    }

    [Fact]
    public void Select_TrajectoryAggregates_ReturnExpectedValues()
    {
        using var db = Tsdb.Open(Options());
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT vehicle (device TAG, position FIELD GEOPOINT)");
        SqlExecutor.Execute(db,
            "INSERT INTO vehicle (time, device, position) VALUES " +
            "(1000, 'car-1', POINT(0, 0)), " +
            "(2000, 'car-1', POINT(0, 0.001)), " +
            "(4000, 'car-1', POINT(0.001, 0.001))");

        var result = Assert.IsType<SelectExecutionResult>(SqlExecutor.Execute(db,
            "SELECT trajectory_length(position), trajectory_centroid(position), trajectory_bbox(position), " +
            "trajectory_speed_max(position, time), trajectory_speed_avg(position, time), trajectory_speed_p95(position, time) FROM vehicle"));

        var row = Assert.Single(result.Rows);
        Assert.InRange(Convert.ToDouble(row[0]), 220d, 225d);
        var centroid = Assert.IsType<GeoPoint>(row[1]);
        Assert.Equal(0.00033333333333333332d, centroid.Lat, 12);
        Assert.Equal(0.00066666666666666664d, centroid.Lon, 12);
        var bbox = Assert.IsType<string>(row[2]);
        Assert.Contains("\"min_lat\":0", bbox);
        Assert.Contains("\"min_lon\":0", bbox);
        Assert.Contains("\"max_lat\":0.001", bbox);
        Assert.Contains("\"max_lon\":0.001", bbox);
        Assert.InRange(Convert.ToDouble(row[3]), 110d, 112d);
        Assert.InRange(Convert.ToDouble(row[4]), 83d, 84d);
        Assert.InRange(Convert.ToDouble(row[5]), 107d, 112d);
    }

    [Fact]
    public void Select_TrajectoryAggregates_GroupByTime_ReturnBucketedValues()
    {
        using var db = Tsdb.Open(Options());
        SqlExecutor.Execute(db,
            "CREATE MEASUREMENT vehicle (device TAG, position FIELD GEOPOINT)");
        SqlExecutor.Execute(db,
            "INSERT INTO vehicle (time, device, position) VALUES " +
            "(1, 'car-1', POINT(0, 0)), " +
            "(500, 'car-1', POINT(0, 0.001)), " +
            "(1500, 'car-1', POINT(0.5, 0.5)), " +
            "(2000, 'car-1', POINT(1, 1)), " +
            "(2500, 'car-1', POINT(1, 1.001))");

        var result = Assert.IsType<SelectExecutionResult>(SqlExecutor.Execute(db,
            "SELECT trajectory_length(position), trajectory_speed_avg(position, time) FROM vehicle GROUP BY time(1000ms)"));

        Assert.Equal(3, result.Rows.Count);
        Assert.InRange(Convert.ToDouble(result.Rows[0][0]), 110d, 112d);
        Assert.InRange(Convert.ToDouble(result.Rows[0][1]), 220d, 225d);
        Assert.Equal(0d, Convert.ToDouble(result.Rows[1][0]));
        Assert.Null(result.Rows[1][1]);
        Assert.InRange(Convert.ToDouble(result.Rows[2][0]), 110d, 112d);
        Assert.InRange(Convert.ToDouble(result.Rows[2][1]), 220d, 225d);
    }

}

