using InfluxDB.Client.Core.Flux.Domain;
using Xunit;

namespace SonnetDB.Accuracy.Tests;

public sealed class InfluxDbAccuracyTests(AccuracyFixture fixture) : IClassFixture<AccuracyFixture>
{
    private readonly AccuracyFixture _fixture = fixture;

    [Fact]
    public async Task ShowMeasurements_MatchesInfluxDb()
    {
        if (!_fixture.TryEnsureReady())
            return;

        var sonnet = await _fixture.QuerySonnetAsync("SHOW MEASUREMENTS");
        var influx = await QueryInfluxMeasurementNamesAsync();

        var sonnetRows = AccuracyResultNormalizer.NormalizeRows(
            sonnet.Rows.Select(AccuracyResultNormalizer.ConvertSonnetRow));
        var influxRows = AccuracyResultNormalizer.NormalizeRows(
            influx.Select(name => (IReadOnlyList<object?>)[name]));

        Assert.Equal(influxRows, sonnetRows);
    }

    [Fact]
    public async Task Telemetry_FieldCounts_MatchInfluxDb()
    {
        if (!_fixture.TryEnsureReady())
            return;

        var sonnet = await _fixture.QuerySonnetAsync(
            "SELECT count(usage), count(errors), count(active), count(status) FROM telemetry");
        var influx = await QueryInfluxTelemetryFieldCountsAsync();

        var sonnetRows = AccuracyResultNormalizer.NormalizeRows(
            sonnet.Rows.Select(AccuracyResultNormalizer.ConvertSonnetRow),
            sortRows: false);
        var influxRows = AccuracyResultNormalizer.NormalizeRows([influx], sortRows: false);

        Assert.Equal(influxRows, sonnetRows);
    }

    [Fact]
    public async Task Telemetry_RawProjectionAcrossSeries_MatchesInfluxDb()
    {
        if (!_fixture.TryEnsureReady())
            return;

        var sonnet = await _fixture.QuerySonnetAsync(
            "SELECT time, host, region, usage, errors, active, status FROM telemetry");
        var influx = await QueryInfluxTelemetryRowsAsync(host: null, limit: null, offset: null);

        var sonnetRows = AccuracyResultNormalizer.NormalizeRows(
            sonnet.Rows.Select(AccuracyResultNormalizer.ConvertSonnetRow));
        var influxRows = AccuracyResultNormalizer.NormalizeRows(influx);

        Assert.Equal(influxRows, sonnetRows);
    }

    [Fact]
    public async Task Telemetry_SparseFields_MatchInfluxDb()
    {
        if (!_fixture.TryEnsureReady())
            return;

        var sonnet = await _fixture.QuerySonnetAsync(
            "SELECT time, host, region, usage, errors, active, status FROM telemetry WHERE host = 'beta'");
        var influx = await QueryInfluxTelemetryRowsAsync(host: "beta", limit: null, offset: null);

        var sonnetRows = AccuracyResultNormalizer.NormalizeRows(
            sonnet.Rows.Select(AccuracyResultNormalizer.ConvertSonnetRow));
        var influxRows = AccuracyResultNormalizer.NormalizeRows(influx);

        Assert.Equal(influxRows, sonnetRows);
    }

    [Fact]
    public async Task Telemetry_LimitOffsetOnSingleSeries_MatchesInfluxDb()
    {
        if (!_fixture.TryEnsureReady())
            return;

        var sonnet = await _fixture.QuerySonnetAsync(
            "SELECT time, host, region, usage, errors, active, status FROM telemetry WHERE host = 'alpha' LIMIT 2 OFFSET 1");
        var influx = await QueryInfluxTelemetryRowsAsync(host: "alpha", limit: 2, offset: 1);

        var sonnetRows = AccuracyResultNormalizer.NormalizeRows(
            sonnet.Rows.Select(AccuracyResultNormalizer.ConvertSonnetRow),
            sortRows: false);
        var influxRows = AccuracyResultNormalizer.NormalizeRows(influx, sortRows: false);

        Assert.Equal(influxRows, sonnetRows);
    }

    [Fact]
    public async Task Telemetry_AggregatesOnSingleSeries_MatchInfluxDb()
    {
        if (!_fixture.TryEnsureReady())
            return;

        var sonnet = await _fixture.QuerySonnetAsync(
            "SELECT sum(usage), avg(usage), min(usage), max(usage), count(usage), first(usage), last(usage) FROM telemetry WHERE host = 'alpha'");
        var influx = await QueryInfluxAggregateRowAsync();

        var sonnetRows = AccuracyResultNormalizer.NormalizeRows(
            sonnet.Rows.Select(AccuracyResultNormalizer.ConvertSonnetRow),
            sortRows: false);
        var influxRows = AccuracyResultNormalizer.NormalizeRows([influx], sortRows: false);

        Assert.Equal(influxRows, sonnetRows);
    }

