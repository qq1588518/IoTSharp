using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using InfluxDB.Client;
using InfluxDB.Client.Writes;
using Microsoft.Data.Sqlite;
using SonnetDB.Benchmarks.Helpers;
using SonnetDB.Engine;
using SonnetDB.Engine.Compaction;
using SonnetDB.Engine.Retention;
using SonnetDB.Memory;
using SonnetDB.Model;

namespace SonnetDB.Benchmarks.Benchmarks;

/// <summary>
/// 1,000,000 条数据批量写入性能对比：SonnetDB、SQLite、LiteDB、InfluxDB、TDengine、IoTDB、TimescaleDB。
/// 每次迭代均先清空数据，再执行完整的 100 万条写入操作。
/// </summary>
[Config(typeof(InsertConfig))]
[MemoryDiagnoser]
[BenchmarkCategory("Insert")]
public class InsertBenchmark
{
    // ── 配置 ──────────────────────────────────────────────────────────────
    private const int _dataPointCount = 1_000_000;
    private const string _influxUrl = "http://localhost:8086";
    private const string _influxToken = "my-super-secret-auth-token";
    private const string _influxOrg = "sndb";
    private const string _influxBucket = "benchmarks";
    private const string _tDengineUrl = "http://localhost:6041";
    private const string _tDengineDb = "bench_insert";
    private const string _tDengineTable = "sd_server001";
    private const string _tDengineSubTable = _tDengineDb + "." + _tDengineTable;
    // PR #49：TDengine schemaless LP 写入专用 DB，避免与显式 STable `sensor_data` 冲突。
    private const string _tDengineSchemalessDb = "bench_insert_schemaless";
    // 每个 HTTP POST 的行数，避免 taosadapter 默认 16MB body 上限。
    private const int _tDengineSchemalessLpBatch = 100_000;
    private const string _iotdbUrl = "http://localhost:18080";
    private const string _iotdbDatabase = "root.bench_insert";
    private const string _iotdbDevice = _iotdbDatabase + ".server001";
    private const string _timescaleTable = "sensor_data_insert";
    private static readonly string _timescaleConnectionString = TimescaleDbBenchmark.DefaultConnectionString;

    // ── 共享数据 ──────────────────────────────────────────────────────────
    private BenchmarkDataPoint[] _dataPoints = [];

    // ── SQLite ─────────────────────────────────────────────────────────────
    private string _sqliteDbPath = string.Empty;

    // ── LiteDB ─────────────────────────────────────────────────────────────
    private string _liteDbPath = string.Empty;
    private LiteDbDataPoint[] _liteDbPoints = [];

    // ── InfluxDB ───────────────────────────────────────────────────────────
    private InfluxDBClient? _influxClient;
    private bool _influxAvailable;

    // ── TDengine ──────────────────────────────────────────────────────────
    private TDengineRestClient? _tdengineClient;
    private bool _tdengineAvailable;
    // PR #49：TDengine schemaless（InfluxDB 兼容端点）预生成的 LP payload 分批。
    private string[] _tdengineLpChunks = [];

    // ── IoTDB ─────────────────────────────────────────────────────────────
    private IoTDBRestClient? _iotdbClient;
    private bool _iotdbAvailable;

    // ── TimescaleDB ───────────────────────────────────────────────────────
    private bool _timescaleAvailable;

    // ── SonnetDB ─────────────────────────────────────────────────────────────
    private string _sonnetDbRootDir = string.Empty;
    private Tsdb? _sonnetDbDb;
    private Point[] _sonnetDbPoints = [];
    private static readonly IReadOnlyDictionary<string, string> _sonnetDbTags =
        new Dictionary<string, string> { ["host"] = "server001" };

    // ─────────────────────────────────────────────────────────────────────
    // GlobalSetup：生成数据 + 建立数据库 Schema
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>全局初始化：生成测试数据并创建各数据库的 Schema。</summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _dataPoints = DataGenerator.Generate(_dataPointCount);

        // ── SQLite ─────────────────────────────────────────────────────
        _sqliteDbPath = Path.Combine(Path.GetTempPath(), $"sonnetdb_bench_insert_{Guid.NewGuid():N}.db");
        using var conn = OpenSqlite(_sqliteDbPath);
        SqliteExecute(conn, "CREATE TABLE IF NOT EXISTS sensor_data " +
                            "(ts INTEGER NOT NULL, host TEXT NOT NULL, value REAL NOT NULL)");

