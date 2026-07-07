using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using SonnetDB.Benchmarks.Helpers;
using SonnetDB.Engine;
using SonnetDB.Engine.Compaction;
using SonnetDB.Engine.Retention;
using SonnetDB.Ingest;
using SonnetDB.Memory;
using SonnetDB.Sql;
using SonnetDB.Sql.Ast;
using SonnetDB.Sql.Execution;

namespace SonnetDB.Benchmarks.Benchmarks;

/// <summary>
/// 嵌入式批量入库基准（PR #45）：对比四种把 100 000 条数据点写入同一 <see cref="Tsdb"/> 的方式：
/// <list type="bullet">
///   <item>SQL 路径：每批 100 行的 <c>INSERT INTO ... VALUES (...)</c>，共 1 000 条 SQL，
///         走完整的 <see cref="SqlParser"/> + <see cref="SqlExecutor"/> 流水线（基线）。</item>
///   <item>TableDirect Line Protocol：<see cref="LineProtocolReader"/> + <see cref="BulkIngestor"/>。</item>
///   <item>TableDirect JSON：<see cref="JsonPointsReader"/> + <see cref="BulkIngestor"/>。</item>
///   <item>TableDirect Bulk VALUES：<see cref="BulkValuesReader"/>（绕开 SQL parser 但仍走 VALUES 语法）。</item>
/// </list>
/// 目标：量化「绕开 SQL parser」对纯写入路径的提升。
/// </summary>
[Config(typeof(BulkIngestConfig))]
[MemoryDiagnoser]
[BenchmarkCategory("BulkIngest")]
public class BulkIngestBenchmark
{
    private const int _dataPointCount = 100_000;
    private const int _rowsPerStatement = 100;

    private BenchmarkDataPoint[] _dataPoints = [];

    /// <summary>预先构造好的 SQL 语句序列（每条含 <see cref="_rowsPerStatement"/> 行 VALUES）。</summary>
    private string[] _sqlStatements = [];
    private string _lpPayload = string.Empty;
    private string _jsonPayload = string.Empty;
    private string _bulkValuesPayload = string.Empty;

    private string _rootDir = string.Empty;
    private Tsdb? _tsdb;

    /// <summary>全局初始化：生成数据并预先把四种 payload 序列化为字符串，避免在迭代内计入字符串拼接耗时。</summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        _dataPoints = DataGenerator.Generate(_dataPointCount);

        // ── SQL：每 100 行一条 INSERT VALUES，共 1000 条 ─────────────────
        var sqls = new List<string>(_dataPointCount / _rowsPerStatement);
        for (int offset = 0; offset < _dataPoints.Length; offset += _rowsPerStatement)
        {
            int end = Math.Min(offset + _rowsPerStatement, _dataPoints.Length);
            var sb = new StringBuilder("INSERT INTO sensor_data(host, value, time) VALUES ", capacity: 64 + _rowsPerStatement * 48);
            for (int i = offset; i < end; i++)
            {
                if (i > offset) sb.Append(',');
                var dp = _dataPoints[i];
                // 强制 4 位小数，避免数值偶尔准整被 parser 当作 Int64（列 schema 为 Float64会报 type mismatch）。
                sb.Append(CultureInfo.InvariantCulture, $"('{dp.Host}',{dp.Value:F4},{dp.Timestamp})");
            }
            sqls.Add(sb.ToString());
        }
        _sqlStatements = sqls.ToArray();

        // ── Line Protocol ────────────────────────────────────────────────
        var lp = new StringBuilder(capacity: _dataPointCount * 50);
        for (int i = 0; i < _dataPoints.Length; i++)
        {
            var dp = _dataPoints[i];
            lp.Append(CultureInfo.InvariantCulture, $"sensor_data,host={dp.Host} value={dp.Value:F4} {dp.Timestamp}\n");
        }
        _lpPayload = lp.ToString();

        // ── JSON ────────────────────────────────────────────────────────
        var json = new StringBuilder(capacity: _dataPointCount * 80);
        json.Append("{\"m\":\"sensor_data\",\"points\":[");
        for (int i = 0; i < _dataPoints.Length; i++)
        {
            if (i > 0) json.Append(',');
            var dp = _dataPoints[i];
            json.Append(CultureInfo.InvariantCulture,
                $"{{\"t\":{dp.Timestamp},\"tags\":{{\"host\":\"{dp.Host}\"}},\"fields\":{{\"value\":{dp.Value:F4}}}}}");
        }
        json.Append("]}");
        _jsonPayload = json.ToString();

