using System.Globalization;
using BenchmarkDotNet.Attributes;
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

namespace SonnetDB.Benchmarks.Benchmarks;

/// <summary>
/// 聚合查询性能对比：SonnetDB、SQLite、LiteDB、InfluxDB、TDengine、IoTDB、TimescaleDB。
/// 预先写入 1,000,000 条数据，然后反复执行按 1 分钟桶的 AVG/MIN/MAX/COUNT 聚合。
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Aggregate")]
public class AggregateBenchmark
{
    // ── 配置 ──────────────────────────────────────────────────────────────
    private const int _dataPointCount = 1_000_000;
    private const string _influxUrl = "http://localhost:8086";
    private const string _influxToken = "my-super-secret-auth-token";
    private const string _influxOrg = "sndb";
    private const string _influxBucket = "benchmarks";
    private const string _tDengineUrl = "http://localhost:6041";
    private const string _tDengineDb = "bench_aggregate";
    private const string _tDengineTable = "sd_server001";
    private const string _tDengineSubTable = _tDengineDb + "." + _tDengineTable;
    private const string _iotdbUrl = "http://localhost:18080";
    private const string _iotdbDatabase = "root.bench_aggregate";
    private const string _iotdbDevice = _iotdbDatabase + ".server001";
    private const string _timescaleTable = "sensor_data_aggregate";
    private static readonly string _timescaleConnectionString = TimescaleDbBenchmark.DefaultConnectionString;

    // ── 共享数据 ──────────────────────────────────────────────────────────
    private BenchmarkDataPoint[] _dataPoints = [];

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
    private TimeRange _sonnetDbFullRange;
    private static readonly IReadOnlyDictionary<string, string> _sonnetDbTags =
        new Dictionary<string, string> { ["host"] = "server001" };

    // ─────────────────────────────────────────────────────────────────────
    // GlobalSetup：写入 100 万条测试数据
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>全局初始化：向各数据库写入 100 万条测试数据，供后续反复聚合。</summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _dataPoints = DataGenerator.Generate(_dataPointCount);

        // ── SonnetDB：写入 100 万条并 Flush 到磁盘 ────────────────
        _sonnetDbSeriesId = SeriesId.Compute(
            new SeriesKey("sensor_data", new Dictionary<string, string> { ["host"] = "server001" }));
        _sonnetDbFullRange = new TimeRange(
            DataGenerator.StartTimestampMs,
            DataGenerator.QueryToMs(_dataPointCount));
        _sonnetDbRootDir = Path.Combine(Path.GetTempPath(), $"sonnetdb_bench_aggregate_{Guid.NewGuid():N}");
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
        _sqliteDbPath = Path.Combine(Path.GetTempPath(),
            $"sonnetdb_bench_aggregate_{Guid.NewGuid():N}.db");
        using var conn = OpenSqlite(_sqliteDbPath);
        SqliteExecute(conn,
            "CREATE TABLE IF NOT EXISTS sensor_data " +
            "(ts INTEGER NOT NULL, host TEXT NOT NULL, value REAL NOT NULL)");
        SqliteExecute(conn, "CREATE INDEX IF NOT EXISTS idx_ts ON sensor_data (ts)");
        SqliteBulkInsert(conn, _dataPoints);

        // ── LiteDB ─────────────────────────────────────────────────────
        _liteDbPath = LiteDbBenchmark.CreateTempPath("aggregate");
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
            await _tdengineClient.ExecuteAsync(
                $"CREATE DATABASE IF NOT EXISTS {_tDengineDb} PRECISION 'ms'").ConfigureAwait(false);
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
    /// SonnetDB 1 分钟桶聚合（真实引擎）。
    /// 对全量 100 万数据点按每分钟分组，计算 AVG，结果约含 16,667 个桶。
    /// </summary>
    [Benchmark(Baseline = true, Description = "SonnetDB 1分钟聚合")]
    public List<AggregateBucket> SonnetDB_Aggregate_1Min()
    {
        var query = new AggregateQuery(
            _sonnetDbSeriesId,
            "value",
            _sonnetDbFullRange,
            Aggregator.Avg,
            60_000L);
        return [.. _sonnetDbDb!.Query.Execute(query)];
    }

