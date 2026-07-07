using SonnetDB.Engine;

namespace SonnetDB.Mcp;

/// <summary>
/// MCP schema 读取结果的 30 秒进程内缓存。
/// </summary>
internal sealed class SonnetDbMcpSchemaCache
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _timeProvider;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, CacheEntry<IReadOnlyList<string>>> _measurementListCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CacheEntry<McpMeasurementSchemaResult>> _measurementSchemaCache = new(StringComparer.Ordinal);

    public SonnetDbMcpSchemaCache()
        : this(TimeProvider.System)
    {
    }

    internal SonnetDbMcpSchemaCache(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// 获取当前数据库的 measurement 名称快照。
    /// </summary>
    public IReadOnlyList<string> GetMeasurements(string databaseName, Tsdb tsdb)
    {
        ArgumentException.ThrowIfNullOrEmpty(databaseName);
        ArgumentNullException.ThrowIfNull(tsdb);

        return GetOrCreate(
            _measurementListCache,
            $"measurements::{databaseName}",
            () => tsdb.Measurements.Snapshot()
                .Select(static measurement => measurement.Name)
                .ToArray());
    }

    /// <summary>
    /// 获取指定 measurement 的 schema 快照。
    /// </summary>
    public McpMeasurementSchemaResult GetMeasurementSchema(string databaseName, string measurementName, Tsdb tsdb)
    {
        ArgumentException.ThrowIfNullOrEmpty(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(measurementName);
        ArgumentNullException.ThrowIfNull(tsdb);

        return GetOrCreate(
            _measurementSchemaCache,
            $"measurement::{databaseName}::{measurementName}",
            () =>
            {
                var schema = tsdb.Measurements.TryGet(measurementName)
                    ?? throw new InvalidOperationException($"measurement '{measurementName}' 不存在。");

                var columns = new List<McpMeasurementColumnResult>(schema.Columns.Count);
                foreach (var column in schema.Columns)
                {
                    columns.Add(new McpMeasurementColumnResult(
                        Name: column.Name,
                        ColumnType: column.Role == SonnetDB.Catalog.MeasurementColumnRole.Tag ? "tag" : "field",
                        DataType: FormatColumnDataType(column)));
                }

                return new McpMeasurementSchemaResult(databaseName, schema.Name, columns);
            });
    }

    private T GetOrCreate<T>(
        Dictionary<string, CacheEntry<T>> cache,
        string key,
        Func<T> valueFactory)
    {
        lock (_lock)
        {
            var now = _timeProvider.GetUtcNow();
            if (cache.TryGetValue(key, out var cached) && cached.ExpiresAt > now)
                return cached.Value;

            var value = valueFactory();
            cache[key] = new CacheEntry<T>(value, now + CacheTtl);
            return value;
        }
    }

    private static string FormatColumnDataType(SonnetDB.Catalog.MeasurementColumn column)
    {
        if (column.DataType == SonnetDB.Storage.Format.FieldType.Vector && column.VectorDimension is int dimension)
            return $"vector({dimension})";

        return column.DataType switch
        {
            SonnetDB.Storage.Format.FieldType.Float64 => "float64",
            SonnetDB.Storage.Format.FieldType.Int64 => "int64",
            SonnetDB.Storage.Format.FieldType.Boolean => "boolean",
            SonnetDB.Storage.Format.FieldType.String => "string",
            SonnetDB.Storage.Format.FieldType.Vector => "vector",
            _ => column.DataType.ToString().ToLowerInvariant(),
        };
    }

    private sealed record CacheEntry<T>(T Value, DateTimeOffset ExpiresAt);
}