        // ── Bulk VALUES（一条超长 INSERT INTO ... VALUES ...）────────────
        var bulk = new StringBuilder(capacity: 64 + _dataPointCount * 48);
        bulk.Append("INSERT INTO sensor_data(host, value, time) VALUES ");
        for (int i = 0; i < _dataPoints.Length; i++)
        {
            if (i > 0) bulk.Append(',');
            var dp = _dataPoints[i];
            bulk.Append(CultureInfo.InvariantCulture, $"('{dp.Host}',{dp.Value:F4},{dp.Timestamp})");
        }
        _bulkValuesPayload = bulk.ToString();
    }

    /// <summary>每次迭代前重建空 <see cref="Tsdb"/> 实例。</summary>
    [IterationSetup]
    public void IterationSetup()
    {
        _tsdb?.Dispose();
        _tsdb = null;
        if (!string.IsNullOrEmpty(_rootDir) && Directory.Exists(_rootDir))
            Directory.Delete(_rootDir, recursive: true);

        _rootDir = Path.Combine(Path.GetTempPath(), $"sonnetdb_bulk_bench_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDir);
        _tsdb = Tsdb.Open(new TsdbOptions
        {
            RootDirectory = _rootDir,
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

        // CREATE MEASUREMENT（每轮新库都需要重新建）
        var ddl = (CreateMeasurementStatement)SqlParser.Parse(
            "CREATE MEASUREMENT sensor_data (host TAG, value FIELD FLOAT)");
        SqlExecutor.ExecuteCreateMeasurement(_tsdb, ddl);
    }

    /// <summary>SQL 路径基线：1000 条 <c>INSERT INTO ... VALUES (...)</c>，每条 100 行，全部走 SqlParser。</summary>
    [Benchmark(Baseline = true, Description = "SQL INSERT VALUES (100 行/条 × 1000 条)")]
    public void Sql_Insert_100k()
    {
        for (int i = 0; i < _sqlStatements.Length; i++)
        {
            var stmt = (InsertStatement)SqlParser.Parse(_sqlStatements[i]);
            SqlExecutor.ExecuteInsert(_tsdb!, stmt);
        }
        _tsdb!.FlushNow();
    }

    /// <summary>TableDirect Line Protocol：<see cref="LineProtocolReader"/> + <see cref="BulkIngestor"/>。</summary>
    [Benchmark(Description = "TableDirect Line Protocol")]
    public void Bulk_LineProtocol_100k()
    {
        var reader = new LineProtocolReader(_lpPayload.AsMemory());
        BulkIngestor.Ingest(_tsdb!, reader, BulkErrorPolicy.FailFast, flushOnComplete: true);
    }

    /// <summary>TableDirect JSON：<see cref="JsonPointsReader"/> + <see cref="BulkIngestor"/>。</summary>
    [Benchmark(Description = "TableDirect JSON")]
    public void Bulk_Json_100k()
    {
        using var reader = new JsonPointsReader(_jsonPayload.AsMemory());
        BulkIngestor.Ingest(_tsdb!, reader, BulkErrorPolicy.FailFast, flushOnComplete: true);
    }

    /// <summary>TableDirect Bulk VALUES：<see cref="BulkValuesReader"/>（绕开 SqlParser 但仍走 VALUES 语法）。</summary>
    [Benchmark(Description = "TableDirect Bulk VALUES")]
    public void Bulk_Values_100k()
    {
        var reader = SchemaBoundBulkValuesReader.Create(_tsdb!, _bulkValuesPayload);
        BulkIngestor.Ingest(_tsdb!, reader, BulkErrorPolicy.FailFast, flushOnComplete: true);
    }

    /// <summary>全局清理。</summary>
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _tsdb?.Dispose();
        _tsdb = null;
        if (!string.IsNullOrEmpty(_rootDir) && Directory.Exists(_rootDir))
            Directory.Delete(_rootDir, recursive: true);
    }

    private sealed class BulkIngestConfig : ManualConfig
    {
        public BulkIngestConfig()
        {
            AddJob(Job.Default
                .WithStrategy(RunStrategy.Monitoring)
                .WithWarmupCount(1)
                .WithIterationCount(5));
        }
    }
}
