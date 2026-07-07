## SonnetDB Copilot AI 架构解析：Agent Orchestrator + Knowledge Base + Skills + MCP Tools

SonnetDB Copilot 是一个深度集成在时序数据库中的 AI 智能体系统。它并非简单的"套壳 ChatGPT"，而是一套完整的 RAG（检索增强生成）+ ReAct（推理行动）架构，包含四大核心组件：

### Agent Orchestrator（编排器）

`CopilotAgent` 是整个 Copilot 的大脑。它接收用户问题后，依次执行：

1. **对话裁剪**：将历史消息按 1200 token 预算裁剪，保留最近的相关上下文
2. **双路检索**：同时从知识库（文档）和技能库（Skills）做向量召回
3. **ReAct 循环**：最多 6 轮推理-行动循环，每轮 Planner 决定调什么工具
4. **最终回答**：汇总所有工具结果，生成带引用的自然语言回答

```csharp
public async IAsyncEnumerable<CopilotChatEvent> RunAsync(
    CopilotAgentContext context,
    IReadOnlyList<AiMessage> messages,
    int? docsK = null,
    int? skillsK = null,
    CancellationToken cancellationToken = default)
{
    // Step 1: 准备对话
    var conversation = PrepareConversation(messages);
    // Step 2: 双路检索
    var docs = await _docsSearchService.SearchAsync(...);
    var skillHits = await _skillSearchService.SearchAsync(...);
    // Step 3: ReAct 循环，每轮执行一个工具
    for (var round = 0; round < MaxReActRounds; round++)
    {
        var plan = await PlanToolsAsync(...);
        if (plan.Count == 0) break;
        var result = await ExecuteToolAsync(...);
    }
    // Step 4: 生成最终回答
    var answer = await GenerateAnswerAsync(...);
}
```

### Knowledge Base（知识库）

知识库存放在 `__copilot__.docs` 表中，包含文档分块后的段落及其 384 维向量。服务启动时后台自动增量摄入 `./docs`、`./web/help` 等目录中的 Markdown/HTML 文件。

### Skills（技能库）

24 个预置技能以 Markdown + YAML frontmatter 格式存放在 `copilot/skills/` 目录。每个技能声明自己的名称、描述、触发关键词和所需工具。例如 `query-aggregation` 技能声明：

```yaml
---
name: query-aggregation
description: 编写时间窗口聚合查询的最佳实践
triggers:
  - aggregation
  - group by
  - 聚合
requires_tools:
  - query_sql
  - describe_measurement
---
```

### MCP Tools（工具层）

Copilot 定义了 8 个 MCP 工具供 Agent 调用：

| 工具 | 用途 | 读写 |
|------|------|------|
| `list_databases` | 列出可见数据库 | 只读 |
| `list_measurements` | 列出 measurement | 只读 |
| `describe_measurement` | 查看表结构 | 只读 |
| `sample_rows` | 采样数据行 | 只读 |
| `query_sql` | 执行只读 SQL | 只读 |
| `explain_sql` | 估算执行计划 | 只读 |
| `draft_sql` | 草拟 SQL 但不执行 | 工具本身只读 |
| `execute_sql` | 执行任意 SQL | 可写 |

### 架构亮点

- **零依赖开箱即用**：内置 `BuiltinHashEmbeddingProvider`，基于 SHA-256 + 词袋哈希投影生成 384 维向量，无需外部模型
- **完善的权限体系**：read-only / read-write 模式 + 凭据本身的权限约束，双重保护数据安全
- **流式 SSE 响应**：所有事件通过 `text/event-stream` 实时推送，前端逐条渲染
- **国际化双节点**：国际站 `sonnet.vip` 和国内站 `ai.sonnetdb.com` 自动分流
