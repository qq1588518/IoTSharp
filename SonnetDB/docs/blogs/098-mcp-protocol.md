## SonnetDB MCP 协议支持：以 Model Context Protocol 赋能 AI 驱动数据库管理

随着大语言模型（LLM）在开发工具中的广泛应用，数据库与 AI 助手的深度集成成为新的技术趋势。SonnetDB 率先实现了对 **Model Context Protocol（MCP）** 的原生支持，使 AI 助手能够直接与数据库交互，执行查询、分析数据和生成报告，将自然语言驱动的数据库管理从概念变为现实。

### MCP 协议概述

Model Context Protocol（MCP）是由 Anthropic 开放标准定义的一种协议，旨在为 AI 模型提供标准化的工具调用接口。MCP 定义了三个核心概念：

- **Tools（工具）**：AI 模型可以调用的函数，如执行 SQL 查询、获取表结构
- **Resources（资源）**：AI 模型可以读取的数据，如查询结果、系统状态
- **Prompts（提示模板）**：预定义的交互模板，引导 AI 执行特定任务

SonnetDB 实现了 MCP 服务端，通过标准化的 MCP 接口暴露数据库管理能力，使任何支持 MCP 的 AI 客户端（如 Claude Code、Claude Desktop）都能直接管理 SonnetDB。

### SonnetDB MCP 工具集

SonnetDB 的 MCP 实现提供了一组丰富的工具，覆盖数据库管理的常见操作：

```typescript
// MCP 工具定义（JSON Schema 格式）
{
  "tools": [
    {
      "name": "query",
      "description": "在 SonnetDB 上执行 SQL 查询并返回结果",
      "inputSchema": {
        "type": "object",
        "properties": {
          "sql": {
            "type": "string",
            "description": "要执行的 SQL 语句"
          },
          "limit": {
            "type": "integer",
            "description": "最大返回行数",
            "default": 100
          }
        },
        "required": ["sql"]
      }
    },
    {
      "name": "show_measurements",
      "description": "列出所有 Measurement 及其 Schema",
      "inputSchema": {
        "type": "object",
        "properties": {}
      }
    },
    {
      "name": "describe_measurement",
      "description": "查看指定 Measurement 的详细定义",
      "inputSchema": {
        "type": "object",
        "properties": {
          "name": {
            "type": "string",
            "description": "Measurement 名称"
          }
        },
        "required": ["name"]
      }
    },
    {
      "name": "system_status",
      "description": "获取数据库系统状态和统计信息",
      "inputSchema": {
        "type": "object",
        "properties": {}
      }
    }
  ]
}
```

### 实战：自然语言驱动数据库操作

配置 MCP 后，开发者可以在 AI 助手中通过自然语言直接操作 SonnetDB：

```bash
# 在 Claude Code 中配置 SonnetDB MCP
# .claude/settings.json
{
  "mcpServers": {
    "sonnetdb": {
      "command": "sonnetdb-mcp-server",
      "args": ["--db-path", "./data/mydb"]
    }
  }
}
```

然后用户可以直接用自然语言与数据库交互：

```text
用户：查询过去一小时 CPU 使用率最高的 5 台服务器是什么？

AI 助手（调用 SonnetDB MCP 工具）：
→ 执行 query("SELECT host, max(usage) AS max_usage
              FROM cpu WHERE time > now() - 3600000
              GROUP BY host ORDER BY max_usage DESC LIMIT 5")

→ 返回结果：
  ┌───────────┬───────────┐
  │ host      │ max_usage │
  ├───────────┼───────────┤
  │ server-07 │    0.98   │
  │ server-12 │    0.95   │
  │ server-03 │    0.93   │
  │ server-19 │    0.91   │
  │ server-05 │    0.89   │
  └───────────┴───────────┘
```

### 权限安全模型

SonnetDB MCP 实现了多层次的权限控制，确保 AI 驱动的数据库操作安全可控：

```csharp
public class McpPermissionModel
{
    /// <summary>
    /// MCP 操作权限分为三个等级：
    /// ReadOnly - 仅查询，不可修改数据或结构
    /// ReadWrite - 可查询和写入数据，但不可修改 Schema
    /// Admin - 完全控制权限
    /// </summary>
    public enum McpAccessLevel { ReadOnly, ReadWrite, Admin }

    public bool ValidateOperation(string toolName, McpAccessLevel userLevel)
    {
        return toolName switch
        {
            "query"             => userLevel >= McpAccessLevel.ReadOnly,
            "show_measurements"  => userLevel >= McpAccessLevel.ReadOnly,
            "describe_measurement" => userLevel >= McpAccessLevel.ReadOnly,
            "insert_data"       => userLevel >= McpAccessLevel.ReadWrite,
            "create_measurement" => userLevel >= McpAccessLevel.Admin,
            "drop_measurement"  => userLevel >= McpAccessLevel.Admin,
            _ => false
        };
    }
}
```

MCP 权限与 SonnetDB 内置的用户角色系统集成，管理员可以为不同用户分配不同的 MCP 访问级别。所有 MCP 操作都会被记录到审计日志中，支持事后追溯。

通过 MCP 协议的支持，SonnetDB 率先实现了 AI 原生数据库的愿景。开发者无需记忆复杂的 SQL 语法，通过自然语言即可完成数据查询、性能分析和日常管理任务，大幅降低了时序数据库的使用门槛。
