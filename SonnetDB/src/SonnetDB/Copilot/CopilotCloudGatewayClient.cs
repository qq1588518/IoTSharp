using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SonnetDB.Configuration;
using SonnetDB.Json;

namespace SonnetDB.Copilot;

internal interface ICopilotCloudGatewayClient
{
    Task<CopilotCloudChatResponse> ChatAsync(
        AiOptions options,
        CopilotCloudChatRequest request,
        CancellationToken cancellationToken);

    Task<CopilotCloudToolResultResponse> SubmitToolResultAsync(
        AiOptions options,
        CopilotCloudToolResultRequest request,
        CancellationToken cancellationToken);
}

internal sealed class CopilotCloudGatewayClient : ICopilotCloudGatewayClient
{
    private const string OfficialGatewayBaseUrl = "https://ai.sonnetdb.com";
    private readonly IHttpClientFactory _httpClientFactory;

    public CopilotCloudGatewayClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<CopilotCloudChatResponse> ChatAsync(
        AiOptions options,
        CopilotCloudChatRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(request);

        using var client = CreateClient(options);
        var body = JsonSerializer.Serialize(request, ServerJsonContext.Default.CopilotCloudChatRequest);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            NormalizeGatewayBaseUrl(options.GatewayBaseUrl) + "/v1/copilot/chat")
        {
            Content = content
        };
        ApplyCloudToken(httpRequest, options);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-ndjson"));

        using var response = await client.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        var requestId = TryGetRequestId(response);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new CopilotCloudGatewayException(
                (int)response.StatusCode,
                requestId,
                payload);
        }

        var events = ParseEventLines(payload);
        return new CopilotCloudChatResponse((int)response.StatusCode, requestId, events);
    }

    public async Task<CopilotCloudToolResultResponse> SubmitToolResultAsync(
        AiOptions options,
        CopilotCloudToolResultRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(request);

        using var client = CreateClient(options);
        var body = JsonSerializer.Serialize(request, ServerJsonContext.Default.CopilotCloudToolResultRequest);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            NormalizeGatewayBaseUrl(options.GatewayBaseUrl) + "/v1/copilot/tool-results")
        {
            Content = content
        };
        ApplyCloudToken(httpRequest, options);

        using var response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var requestId = TryGetRequestId(response);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new CopilotCloudGatewayException(
                (int)response.StatusCode,
                requestId,
                payload);
        }

        var parsed = JsonSerializer.Deserialize(
            payload,
            ServerJsonContext.Default.CopilotCloudToolResultResponse);
        return parsed
            ?? throw new CopilotCloudGatewayException(
                (int)response.StatusCode,
                requestId,
                "Cloud Copilot returned an invalid tool result response.");
    }

    private HttpClient CreateClient(AiOptions options)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 60);
        return client;
    }

    private static void ApplyCloudToken(HttpRequestMessage request, AiOptions options)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue(
            string.IsNullOrWhiteSpace(options.CloudTokenType) ? "Bearer" : options.CloudTokenType.Trim(),
            options.CloudAccessToken);
    }

    private static IReadOnlyList<CopilotCloudRuntimeEvent> ParseEventLines(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        var events = new List<CopilotCloudRuntimeEvent>();
        foreach (var rawLine in payload.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                line = line["data:".Length..].Trim();
                if (line == "[DONE]" || line.Length == 0)
                {
                    continue;
                }
            }

            var evt = JsonSerializer.Deserialize(line, ServerJsonContext.Default.CopilotCloudRuntimeEvent);
            if (evt is not null)
            {
                events.Add(evt);
            }
        }

        return events;
    }

    private static string NormalizeGatewayBaseUrl(string? value)
        => OfficialGatewayBaseUrl;

    private static string? TryGetRequestId(HttpResponseMessage response)
    {
        foreach (var header in new[] { "x-request-id", "X-Request-Id" })
        {
            if (response.Headers.TryGetValues(header, out var values))
            {
                return values.FirstOrDefault();
            }
        }

        return null;
    }
}

internal sealed class CopilotCloudGatewayException : Exception
{
    public int StatusCode { get; }

    public string? RequestId { get; }

    public string Payload { get; }

    public CopilotCloudGatewayException(int statusCode, string? requestId, string payload)
        : base($"Cloud Copilot returned HTTP {statusCode}: {payload}")
    {
        StatusCode = statusCode;
        RequestId = requestId;
        Payload = payload;
    }
}
