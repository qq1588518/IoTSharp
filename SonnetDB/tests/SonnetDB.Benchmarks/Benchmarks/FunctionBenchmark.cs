using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using InfluxDB.Client;
using InfluxDB.Client.Writes;
using SonnetDB.Benchmarks.Helpers;
using SonnetDB.Engine;
using SonnetDB.Engine.Compaction;
using SonnetDB.Engine.Retention;
using SonnetDB.Memory;
using SonnetDB.Sql;
using SonnetDB.Sql.Execution;

namespace SonnetDB.Benchmarks.Benchmarks;

/// <summary>
/// PR #57 — SonnetDB 内置函数基准。
/// 跟踪 PR #50~#56 引入的函数族（标量 / 聚合 / 窗口 / TVF）的 SQL 端到端性能，
/// 并与 InfluxDB Flux、TDengine 提供的等价语义做对比（仅在外部数据库可用时启用）。
/// </summary>
/// <remarks>
/// 数据规模 50,000 个点（单 series，每秒一个点）。函数路径走完整 SqlParser → SqlExecutor 流水线，
/// 与 <see cref="AggregateBenchmark"/> 的 100 万点全表聚合区分开，专注函数算法本身的开销。
/// 外部数据库不可用时仅打印 [SKIP] 信息，不阻塞 SonnetDB 自身的基准。
/// </remarks>
[MemoryDiagnoser]
[BenchmarkCategory("Function")]
public class FunctionBenchmark
{
    // ── 配置 ──────────────────────────────────────────────────────────────
    private const int _dataPointCount = 50_000;
    private const string _influxUrl = "http://localhost:8086";
    private const string _influxToken = "my-super-secret-auth-token";
    private const string _influxOrg = "sndb";
    private const string _influxBucket = "benchmarks_fn";
    private const string _tDengineUrl = "http://localhost:6041";
    private const string _tDengineDb = "bench_function";
    private const string _tDengineSubTable = _tDengineDb + ".sd_server001";

    // ── 共享 ─────────────────────────────────────────────────────────────
    private BenchmarkDataPoint[] _dataPoints = [];

    // ── SonnetDB ───────────────────────────────────────────────────────────
    private string _sonnetDbRootDir = string.Empty;
    private Tsdb? _sonnetDbDb;

    // ── InfluxDB ─────────────────────────────────────────────────────────
    private InfluxDBClient? _influxClient;
    private bool _influxAvailable;
    private string _influxStartRfc3339 = string.Empty;
    private string _influxStopRfc3339 = string.Empty;

    // ── TDengine ─────────────────────────────────────────────────────────
    private TDengineRestClient? _tdengineClient;
    private bool _tdengineAvailable;

    /// <summary>全局初始化：写入 50,000 条数据到各数据库。</summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _dataPoints = DataGenerator.Generate(_dataPointCount);

        // ── SonnetDB ─────────────────────────────────────────────────────
        _sonnetDbRootDir = Path.Combine(Path.GetTempPath(), $"sonnetdb_bench_function_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sonnetDbRootDir);
        _sonnetDbDb = Tsdb.Open(new TsdbOptions
        {
            RootDirectory = _sonnetDbRootDir,
            FlushPolicy = new MemTableFlushPolicy
            {
                MaxBytes = long.MaxValue,
                MaxPoints = int.MaxValue,
                MaxAge = TimeSpan.MaxValue,
            },
            BackgroundFlush = new BackgroundFlushOptions { Enabled = false },
            Compaction = new CompactionPolicy { Enabled = false },
            Retention = new RetentionPolicy { Enabled = false },
        });
        SqlExecutor.Execute(_sonnetDbDb,
            "CREATE MEASUREMENT sensor_data (host TAG, value FIELD FLOAT)");

        // 批量插入：拼成大 INSERT 减少 parser 开销
        const int insertBatch = 5_000;
        var sb = new StringBuilder(64 * insertBatch);
        for (int offset = 0; offset < _dataPoints.Length; offset += insertBatch)
        {
            int end = Math.Min(offset + insertBatch, _dataPoints.Length);
            sb.Clear();
            sb.Append("INSERT INTO sensor_data (time, host, value) VALUES ");
            for (int i = offset; i < end; i++)
            {
                if (i > offset) sb.Append(", ");
                var dp = _dataPoints[i];
                sb.Append('(').Append(dp.Timestamp).Append(", '").Append(dp.Host).Append("', ")
                  .Append(dp.Value.ToString("G17", CultureInfo.InvariantCulture)).Append(')');
            }
            SqlExecutor.Execute(_sonnetDbDb, sb.ToString());
        }
        _sonnetDbDb.FlushNow();

        // ── InfluxDB ──────────────────────────────────────────────────
        _influxStartRfc3339 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        _influxStopRfc3339 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddSeconds(_dataPointCount + 1)
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
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

        // ── TDengine ───────────────────────────────────────────────────
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
    }