    /// <summary>SQLite 1 分钟桶聚合（GROUP BY，全量 100 万条，约 16,667 个桶）。</summary>
    [Benchmark(Description = "SQLite 1分钟聚合")]
    public List<(long Bucket, double Avg, double Min, double Max, long Count)> SQLite_Aggregate_1Min()
    {
        using var conn = OpenSqlite(_sqliteDbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT ts / 60000 * 60000 AS bucket, " +
            "       AVG(value)         AS avg_val, " +
            "       MIN(value)         AS min_val, " +
            "       MAX(value)         AS max_val, " +
            "       COUNT(*)           AS cnt " +
            "FROM sensor_data " +
            "GROUP BY bucket " +
            "ORDER BY bucket";

        var result = new List<(long, double, double, double, long)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add((reader.GetInt64(0), reader.GetDouble(1),
                        reader.GetDouble(2), reader.GetDouble(3), reader.GetInt64(4)));

        return result;
    }

    /// <summary>LiteDB 1 分钟桶聚合（Ts 索引顺序扫描 + 进程内分桶）。</summary>
    [Benchmark(Description = "LiteDB 1分钟聚合")]
    public int LiteDB_Aggregate_1Min()
    {
        using var db = LiteDbBenchmark.Open(_liteDbPath);
        var collection = db.GetCollection<LiteDbDataPoint>(LiteDbBenchmark.CollectionName);
        var result = LiteDbBenchmark.Aggregate1Min(collection.FindAll());
        return result.Count;
    }

    /// <summary>InfluxDB 1 分钟桶聚合（Flux aggregateWindow，全量 100 万条）。</summary>
    [Benchmark(Description = "InfluxDB 1分钟聚合")]
    public async Task<int> InfluxDB_Aggregate_1Min()
    {
        if (!_influxAvailable)
        {
            Console.Error.WriteLine("[SKIP] InfluxDB 不可用");
            return -1;
        }

        var startRfc3339 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var stopRfc3339 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddSeconds(_dataPointCount + 1).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        var flux = $"""
            from(bucket: "{_influxBucket}")
              |> range(start: {startRfc3339}, stop: {stopRfc3339})
              |> filter(fn: (r) => r["_measurement"] == "sensor_data")
              |> aggregateWindow(every: 1m, fn: mean, createEmpty: false)
            """;

        var tables = await _influxClient!.GetQueryApi()
            .QueryAsync(flux, _influxOrg).ConfigureAwait(false);

        return tables.Sum(t => t.Records.Count);
    }

    /// <summary>TDengine 1 分钟桶聚合（INTERVAL(1m)，全量 100 万条）。</summary>
    [Benchmark(Description = "TDengine 1分钟聚合")]
    public async Task<string> TDengine_Aggregate_1Min()
    {
        if (!_tdengineAvailable)
        {
            Console.Error.WriteLine("[SKIP] TDengine 不可用");
            return string.Empty;
        }

        return await _tdengineClient!.ExecuteAsync(
            $"SELECT _wstart, AVG(`value`), MIN(`value`), MAX(`value`), COUNT(*) " +
            $"FROM {_tDengineSubTable} INTERVAL(1m)").ConfigureAwait(false);
    }

    /// <summary>Apache IoTDB 1 分钟桶聚合（GROUP BY 时间窗口，全量 100 万条）。</summary>
    [Benchmark(Description = "IoTDB 1分钟聚合")]
    public async Task<string> IoTDB_Aggregate_1Min()
    {
        if (!_iotdbAvailable)
        {
            Console.Error.WriteLine("[SKIP] IoTDB 不可用");
            return string.Empty;
        }

        long start = DataGenerator.StartTimestampMs;
        long end = DataGenerator.QueryToMs(_dataPointCount);
        return await _iotdbClient!.QueryAsync(
            $"SELECT AVG(value), MIN_VALUE(value), MAX_VALUE(value), COUNT(value) " +
            $"FROM {_iotdbDevice} GROUP BY ([{start},{end}), 60000ms)")
            .ConfigureAwait(false);
    }

    /// <summary>PostgreSQL/TimescaleDB 1 分钟桶聚合（time_bucket，全量 100 万条）。</summary>
    [Benchmark(Description = "TimescaleDB 1分钟聚合")]
    public async Task<int> TimescaleDB_Aggregate_1Min()
    {
        if (!_timescaleAvailable)
        {
            Console.Error.WriteLine("[SKIP] TimescaleDB 不可用");
            return -1;
        }

        return await TimescaleDbBenchmark.Aggregate1MinAsync(
            _timescaleConnectionString,
            _timescaleTable,
            DataGenerator.StartTimestampMs,
            DataGenerator.QueryToMs(_dataPointCount)).ConfigureAwait(false);
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
