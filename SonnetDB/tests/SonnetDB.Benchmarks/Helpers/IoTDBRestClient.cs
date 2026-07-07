using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SonnetDB.Benchmarks.Helpers;

/// <summary>
/// 通过 Apache IoTDB REST API v2（端口 18080）执行 SQL 与 Tablet 写入的轻量级客户端。
/// </summary>
public sealed class IoTDBRestClient : IDisposable
{
    private readonly HttpClient _http;

    /// <summary>
    /// 创建 IoTDB REST v2 客户端。
    /// </summary>
    /// <param name="baseUrl">IoTDB REST API 地址，例如 http://localhost:18080。</param>
    /// <param name="username">用户名，默认 root。</param>
    /// <param name="password">密码，默认 root。</param>
    public IoTDBRestClient(string baseUrl, string username = "root", string password = "root")
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromMinutes(10),
        };
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
    }

    /// <summary>
    /// 执行 IoTDB 管理类 SQL，例如 <c>CREATE DATABASE</c>。
    /// </summary>
    /// <param name="sql">要执行的 SQL。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task NonQueryAsync(string sql, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        string json = JsonSerializer.Serialize(new { sql });
        await PostAndValidateAsync("/rest/v2/nonQuery", json, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行 IoTDB 查询 SQL 并返回原始 JSON 字符串。
    /// </summary>
    /// <param name="sql">要执行的查询 SQL。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>IoTDB 返回的原始 JSON。</returns>
    public async Task<string> QueryAsync(string sql, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        string json = JsonSerializer.Serialize(new { sql });
        return await PostAndValidateAsync("/rest/v2/query", json, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 删除 IoTDB database；如果 database 不存在则忽略错误。
    /// </summary>
    /// <param name="database">database 路径，例如 <c>root.bench_insert</c>。</param>
    public async Task DropDatabaseIfExistsAsync(string database)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        try
        {
            await NonQueryAsync($"DROP DATABASE {database}").ConfigureAwait(false);
        }
        catch
        {
            // IoTDB 不同版本的 DROP IF EXISTS 支持不一致；benchmark 清理允许幂等失败。
        }
    }

    /// <summary>
    /// 创建 database 与单个 DOUBLE timeseries。
    /// </summary>
    /// <param name="database">database 路径，例如 <c>root.bench_insert</c>。</param>
    /// <param name="device">设备路径，例如 <c>root.bench_insert.server001</c>。</param>
    public async Task PrepareSeriesAsync(string database, string device)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(device);

        await DropDatabaseIfExistsAsync(database).ConfigureAwait(false);
        await NonQueryAsync($"CREATE DATABASE {database}").ConfigureAwait(false);
        await NonQueryAsync(
            $"CREATE TIMESERIES {device}.value WITH DATATYPE=DOUBLE, ENCODING=GORILLA")
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 创建单设备多测点的 aligned timeseries，用于与 SonnetDB 的多字段 Point 数据模型对齐。
    /// </summary>
    /// <param name="device">设备路径，例如 <c>root.bench_comparison.sn000001</c>。</param>
    /// <param name="measurements">测点名称列表，例如 <c>c1</c> 到 <c>c30</c>。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task CreateAlignedTimeseriesAsync(
        string device,
        IReadOnlyList<string> measurements,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(device);
        ArgumentNullException.ThrowIfNull(measurements);
        if (measurements.Count == 0)
            throw new ArgumentException("At least one measurement is required.", nameof(measurements));

        var sql = new StringBuilder(device.Length + measurements.Count * 32);
        sql.Append("CREATE ALIGNED TIMESERIES ").Append(device).Append('(');
        for (int measurementIndex = 0; measurementIndex < measurements.Count; measurementIndex++)
        {
            if (measurementIndex > 0)
                sql.Append(',');

            sql.Append(measurements[measurementIndex]).Append(" DOUBLE encoding=GORILLA");
        }

        sql.Append(')');
        await NonQueryAsync(sql.ToString(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 以 REST v2 insertTablet 批量写入单设备单测点数据。
    /// </summary>
    /// <param name="device">目标设备路径。</param>
    /// <param name="points">待写入数据点。</param>
    /// <param name="batchSize">每个 Tablet 的行数。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task InsertTabletAsync(
        string device,
        BenchmarkDataPoint[] points,
        int batchSize = 10_000,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(device);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var sb = new StringBuilder(batchSize * 32);
        for (int offset = 0; offset < points.Length; offset += batchSize)
        {
            int end = Math.Min(offset + batchSize, points.Length);
            sb.Clear();
            sb.Append("{\"timestamps\":[");
            for (int i = offset; i < end; i++)
            {
                if (i > offset) sb.Append(',');
                sb.Append(points[i].Timestamp);
            }

            sb.Append("],\"measurements\":[\"value\"],\"data_types\":[\"DOUBLE\"],\"values\":[[");
            for (int i = offset; i < end; i++)
            {
                if (i > offset) sb.Append(',');
                sb.Append(points[i].Value.ToString("G17", CultureInfo.InvariantCulture));
            }

            sb.Append("]],\"is_aligned\":false,\"device\":\"").Append(device).Append("\"}");
            await PostAndValidateAsync("/rest/v2/insertTablet", sb.ToString(), ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 以 REST v2 insertTablet 批量写入单设备多测点数据。
    /// values 使用列式结构：第一维是 measurement，第二维是 timestamp row。
    /// </summary>
    /// <param name="device">目标设备路径。</param>
    /// <param name="timestamps">Unix 时间戳（毫秒）。</param>
    /// <param name="measurements">测点名称列表。</param>
    /// <param name="values">列式测点值数组。</param>
    /// <param name="isAligned">是否写入 aligned device。</param>
    /// <param name="batchSize">每个 Tablet 的行数。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task InsertTabletAsync(
        string device,
        long[] timestamps,
        IReadOnlyList<string> measurements,
        double[][] values,
        bool isAligned,
        int batchSize = 10_000,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(device);
        ArgumentNullException.ThrowIfNull(timestamps);
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        if (measurements.Count == 0)
            throw new ArgumentException("At least one measurement is required.", nameof(measurements));
        if (values.Length != measurements.Count)
            throw new ArgumentException("The number of value columns must match the number of measurements.", nameof(values));
        foreach (double[] valueColumn in values)
        {
            if (valueColumn.Length != timestamps.Length)
                throw new ArgumentException("Each value column must have the same length as timestamps.", nameof(values));
        }

        var sb = new StringBuilder(Math.Min(batchSize, timestamps.Length) * measurements.Count * 24);
        for (int offset = 0; offset < timestamps.Length; offset += batchSize)
        {
            int end = Math.Min(offset + batchSize, timestamps.Length);
            sb.Clear();

            sb.Append("{\"timestamps\":[");
            for (int rowIndex = offset; rowIndex < end; rowIndex++)
            {
                if (rowIndex > offset)
                    sb.Append(',');

                sb.Append(timestamps[rowIndex]);
            }

            sb.Append("],\"measurements\":[");
            for (int measurementIndex = 0; measurementIndex < measurements.Count; measurementIndex++)
            {
                if (measurementIndex > 0)
                    sb.Append(',');

                AppendJsonString(sb, measurements[measurementIndex]);
            }

            sb.Append("],\"data_types\":[");
            for (int measurementIndex = 0; measurementIndex < measurements.Count; measurementIndex++)
            {
                if (measurementIndex > 0)
                    sb.Append(',');

                sb.Append("\"DOUBLE\"");
            }

            sb.Append("],\"values\":[");
            for (int measurementIndex = 0; measurementIndex < measurements.Count; measurementIndex++)
            {
                if (measurementIndex > 0)
                    sb.Append(',');

                sb.Append('[');
                double[] valueColumn = values[measurementIndex];
                for (int rowIndex = offset; rowIndex < end; rowIndex++)
                {
                    if (rowIndex > offset)
                        sb.Append(',');

                    sb.Append(valueColumn[rowIndex].ToString("G17", CultureInfo.InvariantCulture));
                }

                sb.Append(']');
            }

            sb.Append("],\"is_aligned\":")
                .Append(isAligned ? "true" : "false")
                .Append(",\"device\":");
            AppendJsonString(sb, device);
            sb.Append('}');

            await PostAndValidateAsync("/rest/v2/insertTablet", sb.ToString(), ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private static void AppendJsonString(StringBuilder sb, string value)
    {
        sb.Append(JsonSerializer.Serialize(value));
    }

    private async Task<string> PostAndValidateAsync(string path, string json, CancellationToken ct)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(path, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 200)
        {
            string message = doc.RootElement.TryGetProperty("message", out var messageEl)
                ? messageEl.GetString() ?? body
                : body;
            throw new InvalidOperationException($"IoTDB error: {message}");
        }

        return body;
    }
}
