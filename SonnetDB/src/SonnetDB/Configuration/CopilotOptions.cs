namespace SonnetDB.Configuration;

/// <summary>
/// Copilot 子系统配置。绑定路径：<c>"SonnetDBServer:Copilot"</c>。
/// </summary>
public sealed class CopilotOptions
{
    /// <summary>
    /// 是否启用 Copilot 子系统。默认 <c>true</c>。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Embedding provider 配置。
    /// </summary>
    public CopilotEmbeddingOptions Embedding { get; set; } = new();

    /// <summary>
    /// Chat provider 配置。
    /// </summary>
    public CopilotChatOptions Chat { get; set; } = new();

    /// <summary>
    /// 文档摄入 / 检索配置。
    /// </summary>
    public CopilotDocsOptions Docs { get; set; } = new();

    /// <summary>
    /// 技能库（PR #65）配置。
    /// </summary>
    public CopilotSkillsOptions Skills { get; set; } = new();
}

/// <summary>
/// 技能库摄入 / 检索配置（PR #65）。
/// </summary>
public sealed class CopilotSkillsOptions
{
    /// <summary>
    /// 服务端启动后是否自动执行一次后台技能库增量摄入。默认 <c>false</c>；
    /// 在线 Copilot 的知识与技能由 ai.sonnetdb.com 云端维护。
    /// </summary>
    public bool AutoIngestOnStartup { get; set; } = false;

    /// <summary>
    /// 技能根目录。默认 <c>./copilot/skills</c>。
    /// </summary>
    public string Root { get; set; } = "./copilot/skills";
}

/// <summary>
/// Embedding provider 配置。
/// </summary>
public sealed class CopilotEmbeddingOptions
{
    /// <summary>
    /// provider 名称：<c>builtin</c>（默认，零依赖 hash 投影） / <c>local</c>（本地 ONNX） / <c>openai</c>。
    /// 默认使用 <c>builtin</c>，保证首次启动不需要任何外部依赖即可使 Copilot 就绪。
    /// </summary>
    public string Provider { get; set; } = "builtin";

    /// <summary>
    /// 本地 ONNX 模型路径。
    /// </summary>
    public string? LocalModelPath { get; set; }

    /// <summary>
    /// OpenAI-compatible 服务端点。
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// OpenAI-compatible API Key。
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// embedding 模型名。
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 请求超时（秒）。默认 <c>60</c>。
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// Chat provider 配置。
/// </summary>
public sealed class CopilotChatOptions
{
    /// <summary>
    /// provider 名称：当前仅支持 <c>openai</c>。
    /// </summary>
    public string Provider { get; set; } = "openai";

    /// <summary>
    /// OpenAI-compatible 服务端点。
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// OpenAI-compatible API Key。
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// chat 模型名。
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// （M8）可供前端 CopilotDock 下拉选择的模型列表。仅用于 UI 预填，实际能否调用取决于上游服务。
    /// </summary>
    public List<string> AvailableModels { get; set; } = new();

    /// <summary>
    /// 请求超时（秒）。默认 <c>60</c>。
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// 文档摄入 / 检索配置。
/// </summary>
public sealed class CopilotDocsOptions
{
    /// <summary>
    /// 服务端启动后是否自动执行一次后台增量摄入。默认 <c>false</c>；
    /// 在线 Copilot 不再依赖本地知识库作为兜底。
    /// </summary>
    public bool AutoIngestOnStartup { get; set; } = false;

    /// <summary>
    /// 文档根目录列表。默认优先扫描仓库源码文档 <c>./docs</c>，其次兼容 <c>./web/help</c> 与运行时生成目录。
    /// </summary>
    public List<string> Roots { get; set; } =
    [
        "./docs",
        "./web/help",
        "./src/SonnetDB/wwwroot/help",
    ];

    /// <summary>
    /// 单块最大字符数。默认 <c>800</c>。
    /// </summary>
    public int ChunkSize { get; set; } = 800;

    /// <summary>
    /// 相邻块重叠字符数。默认 <c>100</c>。
    /// </summary>
    public int ChunkOverlap { get; set; } = 100;
}