    // ─────────────────────────────────────────────────────────────────────
    // SonnetDB 基准方法（基线）
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>SonnetDB 窗口函数 <c>derivative(value, 1s)</c>：50k 点逐行差分率。</summary>
    [Benchmark(Baseline = true, Description = "SonnetDB derivative(50k)")]
    public int SonnetDB_Derivative()
    {
        var r = (SelectExecutionResult)SqlExecutor.Execute(_sonnetDbDb!,
            "SELECT derivative(value, 1s) FROM sensor_data")!;
        return r.Rows.Count;
    }

    /// <summary>SonnetDB 窗口函数 <c>moving_average(value, 60)</c>：60 点滑动平均。</summary>
    [Benchmark(Description = "SonnetDB moving_average(50k,60)")]
    public int SonnetDB_MovingAverage()
    {
        var r = (SelectExecutionResult)SqlExecutor.Execute(_sonnetDbDb!,
            "SELECT moving_average(value, 60) FROM sensor_data")!;
        return r.Rows.Count;
    }

    /// <summary>SonnetDB 窗口函数 <c>ewma(value, 0.2)</c>：指数加权移动平均。</summary>
    [Benchmark(Description = "SonnetDB ewma(50k,0.2)")]
    public int SonnetDB_Ewma()
    {
        var r = (SelectExecutionResult)SqlExecutor.Execute(_sonnetDbDb!,
            "SELECT ewma(value, 0.2) FROM sensor_data")!;
        return r.Rows.Count;
    }

    /// <summary>SonnetDB 窗口函数 <c>holt_winters(value, 0.4, 0.1)</c>：Holt 双指数平滑（无季节）。</summary>
    [Benchmark(Description = "SonnetDB holt_winters(50k)")]
    public int SonnetDB_HoltWinters()
    {
        var r = (SelectExecutionResult)SqlExecutor.Execute(_sonnetDbDb!,
            "SELECT holt_winters(value, 0.4, 0.1) FROM sensor_data")!;
        return r.Rows.Count;
    }

    /// <summary>SonnetDB 窗口函数 <c>anomaly(value, 'zscore', 2.5)</c>：z-score 异常检测。</summary>
    [Benchmark(Description = "SonnetDB anomaly_zscore(50k)")]
    public int SonnetDB_AnomalyZScore()
    {
        var r = (SelectExecutionResult)SqlExecutor.Execute(_sonnetDbDb!,
            "SELECT time, anomaly(value, 'zscore', 2.5) FROM sensor_data")!;
        return r.Rows.Count;
    }

    /// <summary>SonnetDB 聚合函数 <c>p99(value)</c>：T-Digest 分位估计。</summary>
    [Benchmark(Description = "SonnetDB p99(50k)")]
    public int SonnetDB_P99()
    {
        var r = (SelectExecutionResult)SqlExecutor.Execute(_sonnetDbDb!,
            "SELECT p99(value) FROM sensor_data")!;
        return r.Rows.Count;
    }

