using System.Text.Json.Serialization;
using SonnetDB.Configuration;

namespace SonnetDB.Auth;

/// <summary>
/// AOT-friendly <see cref="System.Text.Json"/> 源生成 context，专用于 <c>users.json</c>、
/// <c>grants.json</c> 及 <c>ai-config.json</c> 的反/序列化。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(UserFile))]
[JsonSerializable(typeof(GrantsFile))]
[JsonSerializable(typeof(InstallationFile))]
[JsonSerializable(typeof(AiOptions))]
internal sealed partial class AuthJsonContext : JsonSerializerContext;
