using System.Text.Json.Serialization;

namespace SonnetDB.Contracts;

// ---- OpenAI-compatible /v1/chat/completions 内部协议 ----

internal sealed record OpenAiRequest(
    string? Model,
    List<AiMessage> Messages,
    bool Stream);

internal sealed record OpenAiChunk(
    List<OpenAiChoice> Choices);

internal sealed record OpenAiChoice(
    OpenAiDelta Delta);

internal sealed record OpenAiDelta(
    [property: JsonPropertyName("content")] string? Content);