    /// <summary>SonnetDB 聚合函数 <c>distinct_count(value)</c>：HyperLogLog 基数估计。</summary>
    [Benchmark(Description = "SonnetDB distinct_count(50k)")]
    public int SonnetDB_DistinctCount()
    {
        var r = (SelectExecutionResult)SqlExecutor.Execute(_sonnetDbDb!,
            "SELECT distinct_count(value) FROM sensor_data")!;
        return r.Rows.Count;
    }

    /// <summary>SonnetDB 表值函数 <c>forecast(sensor_data, value, 100, 'linear')</c>：100 步线性外推。</summary>
    [Benchmark(Description = "SonnetDB forecast_linear(50k→100)")]
    public int SonnetDB_ForecastLinear()
    {
        var r = (SelectExecutionResult)SqlExecutor.Execute(_sonnetDbDb!,
            "SELECT * FROM forecast(sensor_data, value, 100, 'linear')")!;
        return r.Rows.Count;
    }

    /// <summary>SonnetDB 表值函数 <c>forecast(sensor_data, value, 24, 'holt_winters', 60)</c>：季节预测。</summary>
    [Benchmark(Description = "SonnetDB forecast_hw(50k→24)")]
    public int SonnetDB_ForecastHoltWinters()
    {
        var r = (SelectExecutionResult)SqlExecutor.Execute(_sonnetDbDb!,
            "SELECT * FROM forecast(sensor_data, value, 24, 'holt_winters', 60)")!;
        return r.Rows.Count;
    }

    // ─────────────────────────────────────────────────────────────────────
    // InfluxDB 对照（仅在 InfluxDB 可用时执行）
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>InfluxDB Flux <c>derivative</c>。</summary>
    [Benchmark(Description = "InfluxDB derivative(50k)")]
    public async Task<int> InfluxDB_Derivative()
    {
        if (!_influxAvailable) { Console.Error.WriteLine("[SKIP] InfluxDB 不可用"); return -1; }
        var flux = $"""
            from(bucket: "{_influxBucket}")
              |> range(start: {_influxStartRfc3339}, stop: {_influxStopRfc3339})
              |> filter(fn: (r) => r["_measurement"] == "sensor_data")
              |> derivative(unit: 1s, nonNegative: false)
            """;
        var tables = await _influxClient!.GetQueryApi().QueryAsync(flux, _influxOrg).ConfigureAwait(false);
        return tables.Sum(t => t.Records.Count);
    }

    /// <summary>InfluxDB Flux <c>movingAverage</c>。</summary>
    [Benchmark(Description = "InfluxDB movingAverage(50k,60)")]
    public async Task<int> InfluxDB_MovingAverage()
    {
        if (!_influxAvailable) { Console.Error.WriteLine("[SKIP] InfluxDB 不可用"); return -1; }
        var flux = $"""
            from(bucket: "{_influxBucket}")
              |> range(start: {_influxStartRfc3339}, stop: {_influxStopRfc3339})
              |> filter(fn: (r) => r["_measurement"] == "sensor_data")
              |> movingAverage(n: 60)
            """;
        var tables = await _influxClient!.GetQueryApi().QueryAsync(flux, _influxOrg).ConfigureAwait(false);
        return tables.Sum(t => t.Records.Count);
    }

    /// <summary>InfluxDB Flux <c>holtWinters</c>（experimental）。</summary>
    [Benchmark(Description = "InfluxDB holtWinters(50k)")]
    public async Task<int> InfluxDB_HoltWinters()
    {
        if (!_influxAvailable) { Console.Error.WriteLine("[SKIP] InfluxDB 不可用"); return -1; }
        var flux = $"""
            from(bucket: "{_influxBucket}")
              |> range(start: {_influxStartRfc3339}, stop: {_influxStopRfc3339})
              |> filter(fn: (r) => r["_measurement"] == "sensor_data")
              |> holtWinters(n: 24, seasonality: 0, interval: 1s)
            """;
        var tables = await _influxClient!.GetQueryApi().QueryAsync(flux, _influxOrg).ConfigureAwait(false);
        return tables.Sum(t => t.Records.Count);
    }

