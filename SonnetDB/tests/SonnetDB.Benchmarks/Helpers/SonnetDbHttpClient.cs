using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SonnetDB.Benchmarks.Helpers;

/// <summary>
/// SonnetDB HTTP 客户端（基准专用）：用于创建/删除数据库、执行 SQL、批量 JSON 点写入。
/// </summary>
public sealed class SonnetDbHttpClient : IDisposable
{
    private readonly HttpClient _http;

    public SonnetDbHttpClient(string baseUrl, string bearerToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromMinutes(10)
        };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", bearerToken);
    }

    public async Task PingAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("/healthz", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task DropDatabaseIfExistsAsync(string database, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);

        using var response = await _http.DeleteAsync($"/v1/db/{database}", ct).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    public async Task CreateDatabaseAsync(string database, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);

        var payload = JsonSerializer.Serialize(new { name = database });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("/v1/db", content, ct).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // 已存在时允许继续，避免基准前置清理偶发竞态导致失败。
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict ||
            response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    public async Task ExecuteSqlAsync(string database, string sql, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var payload = JsonSerializer.Serialize(new { sql });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync($"/v1/db/{database}/sql", content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task WriteJsonPointsAsync(
        string database,
        string measurement,
        string jsonPayload,
        string? flush = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(measurement);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPayload);

        string path = $"/v1/db/{database}/measurements/{measurement}/json";
        if (!string.IsNullOrWhiteSpace(flush))
        {
            path += $"?flush={Uri.EscapeDataString(flush)}";
        }

        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(path, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();
}
