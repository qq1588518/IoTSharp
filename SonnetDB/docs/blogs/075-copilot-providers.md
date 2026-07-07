## SonnetDB Copilot AI 供应商配置：OpenAI / DashScope / ZhiPu / Moonshot / DeepSeek

SonnetDB Copilot 采用 OpenAI 兼容协议，这意味着所有兼容 OpenAI API 格式的服务商都可以无缝接入。系统通过 `AiOptions` 配置类统一管理，Web 管理界面支持可视化配置。

### 配置架构

核心配置类 `AiOptions` 定义服务商、API Key、模型等参数：

```csharp
public sealed class AiOptions
{
    public bool Enabled { get; set; } = false;
    public string Provider { get; set; } = "international";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-sonnet-4-6";
    public int TimeoutSeconds { get; set; } = 60;
}
```

`AiCopilotBridge` 负责将 UI 层的配置同步到底层的 `CopilotChatOptions` 和 `CopilotEmbeddingOptions`，实现"前脚保存，后脚就绪"的无缝体验。

### 两大服务节点

系统内置两个默认代理节点：

- **国际站** `https://sonnet.vip/v1/` —— 连接 OpenAI、Anthropic Claude 等海外模型
- **国内站** `https://ai.sonnetdb.com/v1/` —— 连接 DashScope（通义千问）、智谱 GLM、Moonshot、DeepSeek 等国内模型

选择 `international` 或 `domestic` provider 即可自动切换。

### 直接连接模式

如果您有自己的 API Key，也可以绕过代理节点，直接在 `CopilotChatOptions` 中配置自定义 Endpoint：

```json
{
  "SonnetDBServer": {
    "Copilot": {
      "Chat": {
        "Provider": "openai",
        "Endpoint": "https://api.openai.com/v1/",
        "ApiKey": "sk-xxx",
        "Model": "gpt-4o"
      },
      "Embedding": {
        "Provider": "openai",
        "Endpoint": "https://api.openai.com/v1/",
        "ApiKey": "sk-xxx",
        "Model": "text-embedding-3-small"
      }
    }
  }
}
```

### Embedding Provider 三选一

| Provider | 说明 | 适用场景 |
|----------|------|----------|
| `builtin` | 零依赖哈希投影，384 维 | 开发测试、离线环境 |
| `local` | 本地 ONNX 模型 | 内网部署、数据不出域 |
| `openai` | OpenAI 兼容远程服务 | 生产环境、高精度需求 |

`builtin` 是默认选项，保证首次启动服务时 Copilot 即可用，无需任何外部依赖。可在 Web 管理界面随时切换。

### 前端模型选择

Copilot 面板支持运行时切换模型（M8 特性）。用户下拉选择模型名后，该参数会通过 `CopilotChatRequest.Model` 字段传入服务端，临时覆盖默认模型：

```typescript
export interface CopilotChatRequest {
  db?: string;
  messages: CopilotMessage[];
  model?: string; // 本次请求使用的模型
}
```

服务端 `/v1/copilot/models` 端点会返回配置的候选模型列表和默认模型，供前端下拉框展示。这意味着用户可以在同一个对话中随时切换 GPT-4o、Claude Sonnet、DeepSeek V3 等不同模型，获得多元化的回答风格。
