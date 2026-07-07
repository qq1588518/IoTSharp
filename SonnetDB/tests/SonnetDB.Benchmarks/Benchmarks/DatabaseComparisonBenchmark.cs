using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SonnetDB.Benchmarks.Helpers;
using SonnetDB.Catalog;
using SonnetDB.Engine;
using SonnetDB.Engine.Compaction;
using SonnetDB.Engine.Retention;
using SonnetDB.Memory;
using SonnetDB.Model;
using SonnetDB.Storage.Format;

namespace SonnetDB.Benchmarks.Benchmarks;

/// <summary>
/// SonnetDB vs IoTDB 对比基准测试（AB BA AB BA 四轮）
/// 模拟 10 万设备、每设备 30 个测点、一天数据写入测试
/// 不并行运行，逐轮对比性能指标
/// </summary>
public static class DatabaseComparisonBenchmark
{
    private const string TableName = "data";
    private static readonly DateTime StartDate = new DateTime(2026, 04, 01);

    private static readonly DatabaseComparisonOptions DefaultOptions = new()
    {
        RoundCount = 4,
        TimeSlotCount = 12,
        DeviceCount = 1_000,
        FieldCount = 30,
        DeviceBatchSize = 1_000,
        IotDbTabletBatchSize = 12,
        SonnetDbProgressEveryDeviceBatches = 1,
        IotDbProgressEveryDevices = 100
    };

    private static readonly DatabaseComparisonOptions FullOptions = DefaultOptions with
    {
        DeviceCount = 100_000,
        DeviceBatchSize = 10_000,
        SonnetDbProgressEveryDeviceBatches = 10,
        IotDbProgressEveryDevices = 10_000
    };

    private static readonly DatabaseComparisonOptions SmokeOptions = new()
    {
        RoundCount = 1,
        TimeSlotCount = 3,
        DeviceCount = 20,
        FieldCount = 30,
        DeviceBatchSize = 5,
        IotDbTabletBatchSize = 3,
        SonnetDbProgressEveryDeviceBatches = 1,
        IotDbProgressEveryDevices = 5
    };

    public sealed record DatabaseComparisonOptions
    {
        public int RoundCount { get; init; }
        public int TimeSlotCount { get; init; }
        public int DeviceCount { get; init; }
        public int FieldCount { get; init; }
        public int DeviceBatchSize { get; init; }
        public int IotDbTabletBatchSize { get; init; }
        public int SonnetDbProgressEveryDeviceBatches { get; init; }
        public int IotDbProgressEveryDevices { get; init; }

        public long TotalRows => (long)TimeSlotCount * DeviceCount;
        public long TotalFieldValues => TotalRows * FieldCount;