    [Fact]
    public async Task Telemetry_GroupByTimeAggregates_MatchInfluxDb()
    {
        if (!_fixture.TryEnsureReady())
            return;

        var sonnet = await _fixture.QuerySonnetAsync(
            "SELECT avg(usage), count(usage), min(usage), max(usage) FROM telemetry WHERE host = 'alpha' GROUP BY time(60000ms)");
        var influx = await QueryInfluxWindowAggregateRowsAsync();

        var sonnetRows = AccuracyResultNormalizer.NormalizeRows(
            sonnet.Rows.Select(AccuracyResultNormalizer.ConvertSonnetRow),
            sortRows: false);
        var influxRows = AccuracyResultNormalizer.NormalizeRows(influx, sortRows: false);

        Assert.Equal(influxRows, sonnetRows);
    }

    [Fact]
    public async Task Audit_RawProjection_MatchesInfluxDb()
    {
        if (!_fixture.TryEnsureReady())
            return;

        var sonnet = await _fixture.QuerySonnetAsync(
            "SELECT time, host, severity, code, message FROM audit");
        var influx = await QueryInfluxAuditRowsAsync();

        var sonnetRows = AccuracyResultNormalizer.NormalizeRows(
            sonnet.Rows.Select(AccuracyResultNormalizer.ConvertSonnetRow));
        var influxRows = AccuracyResultNormalizer.NormalizeRows(influx);

        Assert.Equal(influxRows, sonnetRows);
    }

    private async Task<IReadOnlyList<string>> QueryInfluxMeasurementNamesAsync()
    {
        var flux = $$"""
            import "influxdata/influxdb/schema"

            schema.measurements(bucket: "{{AccuracyDataSet.InfluxBucket}}")
              |> keep(columns: ["_value"])
              |> sort(columns: ["_value"])
            """;

        var tables = await QueryInfluxAsync(flux);
        return tables.SelectMany(table => table.Records)
            .Select(record => record.GetValueByKey("_value")?.ToString() ?? string.Empty)
            .ToArray();
    }

    private async Task<IReadOnlyList<object?>> QueryInfluxTelemetryFieldCountsAsync()
    {
        return
        [
            await QueryInfluxScalarAsync("telemetry", "usage", "count()"),
            await QueryInfluxScalarAsync("telemetry", "errors", "count()"),
            await QueryInfluxScalarAsync("telemetry", "active", "count()"),
            await QueryInfluxScalarAsync("telemetry", "status", "count()"),
        ];
    }

    private async Task<IReadOnlyList<object?>> QueryInfluxAggregateRowAsync()
    {
        return
        [
            await QueryInfluxScalarAsync("telemetry", "usage", "sum()", "alpha"),
            await QueryInfluxScalarAsync("telemetry", "usage", "mean()", "alpha"),
            await QueryInfluxScalarAsync("telemetry", "usage", "min()", "alpha"),
            await QueryInfluxScalarAsync("telemetry", "usage", "max()", "alpha"),
            await QueryInfluxScalarAsync("telemetry", "usage", "count()", "alpha"),
            await QueryInfluxScalarAsync("telemetry", "usage", "first()", "alpha"),
            await QueryInfluxScalarAsync("telemetry", "usage", "last()", "alpha"),
        ];
    }

