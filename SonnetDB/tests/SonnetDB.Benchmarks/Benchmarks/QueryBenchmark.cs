using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using InfluxDB.Client;
using InfluxDB.Client.Writes;
using Microsoft.Data.Sqlite;
using SonnetDB.Benchmarks.Helpers;
using SonnetDB.Engine;
using SonnetDB.Engine.Compaction;
using SonnetDB.Engine.Retention;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Query;
using LiteQuery = LiteDB.Query;

namespace SonnetDB.Benchmarks.Benchmarks;

/// <summary>
/// 时间范围查询性能对比：SonnetDB、SQLite、LiteDB、InfluxDB、TDengine、IoTDB、TimescaleDB。
/// 预先写入 1,000,000 条数据，然后反复查询最后 10%（约 100,000 条）的时间范围。
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Query")]
public class QueryBenchmark
{
    // ── 配置 ──────────────────────────────────────────────────────────────
    private const int _dataPointCount = 1_000_000;
    private const string _influxUrl = "http://localhost:8086";
    private const string _influxToken = "my-super-secret-auth-token";
    private const string _influxOrg = "sndb";
    private const string _influxBucket = "benchmarks";
    private const string _tDengineUrl = "http://localhost:6041";
    private const string _tDengineDb = "bench_query";
    private const string _tDengineTable = "sd_server001";
    private const string _tDengineSubTable = _tDengineDb + "." + _tDengineTable;
    private const string _iotdbUrl = "http://localhost:18080";
    private const string _iotdbDatabase = "root.bench_query";
    private const string _iotdbDevice = _iotdbDatabase + ".server001";
    private const string _timescaleTable = "sensor_data_query";
    private static readonly string _timescaleConnectionString = TimescaleDbBenchmark.DefaultConnectionString;

    // ── 共享数据 ──────────────────────────────────────────────────────────
    private BenchmarkDataPoint[] _dataPoints = [];
    private long _queryFromMs;
    private long _queryToMs;

    // ── SQLite ─────────────────────────────────────────────────────────────
    private string _sqliteDbPath = string.Empty;

    // ── LiteDB ─────────────────────────────────────────────────────────────
    private string _liteDbPath = string.Empty;

    // ── InfluxDB ───────────────────────────────────────────────────────────
    private InfluxDBClient? _influxClient;
    private bool _influxAvailable;

    // ── TDengine ──────────────────────────────────────────────────────────
    private TDengineRestClient? _tdengineClient;
    private bool _tdengineAvailable;

    // ── IoTDB ─────────────────────────────────────────────────────────────
    private IoTDBRestClient? _iotdbClient;
    private bool _iotdbAvailable;

    // ── TimescaleDB ───────────────────────────────────────────────────────
    private bool _timescaleAvailable;

    // ── SonnetDB ─────────────────────────────────────────────────────────────
    private string _sonnetDbRootDir = string.Empty;
    private Tsdb? _sonnetDbDb;
    private ulong _sonnetDbSeriesId;
    private static readonly IReadOnlyDictionary<string, string> _sonnetDbTags =
        new Dictionary<string, string> { ["host"] = "server001" };

    // ─────────────────────────────────────────────────────────────────────
    // GlobalSetup：写入 100 万条测试数据
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>全局初始化：向各数据库写入 100 万条测试数据，供后续反复查询。</summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _dataPoints = DataGenerator.Generate(_dataPointCount);
        _queryFromMs = DataGenerator.QueryFromMs(_dataPointCount);
        _queryToMs = DataGenerator.QueryToMs(_dataPointCount);