    /// <summary>InfluxDB Flux <c>quantile(q: 0.99)</c>，对照 SonnetDB p99。</summary>
    [Benchmark(Description = "InfluxDB quantile_p99(50k)")]
    public async Task<int> InfluxDB_QuantileP99()
    {
        if (!_influxAvailable) { Console.Error.WriteLine("[SKIP] InfluxDB 不可用"); return -1; }
        var flux = $"""
            from(bucket: "{_influxBucket}")
              |> range(start: {_influxStartRfc3339}, stop: {_influxStopRfc3339})
              |> filter(fn: (r) => r["_measurement"] == "sensor_data")
              |> quantile(q: 0.99, method: "estimate_tdigest")
            """;
        var tables = await _influxClient!.GetQueryApi().QueryAsync(flux, _influxOrg).ConfigureAwait(false);
        return tables.Sum(t => t.Records.Count);
    }

    // ─────────────────────────────────────────────────────────────────────
    // TDengine 对照
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>TDengine <c>DERIVATIVE(value, 1s, 0)</c>。</summary>
    [Benchmark(Description = "TDengine DERIVATIVE(50k)")]
    public async Task<string> TDengine_Derivative()
    {
        if (!_tdengineAvailable) { Console.Error.WriteLine("[SKIP] TDengine 不可用"); return string.Empty; }
        return await _tdengineClient!.ExecuteAsync(
            $"SELECT DERIVATIVE(`value`, 1s, 0) FROM {_tDengineSubTable}").ConfigureAwait(false);
    }

    /// <summary>TDengine <c>MAVG(value, 60)</c> 滑动平均。</summary>
    [Benchmark(Description = "TDengine MAVG(50k,60)")]
    public async Task<string> TDengine_MovingAverage()
    {
        if (!_tdengineAvailable) { Console.Error.WriteLine("[SKIP] TDengine 不可用"); return string.Empty; }
        return await _tdengineClient!.ExecuteAsync(
            $"SELECT MAVG(`value`, 60) FROM {_tDengineSubTable}").ConfigureAwait(false);
    }

    /// <summary>TDengine <c>PERCENTILE(value, 99)</c>。</summary>
    [Benchmark(Description = "TDengine PERCENTILE_99(50k)")]
    public async Task<string> TDengine_PercentileP99()
    {
        if (!_tdengineAvailable) { Console.Error.WriteLine("[SKIP] TDengine 不可用"); return string.Empty; }
        return await _tdengineClient!.ExecuteAsync(
            $"SELECT PERCENTILE(`value`, 99) FROM {_tDengineSubTable}").ConfigureAwait(false);
    }

    // ─────────────────────────────────────────────────────────────────────
    // GlobalCleanup
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>全局清理。</summary>
    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        _sonnetDbDb?.Dispose();
        _sonnetDbDb = null;
        if (!string.IsNullOrEmpty(_sonnetDbRootDir) && Directory.Exists(_sonnetDbRootDir))
            Directory.Delete(_sonnetDbRootDir, recursive: true);

        if (_influxAvailable)
        {
            try
            {
                _influxClient!.GetDeleteApi().Delete(
                    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(_dataPointCount + 1),
                    string.Empty, _influxBucket, _influxOrg).GetAwaiter().GetResult();
            }
            catch { /* ignore */ }
            _influxClient!.Dispose();
        }

        if (_tdengineAvailable)
        {
            try
            {
                await _tdengineClient!.ExecuteAsync($"DROP DATABASE IF EXISTS {_tDengineDb}")
                    .ConfigureAwait(false);
            }
            catch { /* ignore */ }
            _tdengineClient!.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // InfluxDB 辅助
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