    private async Task<IReadOnlyList<IReadOnlyList<object?>>> QueryInfluxWindowAggregateRowsAsync()
    {
        var meanRows = await QueryInfluxWindowValuesAsync("mean", "alpha");
        var countRows = await QueryInfluxWindowValuesAsync("count", "alpha");
        var minRows = await QueryInfluxWindowValuesAsync("min", "alpha");
        var maxRows = await QueryInfluxWindowValuesAsync("max", "alpha");

        Assert.Equal(meanRows.Count, countRows.Count);
        Assert.Equal(meanRows.Count, minRows.Count);
        Assert.Equal(meanRows.Count, maxRows.Count);

        var rows = new List<IReadOnlyList<object?>>(meanRows.Count);
        for (var i = 0; i < meanRows.Count; i++)
        {
            rows.Add(
            [
                meanRows[i],
                countRows[i],
                minRows[i],
                maxRows[i],
            ]);
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyList<object?>>> QueryInfluxTelemetryRowsAsync(string? host, int? limit, int? offset)
    {
        var hostFilter = host is null ? string.Empty : $" and r.host == \"{host}\"";
        var limitClause = limit is null
            ? string.Empty
            : $"  |> limit(n: {limit.Value}, offset: {offset ?? 0})";

        var flux = $$"""
            from(bucket: "{{AccuracyDataSet.InfluxBucket}}")
              |> range(start: {{AccuracyDataSet.RangeStartRfc3339}}, stop: {{AccuracyDataSet.RangeStopRfc3339}})
              |> filter(fn: (r) => r._measurement == "telemetry"{{hostFilter}})
              |> pivot(rowKey: ["_time", "host", "region"], columnKey: ["_field"], valueColumn: "_value")
              |> keep(columns: ["_time", "host", "region", "usage", "errors", "active", "status"])
              |> sort(columns: ["_time", "host", "region"])
            {{limitClause}}
            """;

        return await QueryInfluxRowsAsync(
            flux,
            [
                "time",
                "host",
                "region",
                "usage",
                "errors",
                "active",
                "status",
            ]);
    }

    private async Task<IReadOnlyList<IReadOnlyList<object?>>> QueryInfluxAuditRowsAsync()
    {
        var flux = $$"""
            from(bucket: "{{AccuracyDataSet.InfluxBucket}}")
              |> range(start: {{AccuracyDataSet.RangeStartRfc3339}}, stop: {{AccuracyDataSet.RangeStopRfc3339}})
              |> filter(fn: (r) => r._measurement == "audit")
              |> pivot(rowKey: ["_time", "host", "severity"], columnKey: ["_field"], valueColumn: "_value")
              |> keep(columns: ["_time", "host", "severity", "code", "message"])
              |> sort(columns: ["_time", "host", "severity"])
            """;

        return await QueryInfluxRowsAsync(
            flux,
            [
                "time",
                "host",
                "severity",
                "code",
                "message",
            ]);
    }

    private async Task<IReadOnlyList<IReadOnlyList<object?>>> QueryInfluxRowsAsync(string flux, IReadOnlyList<string> columns)
    {
        var tables = await QueryInfluxAsync(flux);
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var record in tables.SelectMany(table => table.Records))
        {
            var row = new object?[columns.Count];
            for (var i = 0; i < columns.Count; i++)
                row[i] = GetInfluxValue(record, columns[i]);
            rows.Add(row);
        }

        return rows;
    }

    private async Task<IReadOnlyList<object?>> QueryInfluxWindowValuesAsync(string functionName, string host)
    {
        var flux = $$"""
            from(bucket: "{{AccuracyDataSet.InfluxBucket}}")
              |> range(start: {{AccuracyDataSet.RangeStartRfc3339}}, stop: {{AccuracyDataSet.RangeStopRfc3339}})
              |> filter(fn: (r) => r._measurement == "telemetry" and r._field == "usage" and r.host == "{{host}}")
              |> aggregateWindow(every: 1m, fn: {{functionName}}, createEmpty: false)
              |> keep(columns: ["_time", "_value"])
              |> sort(columns: ["_time"])
            """;

        var tables = await QueryInfluxAsync(flux);
        return tables.SelectMany(table => table.Records)
            .Select(record => GetInfluxValue(record, "_value"))
            .ToArray();
    }

    private async Task<object?> QueryInfluxScalarAsync(
        string measurement,
        string field,
        string aggregateCall,
        string? host = null)
    {
        var hostFilter = host is null ? string.Empty : $" and r.host == \"{host}\"";
        var flux = $$"""
            from(bucket: "{{AccuracyDataSet.InfluxBucket}}")
              |> range(start: {{AccuracyDataSet.RangeStartRfc3339}}, stop: {{AccuracyDataSet.RangeStopRfc3339}})
              |> filter(fn: (r) => r._measurement == "{{measurement}}" and r._field == "{{field}}"{{hostFilter}})
              |> group(columns: [])
              |> {{aggregateCall}}
            """;

        var tables = await QueryInfluxAsync(flux);
        var record = tables.SelectMany(table => table.Records).Single();
        return GetInfluxValue(record, "_value");
    }

    private async Task<IReadOnlyList<FluxTable>> QueryInfluxAsync(string flux)
        => await _fixture.InfluxClient.GetQueryApi().QueryAsync(flux, AccuracyDataSet.InfluxOrg);

    private static object? GetInfluxValue(FluxRecord record, string column)
    {
        if (column == "time")
        {
            var time = record.GetTimeInDateTime();
            return time is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(time.Value, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        }

        return record.Values.TryGetValue(column == "time" ? "_time" : column, out var value)
            ? value
            : null;
    }
}