        // ── LiteDB：预构建文档数组，插入迭代时从空库批量写入 ─────────────
        _liteDbPoints = LiteDbBenchmark.CreatePoints(_dataPoints);

        // ── InfluxDB ────────────────────────────────────────────────────
        try
        {
            _influxClient = new InfluxDBClient(_influxUrl, _influxToken);
            _influxAvailable = await _influxClient.PingAsync().ConfigureAwait(false);
            if (_influxAvailable)
                await EnsureInfluxBucketAsync().ConfigureAwait(false);
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
            // PR #49：schemaless 专用 DB（被 InfluxDB-compat /influxdb/v1/write 自动建 STable）
            await _tdengineClient.ExecuteAsync(
                $"CREATE DATABASE IF NOT EXISTS {_tDengineSchemalessDb} PRECISION 'ms'")
                .ConfigureAwait(false);
            // PR #49：预生成 LP 分片，避免迭代内字符串拼接
            _tdengineLpChunks = BuildLineProtocolChunks(_dataPoints, _tDengineSchemalessLpBatch);
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
        if (!_timescaleAvailable)
        {
            Console.Error.WriteLine(
                "[SKIP] TimescaleDB 不可用。请先执行: docker compose -f tests/SonnetDB.Benchmarks/docker/docker-compose.yml up -d timescaledb");
        }

        // ── SonnetDB：预构建 Point 数组（共享 tags，避免每次迭代重新分配） ────
        _sonnetDbPoints = new Point[_dataPointCount];
        for (int i = 0; i < _dataPointCount; i++)
        {
            var dp = _dataPoints[i];
            _sonnetDbPoints[i] = Point.Create(
                "sensor_data",
                dp.Timestamp,
                _sonnetDbTags,
                new Dictionary<string, FieldValue> { ["value"] = FieldValue.FromDouble(dp.Value) });
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // IterationSetup：清空上一轮写入的数据
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>每次迭代前清空各数据库中的数据，确保每轮测量均从零开始。</summary>
    [IterationSetup]
    public void IterationSetup()
    {
        // SQLite
        using var conn = OpenSqlite(_sqliteDbPath);
        SqliteExecute(conn, "DELETE FROM sensor_data");

        // LiteDB
        LiteDbBenchmark.DeleteDatabaseFiles(_liteDbPath);
        _liteDbPath = LiteDbBenchmark.CreateTempPath("insert");

        // InfluxDB
        if (_influxAvailable)
        {
            _influxClient!.GetDeleteApi().Delete(
                    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(_dataPointCount + 1),
                    string.Empty, _influxBucket, _influxOrg)
                .GetAwaiter().GetResult();
        }

        // TDengine
        if (_tdengineAvailable)
        {
            _tdengineClient!.ExecuteAsync($"DELETE FROM {_tDengineSubTable}").GetAwaiter().GetResult();
            // PR #49：清空 schemaless DB（drop + recreate；schemaless 的 STable 由首次写入自动建立）
            _tdengineClient!.ExecuteAsync($"DROP DATABASE IF EXISTS {_tDengineSchemalessDb}").GetAwaiter().GetResult();
            _tdengineClient!.ExecuteAsync(
                $"CREATE DATABASE IF NOT EXISTS {_tDengineSchemalessDb} PRECISION 'ms'").GetAwaiter().GetResult();
        }

        // IoTDB
        if (_iotdbAvailable)
            _iotdbClient!.PrepareSeriesAsync(_iotdbDatabase, _iotdbDevice).GetAwaiter().GetResult();

        // TimescaleDB
        if (_timescaleAvailable)
            TimescaleDbBenchmark.PrepareSensorTableAsync(_timescaleConnectionString, _timescaleTable)
                .GetAwaiter().GetResult();

        // SonnetDB：关闭旧实例、清空目录、重新打开
        _sonnetDbDb?.Dispose();
        _sonnetDbDb = null;
        if (!string.IsNullOrEmpty(_sonnetDbRootDir) && Directory.Exists(_sonnetDbRootDir))
            Directory.Delete(_sonnetDbRootDir, recursive: true);
        _sonnetDbRootDir = Path.Combine(Path.GetTempPath(), $"sonnetdb_bench_insert_{Guid.NewGuid():N}");
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
    }

    // ─────────────────────────────────────────────────────────────────────
    // Benchmark 方法
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// SonnetDB 写入 100 万条（真实引擎：MemTable + WAL + Flush）。
    /// 每次迭代均从空的 Tsdb 实例写入，最后调用 FlushNow 将数据落盘。
    /// </summary>
    [Benchmark(Baseline = true, Description = "SonnetDB 写入 100万条")]
    public void SonnetDB_Insert_1M()
    {
        _sonnetDbDb!.WriteMany(_sonnetDbPoints);
        _sonnetDbDb!.FlushNow();
    }

    /// <summary>SQLite 写入 100 万条（文件模式，WAL 日志，事务批量提交）。</summary>
    [Benchmark(Description = "SQLite 写入 100万条")]
    public void SQLite_Insert_1M()
    {
        using var conn = OpenSqlite(_sqliteDbPath);
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO sensor_data (ts, host, value) VALUES (@ts, @host, @val)";
        var tsParam = cmd.Parameters.AddWithValue("@ts", 0L);
        var hostParam = cmd.Parameters.AddWithValue("@host", string.Empty);
        var valParam = cmd.Parameters.AddWithValue("@val", 0.0);
        cmd.Prepare();

        foreach (var dp in _dataPoints)
        {
            tsParam.Value = dp.Timestamp;
            hostParam.Value = dp.Host;
            valParam.Value = dp.Value;
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>LiteDB 写入 100 万条（单文件文档数据库，InsertBulk 批量写入）。</summary>
    [Benchmark(Description = "LiteDB 写入 100万条")]
    public void LiteDB_Insert_1M()
    {
        using var db = LiteDbBenchmark.Open(_liteDbPath);
        LiteDbBenchmark.InsertBulk(db, _liteDbPoints, ensureQueryIndexes: false);
    }

    /// <summary>InfluxDB 写入 100 万条（Line Protocol，10k 批次）。</summary>
    [Benchmark(Description = "InfluxDB 写入 100万条")]
    public async Task InfluxDB_Insert_1M()
    {
        if (!_influxAvailable)
        {
            Console.Error.WriteLine("[SKIP] InfluxDB 不可用");
            return;
        }

        const int batchSize = 10_000;
        var writeApi = _influxClient!.GetWriteApiAsync();

        for (int offset = 0; offset < _dataPoints.Length; offset += batchSize)
        {
            int end = Math.Min(offset + batchSize, _dataPoints.Length);
            var batch = new PointData[end - offset];
            for (int i = 0; i < batch.Length; i++)
            {
                var dp = _dataPoints[offset + i];
                batch[i] = PointData.Measurement("sensor_data")
                    .Tag("host", dp.Host)
                    .Field("value", dp.Value)
                    .Timestamp(dp.Timestamp, InfluxDB.Client.Api.Domain.WritePrecision.Ms);
            }

            await writeApi.WritePointsAsync(batch, _influxBucket, _influxOrg).ConfigureAwait(false);
        }
    }

    /// <summary>TDengine 写入 100 万条（REST API，1k 批次）。</summary>
    [Benchmark(Description = "TDengine 写入 100万条")]
    public async Task TDengine_Insert_1M()
    {
        if (!_tdengineAvailable)
        {
            Console.Error.WriteLine("[SKIP] TDengine 不可用");
            return;
        }

        await _tdengineClient!.BulkInsertAsync(_tDengineSubTable, _dataPoints).ConfigureAwait(false);
    }

    /// <summary>
    /// PR #49：TDengine 写入 100 万条（schemaless / InfluxDB-compat Line Protocol，每批 100k 行）。
    /// 走 <c>POST /influxdb/v1/write?db=...&amp;precision=ms</c>，由 TDengine taosadapter 自动建 STable。
    /// </summary>
    [Benchmark(Description = "TDengine 写入 100万条 (schemaless LP)")]
    public async Task TDengine_InsertSchemaless_1M()
    {
        if (!_tdengineAvailable)
        {
            Console.Error.WriteLine("[SKIP] TDengine 不可用");
            return;
        }

        for (int i = 0; i < _tdengineLpChunks.Length; i++)
        {
            await _tdengineClient!.WriteLineProtocolAsync(
                _tDengineSchemalessDb, _tdengineLpChunks[i], precision: "ms").ConfigureAwait(false);
        }
    }

    /// <summary>Apache IoTDB 写入 100 万条（REST v2 insertTablet，10k 行/批）。</summary>
    [Benchmark(Description = "IoTDB 写入 100万条")]
    public async Task IoTDB_Insert_1M()
    {
        if (!_iotdbAvailable)
        {
            Console.Error.WriteLine("[SKIP] IoTDB 不可用");
            return;
        }

        await _iotdbClient!.InsertTabletAsync(_iotdbDevice, _dataPoints).ConfigureAwait(false);
    }

    /// <summary>PostgreSQL/TimescaleDB 写入 100 万条（binary COPY 到 hypertable）。</summary>
    [Benchmark(Description = "TimescaleDB 写入 100万条")]
    public async Task TimescaleDB_Insert_1M()
    {
        if (!_timescaleAvailable)
        {
            Console.Error.WriteLine("[SKIP] TimescaleDB 不可用");
            return;
        }

        await TimescaleDbBenchmark.BulkCopyAsync(
            _timescaleConnectionString,
            _timescaleTable,
            _dataPoints).ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────
    // GlobalCleanup
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>全局清理：删除测试数据库文件及外部数据库中的 Schema。</summary>
    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        // SQLite
        SqliteConnection.ClearAllPools();
        if (File.Exists(_sqliteDbPath))
            File.Delete(_sqliteDbPath);
        // LiteDB
        LiteDbBenchmark.DeleteDatabaseFiles(_liteDbPath);
        // InfluxDB：仅删除数据，保留 bucket，避免后续基准进程因 bucket 不存在而失败。
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

        // TDengine
        if (_tdengineAvailable)
        {
            try
            {
                await _tdengineClient!.ExecuteAsync($"DROP DATABASE IF EXISTS {_tDengineDb}")
                    .ConfigureAwait(false);
                // PR #49：清理 schemaless DB
                await _tdengineClient!.ExecuteAsync($"DROP DATABASE IF EXISTS {_tDengineSchemalessDb}")
                    .ConfigureAwait(false);
            }
            catch { /* 清理失败不影响结果 */ }

            _tdengineClient!.Dispose();
        }

        // IoTDB
        if (_iotdbAvailable)
        {
            await _iotdbClient!.DropDatabaseIfExistsAsync(_iotdbDatabase).ConfigureAwait(false);
            _iotdbClient!.Dispose();
        }

        // TimescaleDB
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

    /// <summary>
    /// PR #49：把 <paramref name="points"/> 按 <paramref name="batchSize"/> 行切成多段
    /// InfluxDB Line Protocol payload，使用 <c>sensor_data,host=&lt;host&gt; value=&lt;value&gt; &lt;ts&gt;</c> 格式，
    /// 时间戳单位与 schemaless 端点的 <c>precision=ms</c> 对齐。
    /// </summary>
    private static string[] BuildLineProtocolChunks(BenchmarkDataPoint[] points, int batchSize)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        int chunks = (points.Length + batchSize - 1) / batchSize;
        var result = new string[chunks];
        var sb = new System.Text.StringBuilder(batchSize * 50);
        for (int c = 0; c < chunks; c++)
        {
            int start = c * batchSize;
            int end = Math.Min(start + batchSize, points.Length);
            sb.Clear();
            for (int i = start; i < end; i++)
            {
                var dp = points[i];
                sb.Append("sensor_data,host=").Append(dp.Host)
                  .Append(" value=").Append(dp.Value.ToString("G17", CultureInfo.InvariantCulture))
                  .Append(' ').Append(dp.Timestamp).Append('\n');
            }
            result[c] = sb.ToString();
        }
        return result;
    }

    // ─────────────────────────────────────────────────────────────────────
    // BenchmarkDotNet 配置
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 插入基准采用监控模式（RunStrategy.Monitoring）：
    /// 每轮直接测量一次完整 100 万条写入，避免吞吐量受到多轮迭代干扰。
    /// </summary>
    private sealed class InsertConfig : ManualConfig
    {
        public InsertConfig()
        {
            AddJob(Job.Default
                .WithStrategy(RunStrategy.Monitoring)
                .WithWarmupCount(0)
                .WithIterationCount(3));
        }
    }
}