        public void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RoundCount);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(TimeSlotCount);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(DeviceCount);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(FieldCount);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(DeviceBatchSize);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(IotDbTabletBatchSize);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(SonnetDbProgressEveryDeviceBatches);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(IotDbProgressEveryDevices);
        }
    }

    private sealed record PreparedData(
        string[] FieldNames,
        long[] Timestamps,
        IReadOnlyDictionary<string, string>[] TagsByDevice,
        IReadOnlyDictionary<string, FieldValue> Fields,
        double[][] IotDbValues);

    private readonly record struct WriteCounts(long Rows, long FieldValues);

    // 性能收集数据结构
    private class BenchmarkResult
    {
        public string DatabaseName { get; set; } = string.Empty;
        public int RunNumber { get; set; }
        public long TotalMilliseconds { get; set; }
        public long TotalRowsWritten { get; set; }
        public long TotalFieldValuesWritten { get; set; }
        public double ValuesPerSecond => TotalMilliseconds > 0
            ? (TotalFieldValuesWritten * 1000.0 / TotalMilliseconds)
            : 0;
        public string Phase { get; set; } = string.Empty; // "A" 或 "B"
    }

    /// <summary>
    /// 运行对比基准测试
    /// </summary>
    public static Task RunComparison() => RunComparison(DefaultOptions);

    /// <summary>
    /// 运行“Server vs Server”对比：SonnetDB HTTP Server vs IoTDB REST Server。
    /// </summary>
    public static Task RunServerComparison() => RunComparison(DefaultOptions, useServerMode: true);

    /// <summary>
    /// 运行 100,000 设备的完整高基数测试；该模式可能非常耗时。
    /// </summary>
    public static Task RunFullComparison() => RunComparison(FullOptions);

    /// <summary>
    /// 运行小规模真实冒烟测试，用于验证 SonnetDB 和 IoTDB 链路是否可用。
    /// </summary>
    public static Task RunSmokeComparison() => RunComparison(SmokeOptions);

    /// <summary>
    /// 运行“Server vs Server”小规模冒烟测试。
    /// </summary>
    public static Task RunServerSmokeComparison() => RunComparison(SmokeOptions, useServerMode: true);

    /// <summary>
    /// 按指定参数运行对比基准测试。
    /// </summary>
    public static Task RunComparison(DatabaseComparisonOptions options)
        => RunComparison(options, useServerMode: false);

    /// <summary>
    /// 按指定参数运行对比基准测试。
    /// </summary>
    public static async Task RunComparison(DatabaseComparisonOptions options, bool useServerMode)
    {
        options.Validate();

        var sequenceLabel = string.Join(
            ' ',
            Enumerable.Range(0, options.RoundCount).Select(roundIndex => roundIndex % 2 == 0 ? "AB" : "BA"));

        string modeLabel = useServerMode ? "Server vs Server" : "Embedded vs IoTDB Server";
        Console.WriteLine("═════════════════════════════════════════════════════════════════");
        Console.WriteLine($"  SonnetDB vs IoTDB 对比基准测试 ({modeLabel} | {sequenceLabel}，{options.RoundCount} 轮)");
        Console.WriteLine("═════════════════════════════════════════════════════════════════");
        Console.WriteLine($"  设备数: {options.DeviceCount:N0} | 字段/设备: {options.FieldCount:N0} | 时间点: {options.TimeSlotCount:N0}");
        Console.WriteLine($"  行数: {options.TotalRows:N0} | 字段值总数: {options.TotalFieldValues:N0}");
        Console.WriteLine();

        var preparedData = PrepareData(options);
        var results = new List<BenchmarkResult>();
        var runNumber = 1;

        // ── 运行 AB BA AB BA 四轮 ────────────────────────────────────────
        for (int roundIndex = 0; roundIndex < options.RoundCount; roundIndex++)
        {
            var phaseA = roundIndex % 2 == 0 ? "A" : "B";
            var phaseB = phaseA == "A" ? "B" : "A";

            Console.WriteLine($"╔═══ 第 {roundIndex + 1} 轮：{phaseA}{phaseB} ═══╗");
            Console.WriteLine();

            // 第一阶段（A或B）
            Console.WriteLine($"● {phaseA} 阶段开始...");
            var resultA = await RunSingleBenchmark(phaseA, runNumber, options, preparedData, useServerMode).ConfigureAwait(false);
            results.Add(resultA);
            Console.WriteLine($"  耗时: {resultA.TotalMilliseconds}ms | " +
                            $"吞吐量: {resultA.ValuesPerSecond:F0} values/sec");
            Console.WriteLine();

            // 第二阶段（B或A）
            Console.WriteLine($"● {phaseB} 阶段开始...");
            var resultB = await RunSingleBenchmark(phaseB, runNumber, options, preparedData, useServerMode).ConfigureAwait(false);
            results.Add(resultB);
            Console.WriteLine($"  耗时: {resultB.TotalMilliseconds}ms | " +
                            $"吞吐量: {resultB.ValuesPerSecond:F0} values/sec");
            Console.WriteLine();

            runNumber++;
        }

        // ── 输出总结和对比 ────────────────────────────────────────────────
        Console.WriteLine("═════════════════════════════════════════════════════════════════");
        Console.WriteLine("  性能对比总结");
        Console.WriteLine("═════════════════════════════════════════════════════════════════");
        Console.WriteLine();

        PrintComparisonTable(results);
        PrintStatistics(results);
    }

    /// <summary>
    /// 运行单次基准测试（SonnetDB 或 IoTDB）
    /// </summary>
    private static async Task<BenchmarkResult> RunSingleBenchmark(
        string phase,
        int runNumber,
        DatabaseComparisonOptions options,
        PreparedData preparedData,
        bool useServerMode)
    {
        var stopwatch = Stopwatch.StartNew();
        WriteCounts counts = default;

        if (phase == "A")
        {
            // 运行 SonnetDB 测试
            counts = useServerMode
                ? await RunSonnetDbServerBenchmarkAsync(options, preparedData).ConfigureAwait(false)
                : RunSonnetDbBenchmark(options, preparedData);
        }
        else if (phase == "B")
        {
            // 运行 IoTDB 测试
            counts = await RunIoTDbBenchmarkAsync(options, preparedData).ConfigureAwait(false);
        }

        stopwatch.Stop();

        return new BenchmarkResult
        {
            DatabaseName = phase == "A" ? "SonnetDB" : "IoTDB",
            RunNumber = runNumber,
            TotalMilliseconds = stopwatch.ElapsedMilliseconds,
            TotalRowsWritten = counts.Rows,
            TotalFieldValuesWritten = counts.FieldValues,
            Phase = phase
        };
    }

    /// <summary>
    /// SonnetDB 写入测试（来自 Program.cs）
    /// 模拟 10w 设备，每设备 30 测点，一天数据（288 个 5 分钟间隔）
    /// </summary>
    private static WriteCounts RunSonnetDbBenchmark(DatabaseComparisonOptions options, PreparedData preparedData)
    {
        var sonnetDbRootDir = Path.Combine(Path.GetTempPath(), $"sonnetdb_bench_{Guid.NewGuid():N}");
        Directory.CreateDirectory(sonnetDbRootDir);

        try
        {
            using var tsdb = Tsdb.Open(new TsdbOptions
            {
                RootDirectory = sonnetDbRootDir,
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

            // 创建表
            CreateTable(tsdb, TableName, options.FieldCount);

            long totalRows = 0;
            long totalFieldValues = 0;
            var batchWriteStartTime = DateTime.Now;
            var points = new Point[options.DeviceBatchSize];

            // 模拟 10w 设备，每设备 30 测点测试，写入一天数据
            for (int timeSlotIndex = 0; timeSlotIndex < options.TimeSlotCount; timeSlotIndex++)
            {
                var time = StartDate.AddMinutes(5 * timeSlotIndex);
                var ts = preparedData.Timestamps[timeSlotIndex];

                // 模拟每 5 分钟一次数据
                var deviceBatchIndex = 0;
                for (int deviceStartIndex = 0;
                     deviceStartIndex < options.DeviceCount;
                     deviceStartIndex += options.DeviceBatchSize)
                {
                    int currentBatchSize = Math.Min(options.DeviceBatchSize, options.DeviceCount - deviceStartIndex);
                    if (points.Length < currentBatchSize)
                        points = new Point[currentBatchSize];

                    // 每次写一批设备，每个设备一行，字段数与 IoTDB 完全一致。
                    for (int deviceOffset = 0; deviceOffset < currentBatchSize; deviceOffset++)
                    {
                        int deviceIndex = deviceStartIndex + deviceOffset;
                        points[deviceOffset] = Point.Create(
                            TableName,
                            ts,
                            preparedData.TagsByDevice[deviceIndex],
                            preparedData.Fields);
                    }

                    tsdb.WriteMany(points.AsSpan(0, currentBatchSize));
                    totalRows += currentBatchSize;
                    totalFieldValues += (long)currentBatchSize * options.FieldCount;
                    deviceBatchIndex++;

                    if (deviceBatchIndex % options.SonnetDbProgressEveryDeviceBatches == 0)
                    {
                        var progressSeconds = (DateTime.Now - batchWriteStartTime).TotalSeconds;
                        var throughput = totalFieldValues / progressSeconds;
                        Console.WriteLine($"    SonnetDB 进度: {time:yyyy-MM-dd HH:mm:ss} " +
                                        $"设备批次 {deviceBatchIndex:N0} | " +
                                        $"已写 {totalRows:N0} 行/{totalFieldValues:N0} 值 | " +
                                        $"吞吐 {throughput:F0} values/sec");
                    }
                }
            }

            tsdb.FlushNow();
            var finalSeconds = (DateTime.Now - batchWriteStartTime).TotalSeconds;
            var finalThroughput = totalFieldValues / finalSeconds;
            Console.WriteLine($"    SonnetDB 完成: 共写入 {totalRows:N0} 行/{totalFieldValues:N0} 值，耗时 {finalSeconds:F2}s，吞吐 {finalThroughput:F0} values/sec");

            return new WriteCounts(totalRows, totalFieldValues);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] SonnetDB 测试失败: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return default;
        }
        finally
        {
            // 清理临时目录
            try
            {
                if (Directory.Exists(sonnetDbRootDir))
                    Directory.Delete(sonnetDbRootDir, recursive: true);
            }
            catch { }
        }
    }

    /// <summary>
    /// IoTDB 写入测试（通过 REST API v2）
    /// </summary>
    private static async Task<WriteCounts> RunIoTDbBenchmarkAsync(DatabaseComparisonOptions options, PreparedData preparedData)
    {
        const string iotdbUrl = "http://localhost:18080";
        const string database = "root.bench_comparison";

        try
        {
            using var client = new IoTDBRestClient(iotdbUrl);
            // 检查连接
            await client.QueryAsync("SHOW VERSION").ConfigureAwait(false);

            // 准备数据库。IoTDB insertTablet 会按设备自动创建 aligned schema，避免 100,000 次 DDL 污染写入计时。
            await client.DropDatabaseIfExistsAsync(database).ConfigureAwait(false);
            await client.NonQueryAsync($"CREATE DATABASE {database}").ConfigureAwait(false);

            long totalRows = 0;
            long totalFieldValues = 0;
            var startTime = DateTime.Now;

            for (int deviceIndex = 0; deviceIndex < options.DeviceCount; deviceIndex++)
            {
                string device = database + "." + CreateDeviceName(deviceIndex);
                await client.InsertTabletAsync(
                    device,
                    preparedData.Timestamps,
                    preparedData.FieldNames,
                    preparedData.IotDbValues,
                    isAligned: true,
                    batchSize: options.IotDbTabletBatchSize).ConfigureAwait(false);

                totalRows += options.TimeSlotCount;
                totalFieldValues += (long)options.TimeSlotCount * options.FieldCount;

                if ((deviceIndex + 1) % options.IotDbProgressEveryDevices == 0)
                {
                    var progressSeconds = (DateTime.Now - startTime).TotalSeconds;
                    var throughput = totalFieldValues / progressSeconds;
                    Console.WriteLine($"    IoTDB 进度: 设备 {deviceIndex + 1:N0}/{options.DeviceCount:N0} | " +
                                    $"已写 {totalRows:N0} 行/{totalFieldValues:N0} 值 | " +
                                    $"吞吐 {throughput:F0} values/sec");
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            Console.WriteLine($"    IoTDB 写入完成: {totalRows:N0} 行/{totalFieldValues:N0} 值，耗时 {elapsed:F2} 秒");

            return new WriteCounts(totalRows, totalFieldValues);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] IoTDB 测试失败: {ex.Message}");
            Console.Error.WriteLine("请确保 IoTDB 已启动: docker compose -f tests/SonnetDB.Benchmarks/docker/docker-compose.yml up -d iotdb");
            return default;
        }
    }

    private static async Task<WriteCounts> RunSonnetDbServerBenchmarkAsync(
        DatabaseComparisonOptions options,
        PreparedData preparedData)
    {
        string baseUrl = Environment.GetEnvironmentVariable("SONNETDB_BENCH_URL")
            ?? "http://localhost:5080";
        string token = Environment.GetEnvironmentVariable("SONNETDB_BENCH_TOKEN")
            ?? "bench-admin-token";

        const string database = "bench_comparison";
        const string measurement = TableName;

        try
        {
            using var client = new SonnetDbHttpClient(baseUrl, token);
            await client.PingAsync().ConfigureAwait(false);
            await client.DropDatabaseIfExistsAsync(database).ConfigureAwait(false);
            await client.CreateDatabaseAsync(database).ConfigureAwait(false);

            string createSql = BuildCreateMeasurementSql(measurement, preparedData.FieldNames);
            await client.ExecuteSqlAsync(database, createSql).ConfigureAwait(false);

            long totalRows = 0;
            long totalFieldValues = 0;
            var startTime = DateTime.Now;
            var payloadBuilder = new StringBuilder(8 * 1024);

            for (int deviceIndex = 0; deviceIndex < options.DeviceCount; deviceIndex++)
            {
                string deviceName = CreateDeviceName(deviceIndex);
                string payload = BuildSonnetJsonPayload(deviceName, preparedData, payloadBuilder);

                // 为与 IoTDB REST 路径一致，不指定 flush（保持服务器默认写入路径）。
                await client.WriteJsonPointsAsync(database, measurement, payload).ConfigureAwait(false);
                totalRows += options.TimeSlotCount;
                totalFieldValues += (long)options.TimeSlotCount * options.FieldCount;

                if ((deviceIndex + 1) % options.IotDbProgressEveryDevices == 0)
                {
                    var progressSeconds = (DateTime.Now - startTime).TotalSeconds;
                    var throughput = totalFieldValues / progressSeconds;
                    Console.WriteLine($"    SonnetDB Server 进度: 设备 {deviceIndex + 1:N0}/{options.DeviceCount:N0} | " +
                                    $"已写 {totalRows:N0} 行/{totalFieldValues:N0} 值 | " +
                                    $"吞吐 {throughput:F0} values/sec");
                }
            }

            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            Console.WriteLine($"    SonnetDB Server 写入完成: {totalRows:N0} 行/{totalFieldValues:N0} 值，耗时 {elapsed:F2} 秒");
            return new WriteCounts(totalRows, totalFieldValues);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] SonnetDB Server 测试失败: {ex.Message}");
            Console.Error.WriteLine("请确保 SonnetDB 已启动: docker compose -f tests/SonnetDB.Benchmarks/docker/docker-compose.yml up -d sonnetdb");
            return default;
        }
    }

    private static PreparedData PrepareData(DatabaseComparisonOptions options)
    {
        var fieldNames = CreateFieldNames(options.FieldCount);
        var timestamps = CreateTimestamps(options.TimeSlotCount);

        var tagsByDevice = new IReadOnlyDictionary<string, string>[options.DeviceCount];
        for (int deviceIndex = 0; deviceIndex < tagsByDevice.Length; deviceIndex++)
        {
            tagsByDevice[deviceIndex] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sn"] = CreateDeviceName(deviceIndex)
            };
        }

        var fields = new Dictionary<string, FieldValue>(options.FieldCount, StringComparer.Ordinal);
        for (int fieldIndex = 0; fieldIndex < fieldNames.Length; fieldIndex++)
        {
            fields[fieldNames[fieldIndex]] = FieldValue.FromDouble(CreateValue(fieldIndex));
        }

        var iotDbValues = new double[options.FieldCount][];
        for (int fieldIndex = 0; fieldIndex < iotDbValues.Length; fieldIndex++)
        {
            iotDbValues[fieldIndex] = new double[options.TimeSlotCount];
            Array.Fill(iotDbValues[fieldIndex], CreateValue(fieldIndex));
        }

        return new PreparedData(fieldNames, timestamps, tagsByDevice, fields, iotDbValues);
    }

    /// <summary>创建测表</summary>
    private static void CreateTable(Tsdb db, string name, int fieldCount)
    {
        if (db.Measurements.Contains(name))
        {
            return;
        }

        var columns = new List<MeasurementColumn>();
        var tag = new MeasurementColumn("sn", MeasurementColumnRole.Tag, FieldType.String);
        columns.Add(tag);
        for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            var field = new MeasurementColumn($"c{fieldIndex + 1}", MeasurementColumnRole.Field, FieldType.Float64);
            columns.Add(field);
        }
        var table = MeasurementSchema.Create(name, columns);
        db.CreateMeasurement(table);
    }

    private static string[] CreateFieldNames(int fieldCount)
    {
        var fieldNames = new string[fieldCount];
        for (int fieldIndex = 0; fieldIndex < fieldNames.Length; fieldIndex++)
        {
            fieldNames[fieldIndex] = $"c{fieldIndex + 1}";
        }

        return fieldNames;
    }

    private static long[] CreateTimestamps(int timeSlotCount)
    {
        var timestamps = new long[timeSlotCount];
        for (int timeSlotIndex = 0; timeSlotIndex < timestamps.Length; timeSlotIndex++)
        {
            var time = StartDate.AddMinutes(5 * timeSlotIndex);
            timestamps[timeSlotIndex] = new DateTimeOffset(time).ToUnixTimeMilliseconds();
        }

        return timestamps;
    }

    private static string CreateDeviceName(int deviceIndex) => $"sn{deviceIndex + 1:D6}";

    private static string BuildCreateMeasurementSql(string measurement, IReadOnlyList<string> fieldNames)
    {
        var builder = new StringBuilder(measurement.Length + fieldNames.Count * 24 + 32);
        builder.Append("CREATE MEASUREMENT ").Append(measurement).Append(" (sn TAG");
        foreach (string fieldName in fieldNames)
        {
            builder.Append(", ").Append(fieldName).Append(" FIELD FLOAT");
        }

        builder.Append(')');
        return builder.ToString();
    }

    private static string BuildSonnetJsonPayload(
        string deviceName,
        PreparedData preparedData,
        StringBuilder payloadBuilder)
    {
        payloadBuilder.Clear();
        payloadBuilder.Append('{').Append("\"m\":\"").Append(TableName).Append("\",\"points\":[");

        for (int timeSlotIndex = 0; timeSlotIndex < preparedData.Timestamps.Length; timeSlotIndex++)
        {
            if (timeSlotIndex > 0)
            {
                payloadBuilder.Append(',');
            }

            payloadBuilder.Append("{\"t\":")
                .Append(preparedData.Timestamps[timeSlotIndex])
                .Append(",\"tags\":{\"sn\":")
                .Append(JsonSerializer.Serialize(deviceName))
                .Append("},\"fields\":{");

            for (int fieldIndex = 0; fieldIndex < preparedData.FieldNames.Length; fieldIndex++)
            {
                if (fieldIndex > 0)
                {
                    payloadBuilder.Append(',');
                }

                string fieldName = preparedData.FieldNames[fieldIndex];
                payloadBuilder.Append(JsonSerializer.Serialize(fieldName))
                    .Append(':')
                    .Append(preparedData.IotDbValues[fieldIndex][timeSlotIndex].ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
            }

            payloadBuilder.Append("}}");
        }

        payloadBuilder.Append("]}");
        return payloadBuilder.ToString();
    }

    private static double CreateValue(int fieldIndex) => fieldIndex + 1;

    /// <summary>输出对比表格</summary>
    private static void PrintComparisonTable(List<BenchmarkResult> results)
    {
        Console.WriteLine("╔════════╦═════════════╦════════════╦═══════════════╦═══════════════════╦════════════════════╗");
        Console.WriteLine("║ 轮数   ║ 数据库      ║ 耗时(ms)   ║ 行数          ║ 字段值            ║ 吞吐量(values/sec) ║");
        Console.WriteLine("╠════════╬═════════════╬════════════╬═══════════════╬═══════════════════╬════════════════════╣");

        var groupedByRound = results.GroupBy(r => r.RunNumber).ToList();
        for (int roundIndex = 0; roundIndex < groupedByRound.Count; roundIndex++)
        {
            var round = groupedByRound[roundIndex];
            foreach (var result in round)
            {
                Console.WriteLine($"║ {result.RunNumber,6} ║ {result.DatabaseName,-11} ║ " +
                                $"{result.TotalMilliseconds,10} ║ " +
                        $"{result.TotalRowsWritten,13:N0} ║ " +
                        $"{result.TotalFieldValuesWritten,17:N0} ║ " +
                        $"{result.ValuesPerSecond,18:F0} ║");
            }
            if (roundIndex < groupedByRound.Count - 1)
                Console.WriteLine("╠════════╬═════════════╬════════════╬═══════════════╬═══════════════════╬════════════════════╣");
        }
        Console.WriteLine("╚════════╩═════════════╩════════════╩═══════════════╩═══════════════════╩════════════════════╝");
    }

    /// <summary>输出统计信息</summary>
    private static void PrintStatistics(List<BenchmarkResult> results)
    {
        Console.WriteLine();
        Console.WriteLine("● SonnetDB 统计:");
        var sonnetDbResults = results.Where(r => r.DatabaseName == "SonnetDB").ToList();
        if (sonnetDbResults.Any())
        {
            var avgTime = sonnetDbResults.Average(r => r.TotalMilliseconds);
            var avgThroughput = sonnetDbResults.Average(r => r.ValuesPerSecond);
            var minTime = sonnetDbResults.Min(r => r.TotalMilliseconds);
            var maxTime = sonnetDbResults.Max(r => r.TotalMilliseconds);

            Console.WriteLine($"  平均耗时: {avgTime:F0} ms");
            Console.WriteLine($"  最小耗时: {minTime} ms");
            Console.WriteLine($"  最大耗时: {maxTime} ms");
            Console.WriteLine($"  平均吞吐量: {avgThroughput:F0} values/sec");
        }

        Console.WriteLine();
        Console.WriteLine("● IoTDB 统计:");
        var iotdbResults = results.Where(r => r.DatabaseName == "IoTDB").ToList();
        if (iotdbResults.Any())
        {
            var avgTime = iotdbResults.Average(r => r.TotalMilliseconds);
            var avgThroughput = iotdbResults.Average(r => r.ValuesPerSecond);
            var minTime = iotdbResults.Min(r => r.TotalMilliseconds);
            var maxTime = iotdbResults.Max(r => r.TotalMilliseconds);

            Console.WriteLine($"  平均耗时: {avgTime:F0} ms");
            Console.WriteLine($"  最小耗时: {minTime} ms");
            Console.WriteLine($"  最大耗时: {maxTime} ms");
            Console.WriteLine($"  平均吞吐量: {avgThroughput:F0} values/sec");
        }

        // 对比分析
        if (sonnetDbResults.Any() && iotdbResults.Any())
        {
            Console.WriteLine();
            Console.WriteLine("● 相对性能对比:");
            var sonnetAvg = sonnetDbResults.Average(r => r.ValuesPerSecond);
            var iotdbAvg = iotdbResults.Average(r => r.ValuesPerSecond);
            var ratio = sonnetAvg / iotdbAvg;

            if (ratio > 1)
            {
                Console.WriteLine($"  SonnetDB 比 IoTDB 快 {ratio:F2}x");
            }
            else
            {
                Console.WriteLine($"  IoTDB 比 SonnetDB 快 {1 / ratio:F2}x");
            }
        }
    }
}