        // ── SonnetDB：写入 100 万条并 Flush 到磁盘 ────────────────
        _sonnetDbSeriesId = SeriesId.Compute(
            new SeriesKey("sensor_data", new Dictionary<string, string> { ["host"] = "server001" }));
        _sonnetDbRootDir = Path.Combine(Path.GetTempPath(), $"sonnetdb_bench_query_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sonnetDbRootDir);
        _sonnetDbDb = Tsdb.Open(new TsdbOptions
        {
            RootDirectory = _sonnetDbRootDir,
            FlushPolicy = new MemTableFlushPolicy
            {
                MaxBytes = long.MaxValue,
                MaxPoints = int.MaxValue,
                MaxAge = TimeSpan.MaxValue
            },
            BackgroundFlush = new BackgroundFlushOptions { Enabled = false },
            Compaction = new CompactionPolicy { Enabled = false },
            Retention = new RetentionPolicy { Enabled = false }
        });
        foreach (var dp in _dataPoints)
            _sonnetDbDb.Write(Point.Create(
                "sensor_data",
                dp.Timestamp,
                _sonnetDbTags,
                new Dictionary<string, FieldValue> { ["value"] = FieldValue.FromDouble(dp.Value) }));
        _sonnetDbDb.FlushNow();

        // ── SQLite ─────────────────────────────────────────────────────
        _sqliteDbPath = Path.Combine(Path.GetTempPath(), $"sonnetdb_bench_query_{Guid.NewGuid():N}.db");
        using var conn = OpenSqlite(_sqliteDbPath);
        SqliteExecute(conn, "CREATE TABLE IF NOT EXISTS sensor_data " +
                            "(ts INTEGER NOT NULL, host TEXT NOT NULL, value REAL NOT NULL)");
        SqliteExecute(conn, "CREATE INDEX IF NOT EXISTS idx_ts ON sensor_data (ts)");
        SqliteBulkInsert(conn, _dataPoints);

        // ── LiteDB ─────────────────────────────────────────────────────
        _liteDbPath = LiteDbBenchmark.CreateTempPath("query");
        using (var liteDb = LiteDbBenchmark.Open(_liteDbPath))
            LiteDbBenchmark.InsertBulk(
                liteDb,
                LiteDbBenchmark.CreatePoints(_dataPoints),
                ensureQueryIndexes: true);

        // ── InfluxDB ────────────────────────────────────────────────────
        try
        {
            _influxClient = new InfluxDBClient(_influxUrl, _influxToken);
            _influxAvailable = await _influxClient.PingAsync().ConfigureAwait(false);
            if (_influxAvailable)
            {
                await EnsureInfluxBucketAsync().ConfigureAwait(false);
                await WriteInfluxDataAsync(_dataPoints).ConfigureAwait(false);
            }
        }
        catch
        {
            _influxAvailable = false;
            Console.Error.WriteLine(
                "[SKIP] InfluxDB 不可用。请先执行: docker compose -f tests/SonnetDB.Benchmarks/docker/docker-compose.yml up -d influxdb");
        }

        // ── TDengine ────────────────────────────────────────────────────
        try
        {
            _tdengineClient = new TDengineRestClient(_tDengineUrl);
            await _tdengineClient.ExecuteAsync($"CREATE DATABASE IF NOT EXISTS {_tDengineDb} PRECISION 'ms'")
                .ConfigureAwait(false);
            await _tdengineClient.ExecuteAsync(
                $"CREATE STABLE IF NOT EXISTS {_tDengineDb}.sensor_data " +
                "(ts TIMESTAMP, `value` DOUBLE) TAGS (`host` BINARY(64))").ConfigureAwait(false);
            await _tdengineClient.ExecuteAsync(
                $"CREATE TABLE IF NOT EXISTS {_tDengineSubTable} " +
                $"USING {_tDengineDb}.sensor_data TAGS ('server001')").ConfigureAwait(false);
            await _tdengineClient.BulkInsertAsync(_tDengineSubTable, _dataPoints).ConfigureAwait(false);
            _tdengineAvailable = true;
        }
        catch
        {
            _tdengineAvailable = false;
            Console.Error.WriteLine(
                "[SKIP] TDengine 不可用。请先执行: docker compose -f tests/SonnetDB.Benchmarks/docker/docker-compose.yml up -d tdengine");
        }

        // ── IoTDB ───────────────────────────────────────────────────────
        try
        {
            _iotdbClient = new IoTDBRestClient(_iotdbUrl);
            await _iotdbClient.QueryAsync("SHOW VERSION").ConfigureAwait(false);
            await _iotdbClient.PrepareSeriesAsync(_iotdbDatabase, _iotdbDevice).ConfigureAwait(false);
            await _iotdbClient.InsertTabletAsync(_iotdbDevice, _dataPoints).ConfigureAwait(false);
            _iotdbAvailable = true;
        }
        catch
        {
            _iotdbAvailable = false;
            Console.Error.WriteLine(
                "[SKIP] IoTDB 不可用。请先执行: docker compose -f tests/SonnetDB.Benchmarks/docker/docker-compose.yml up -d iotdb");
        }

        // ── TimescaleDB ─────────────────────────────────────────────────
        _timescaleAvailable = await TimescaleDbBenchmark.IsAvailableAsync(_timescaleConnectionString)
            .ConfigureAwait(false);
        if (_timescaleAvailable)
        {
            await TimescaleDbBenchmark.PrepareSensorTableAsync(_timescaleConnectionString, _timescaleTable)
                .ConfigureAwait(false);
            await TimescaleDbBenchmark.BulkCopyAsync(_timescaleConnectionString, _timescaleTable, _dataPoints)
                .ConfigureAwait(false);
        }
        else
        {
            Console.Error.WriteLine(
                "[SKIP] TimescaleDB 不可用。请先执行: docker compose -f tests/SonnetDB.Benchmarks/docker/docker-compose.yml up -d timescaledb");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Benchmark 方法
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// SonnetDB 时间范围查询（真实引擎）。
    /// 查询最后 10% 时间段内约 100,000 条数据点。
    /// </summary>
    [Benchmark(Baseline = true, Description = "SonnetDB 范围查询")]
    public List<DataPoint> SonnetDB_Query_Range()
    {
        var query = new PointQuery(
            _sonnetDbSeriesId,
            "value",
            new TimeRange(_queryFromMs, _queryToMs - 1));
        return [.. _sonnetDbDb!.Query.Execute(query)];
    }

    /// <summary>SQLite 时间范围查询（索引扫描，约 100,000 条）。</summary>
    [Benchmark(Description = "SQLite 范围查询")]
    public List<(long Ts, string Host, double Value)> SQLite_Query_Range()
    {
        using var conn = OpenSqlite(_sqliteDbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT ts, host, value FROM sensor_data WHERE ts >= @from AND ts < @to ORDER BY ts";
        cmd.Parameters.AddWithValue("@from", _queryFromMs);
        cmd.Parameters.AddWithValue("@to", _queryToMs);

        var result = new List<(long, string, double)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add((reader.GetInt64(0), reader.GetString(1), reader.GetDouble(2)));

        return result;
    }

    /// <summary>LiteDB 时间范围查询（Ts 索引，约 100,000 条）。</summary>
    [Benchmark(Description = "LiteDB 范围查询")]
    public int LiteDB_Query_Range()
    {
        using var db = LiteDbBenchmark.Open(_liteDbPath);
        var collection = db.GetCollection<LiteDbDataPoint>(LiteDbBenchmark.CollectionName);
        var result = collection.Find(
            LiteQuery.Between(nameof(LiteDbDataPoint.Ts), _queryFromMs, _queryToMs - 1))
            .ToList();
        return result.Count;
    }

    /// <summary>InfluxDB 时间范围查询（Flux，约 100,000 条）。</summary>
    [Benchmark(Description = "InfluxDB 范围查询")]
    public async Task<int> InfluxDB_Query_Range()
    {
        if (!_influxAvailable)
        {
            Console.Error.WriteLine("[SKIP] InfluxDB 不可用");
            return -1;
        }

        var fromRfc3339 = DateTimeOffset.FromUnixTimeMilliseconds(_queryFromMs)
            .UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toRfc3339 = DateTimeOffset.FromUnixTimeMilliseconds(_queryToMs)
            .UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        var flux = $"""
            from(bucket: "{_influxBucket}")
              |> range(start: {fromRfc3339}, stop: {toRfc3339})
              |> filter(fn: (r) => r["_measurement"] == "sensor_data")
              |> sort(columns: ["_time"])
            """;

        var tables = await _influxClient!.GetQueryApi()
            .QueryAsync(flux, _influxOrg).ConfigureAwait(false);

        return tables.Sum(t => t.Records.Count);
    }

    /// <summary>TDengine 时间范围查询（SQL，约 100,000 条）。</summary>
    [Benchmark(Description = "TDengine 范围查询")]
    public async Task<string> TDengine_Query_Range()
    {
        if (!_tdengineAvailable)
        {
            Console.Error.WriteLine("[SKIP] TDengine 不可用");
            return string.Empty;
        }

        return await _tdengineClient!.ExecuteAsync(
            $"SELECT ts, `host`, `value` FROM {_tDengineSubTable} " +
            $"WHERE ts >= {_queryFromMs} AND ts < {_queryToMs} ORDER BY ts")
            .ConfigureAwait(false);
    }

    /// <summary>Apache IoTDB 时间范围查询（REST v2 SQL，约 100,000 条）。</summary>
    [Benchmark(Description = "IoTDB 范围查询")]
    public async Task<string> IoTDB_Query_Range()
    {
        if (!_iotdbAvailable)
        {
            Console.Error.WriteLine("[SKIP] IoTDB 不可用");
            return string.Empty;
        }

        return await _iotdbClient!.QueryAsync(
            $"SELECT value FROM {_iotdbDevice} WHERE time >= {_queryFromMs} AND time < {_queryToMs}")
            .ConfigureAwait(false);
    }

    /// <summary>PostgreSQL/TimescaleDB 时间范围查询（hypertable + host/time 索引，约 100,000 条）。</summary>
    [Benchmark(Description = "TimescaleDB 范围查询")]
    public async Task<int> TimescaleDB_Query_Range()
    {
        if (!_timescaleAvailable)
        {
            Console.Error.WriteLine("[SKIP] TimescaleDB 不可用");
            return -1;
        }

        return await TimescaleDbBenchmark.QueryRangeAsync(
            _timescaleConnectionString,
            _timescaleTable,
            _queryFromMs,
            _queryToMs).ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────
    // GlobalCleanup
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>全局清理：删除测试数据库。</summary>
    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_sqliteDbPath))
            File.Delete(_sqliteDbPath);
        LiteDbBenchmark.DeleteDatabaseFiles(_liteDbPath);

        if (_influxAvailable)
        {
            try
            {
                _influxClient!.GetDeleteApi().Delete(
                    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(_dataPointCount + 1),
                    string.Empty, _influxBucket, _influxOrg).GetAwaiter().GetResult();
            }
            catch { /* 清理失败不影响结果 */ }

            _influxClient!.Dispose();
        }

        if (_tdengineAvailable)
        {
            try
            {
                await _tdengineClient!.ExecuteAsync($"DROP DATABASE IF EXISTS {_tDengineDb}")
                    .ConfigureAwait(false);
            }
            catch { /* 清理失败不影响结果 */ }

            _tdengineClient!.Dispose();
        }

        if (_iotdbAvailable)
        {
            await _iotdbClient!.DropDatabaseIfExistsAsync(_iotdbDatabase).ConfigureAwait(false);
            _iotdbClient!.Dispose();
        }

        if (_timescaleAvailable)
        {
            await TimescaleDbBenchmark.DropSensorTableAsync(_timescaleConnectionString, _timescaleTable)
                .ConfigureAwait(false);
        }

        // SonnetDB
        _sonnetDbDb?.Dispose();
        _sonnetDbDb = null;
        if (!string.IsNullOrEmpty(_sonnetDbRootDir) && Directory.Exists(_sonnetDbRootDir))
            Directory.Delete(_sonnetDbRootDir, recursive: true);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 辅助方法
    // ─────────────────────────────────────────────────────────────────────

    private async Task EnsureInfluxBucketAsync()
    {
        var bucketsApi = _influxClient!.GetBucketsApi();
        var existing = await bucketsApi.FindBucketByNameAsync(_influxBucket).ConfigureAwait(false);
        if (existing is not null) return;
        var orgs = await _influxClient.GetOrganizationsApi()
            .FindOrganizationsAsync(org: _influxOrg).ConfigureAwait(false);
        if (orgs is null || orgs.Count == 0)
            throw new InvalidOperationException($"InfluxDB org '{_influxOrg}' not found");
        await bucketsApi.CreateBucketAsync(_influxBucket, orgs[0].Id).ConfigureAwait(false);
    }

    private static SqliteConnection OpenSqlite(string path)
    {
        var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        SqliteExecute(conn, "PRAGMA synchronous = OFF");
        SqliteExecute(conn, "PRAGMA journal_mode = WAL");
        return conn;
    }

    private static void SqliteExecute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void SqliteBulkInsert(SqliteConnection conn, BenchmarkDataPoint[] points)
    {
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO sensor_data (ts, host, value) VALUES (@ts, @host, @val)";
        var tsParam = cmd.Parameters.AddWithValue("@ts", 0L);
        var hostParam = cmd.Parameters.AddWithValue("@host", string.Empty);
        var valParam = cmd.Parameters.AddWithValue("@val", 0.0);
        cmd.Prepare();

        foreach (var dp in points)
        {
            tsParam.Value = dp.Timestamp;
            hostParam.Value = dp.Host;
            valParam.Value = dp.Value;
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private async Task WriteInfluxDataAsync(BenchmarkDataPoint[] points)
    {
        const int batchSize = 10_000;
        var writeApi = _influxClient!.GetWriteApiAsync();

        for (int offset = 0; offset < points.Length; offset += batchSize)
        {
            int end = Math.Min(offset + batchSize, points.Length);
            var batch = new PointData[end - offset];
            for (int i = 0; i < batch.Length; i++)
            {
                var dp = points[offset + i];
                batch[i] = PointData.Measurement("sensor_data")
                    .Tag("host", dp.Host)
                    .Field("value", dp.Value)
                    .Timestamp(dp.Timestamp, InfluxDB.Client.Api.Domain.WritePrecision.Ms);
            }

            await writeApi.WritePointsAsync(batch, _influxBucket, _influxOrg).ConfigureAwait(false);
        }
    }
}
