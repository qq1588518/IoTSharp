using Npgsql;
using NpgsqlTypes;

namespace SonnetDB.Benchmarks.Helpers;

internal static class TimescaleDbBenchmark
{
    public static string DefaultConnectionString =>
        Environment.GetEnvironmentVariable("TIMESCALEDB_BENCH_CONNECTION")
        ?? "Host=localhost;Port=5433;Username=postgres;Password=postgres;Database=benchmarks;Pooling=true;Maximum Pool Size=20;Timeout=15;Command Timeout=300";

    public static async Task<bool> IsAvailableAsync(string connectionString)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync().ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task PrepareSensorTableAsync(string connectionString, string tableName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        string sql = $"""
            CREATE EXTENSION IF NOT EXISTS timescaledb;
            DROP TABLE IF EXISTS {tableName};
            CREATE TABLE {tableName} (
                time  TIMESTAMPTZ      NOT NULL,
                host  TEXT             NOT NULL,
                value DOUBLE PRECISION NOT NULL
            );
            SELECT create_hypertable('{tableName}', 'time', if_not_exists => TRUE);
            CREATE INDEX IF NOT EXISTS ix_{tableName}_host_time ON {tableName} (host, time DESC);
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public static async Task DropSensorTableAsync(string connectionString, string tableName)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            await using var command = new NpgsqlCommand($"DROP TABLE IF EXISTS {tableName}", connection);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch
        {
            // 清理失败不影响 benchmark 结果。
        }
    }

    public static async Task BulkCopyAsync(
        string connectionString,
        string tableName,
        BenchmarkDataPoint[] points)
    {
        ArgumentNullException.ThrowIfNull(points);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var importer = await connection.BeginBinaryImportAsync(
            $"COPY {tableName} (time, host, value) FROM STDIN (FORMAT BINARY)")
            .ConfigureAwait(false);

        for (int i = 0; i < points.Length; i++)
        {
            var point = points[i];
            await importer.StartRowAsync().ConfigureAwait(false);
            await importer.WriteAsync(
                DateTimeOffset.FromUnixTimeMilliseconds(point.Timestamp).UtcDateTime,
                NpgsqlDbType.TimestampTz).ConfigureAwait(false);
            await importer.WriteAsync(point.Host, NpgsqlDbType.Text).ConfigureAwait(false);
            await importer.WriteAsync(point.Value, NpgsqlDbType.Double).ConfigureAwait(false);
        }

        await importer.CompleteAsync().ConfigureAwait(false);
    }

    public static async Task<int> QueryRangeAsync(
        string connectionString,
        string tableName,
        long fromMs,
        long toMs)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            $"SELECT time, host, value FROM {tableName} WHERE host = @host AND time >= @from AND time < @to ORDER BY time",
            connection);
        command.Parameters.AddWithValue("host", NpgsqlDbType.Text, "server001");
        command.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz,
            DateTimeOffset.FromUnixTimeMilliseconds(fromMs).UtcDateTime);
        command.Parameters.AddWithValue("to", NpgsqlDbType.TimestampTz,
            DateTimeOffset.FromUnixTimeMilliseconds(toMs).UtcDateTime);

        int count = 0;
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            _ = reader.GetDateTime(0);
            _ = reader.GetString(1);
            _ = reader.GetDouble(2);
            count++;
        }

        return count;
    }

    public static async Task<int> Aggregate1MinAsync(
        string connectionString,
        string tableName,
        long fromMs,
        long toMs)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT time_bucket('1 minute', time) AS bucket,
                   avg(value),
                   min(value),
                   max(value),
                   count(value)
            FROM {tableName}
            WHERE time >= @from AND time < @to
            GROUP BY bucket
            ORDER BY bucket
            """,
            connection);
        command.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz,
            DateTimeOffset.FromUnixTimeMilliseconds(fromMs).UtcDateTime);
        command.Parameters.AddWithValue("to", NpgsqlDbType.TimestampTz,
            DateTimeOffset.FromUnixTimeMilliseconds(toMs).UtcDateTime);

        int count = 0;
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            _ = reader.GetDateTime(0);
            _ = reader.GetDouble(1);
            _ = reader.GetDouble(2);
            _ = reader.GetDouble(3);
            _ = reader.GetInt64(4);
            count++;
        }

        return count;
    }
}
