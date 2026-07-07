namespace SonnetDB.Configuration;

/// <summary>
/// AI 助手配置。在线 Copilot 固定通过 <c>https://ai.sonnetdb.com</c> 官方 AI Gateway 调用。
/// 运行时通过 <see cref="SonnetDB.Auth.AiConfigStore"/> 读写持久化副本（<c>.system/ai-config.json</c>）。
/// </summary>
public sealed class AiOptions
{
    /// <summary>是否启用 AI 助手功能。默认 <c>true</c>，真正可用性由 Cloud Token 绑定状态决定。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>官方 AI Gateway 地址。新版本固定为 <c>https://ai.sonnetdb.com</c>，该字段仅用于兼容读取旧配置。</summary>
    public string GatewayBaseUrl { get; set; } = "https://ai.sonnetdb.com";

    /// <summary>sonnetdb.com Platform API 地址。新版本固定为 <c>https://api.sonnetdb.com</c>，该字段仅用于兼容读取旧配置。</summary>
    public string PlatformApiBaseUrl { get; set; } = "https://api.sonnetdb.com";

    /// <summary>旧版 API Key 配置，仅用于读取老配置文件；新版本不再写入或使用。</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>sonnetdb.com 设备码流程签发的 Cloud Access Token。</summary>
    public string CloudAccessToken { get; set; } = "";

    /// <summary>sonnetdb.com 设备码流程签发的 Cloud Refresh Token，预留给后续刷新端点使用。</summary>
    public string CloudRefreshToken { get; set; } = "";

    /// <summary>安装级稳定设备 ID，持久化在 <c>.system/ai-config.json</c> 中，版本升级和容器重建后保持不变。</summary>
    public string CloudDeviceLocalId { get; set; } = "";

    /// <summary>Cloud Access Token 类型，通常为 <c>Bearer</c>。</summary>
    public string CloudTokenType { get; set; } = "Bearer";

    /// <summary>Cloud Access Token 过期时间（UTC）。</summary>
    public DateTimeOffset? CloudAccessTokenExpiresAtUtc { get; set; }

    /// <summary>Cloud Token scope。</summary>
    public string CloudScope { get; set; } = "";

    /// <summary>最后一次成功绑定 sonnetdb.com 账号的时间（UTC）。</summary>
    public DateTimeOffset? CloudBoundAtUtc { get; set; }

    /// <summary>旧版本地模型配置，仅用于读取老配置文件；新版本模型从平台模型列表获取。</summary>
    public string Model { get; set; } = "claude-sonnet-4-6";

    /// <summary>请求超时（秒）。新版本不在 UI 暴露，仅作为内部请求保护和旧配置兼容字段。</summary>
    public int TimeoutSeconds { get; set; } = 60;
}
