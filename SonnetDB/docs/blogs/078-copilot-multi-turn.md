## 多轮对话与 SQL 自修复：可达 3 次重试的智能 Agent

SonnetDB Copilot 支持真正意义上的多轮对话——Agent 维护完整的对话历史，并能在 SQL 执行失败时自动修复，最多重试 3 次。

### 多轮对话机制

`CopilotAgent.PrepareConversation()` 处理多轮对话的完整生命周期：

```csharp
private static CopilotConversation PrepareConversation(IReadOnlyList<AiMessage> messages)
{
    var normalized = NormalizeMessages(messages);
    var trimmed = TrimConversation(normalized);
    // 找到最新的用户消息作为当前问题
    var latestUserIndex = FindLatestUserMessageIndex(trimmed);
    var activeMessages = trimmed.Take(latestUserIndex + 1).ToArray();
    // 之前的所有轮次作为历史
    var history = activeMessages[..latestUserIndex];
    // 构建检索用的增强查询（包含历史摘要）
    var retrievalQuery = BuildRetrievalQuery(activeMessages);
    return new CopilotConversation(activeMessages, history, ..., retrievalQuery);
}
```

关键特性：
- **Token 预算裁剪**：历史消息按 1200 token 预算自动裁剪，确保 LLM 上下文窗口不被历史撑爆
- **上下文感知检索**：`BuildRetrievalQuery()` 会将历史摘要与最新问题拼接，提升向量召回的相关性
- **多轮状态保留**：每个工具的执行结果通过 `CopilotToolObservation` 记录，后续轮次的 Planner 可以引用

### ReAct 多轮循环

Copilot 采用标准的 ReAct（推理-行动）模式：

```
Round 1: Planner 分析问题 → 决定执行 list_measurements
        → 返回 measurement 列表
Round 2: Planner 看到列表 → 决定执行 describe_measurement
        → 返回表结构
Round 3: Planner 获得完整 schema → 执行 query_sql
        → 返回查询结果
→ 汇总所有观测 → 生成最终回答
```

最多 6 轮循环，每轮只执行一个工具，避免了并行工具调用可能带来的冲突。

### SQL 自修复：3 次重试

这是 Copilot 最实用的能力之一。当 `query_sql` 执行失败时，`ExecuteQuerySqlWithRepairAsync()` 不会直接放弃，而是启动修复循环：

```csharp
for (var attempt = 1; attempt <= MaxSqlRepairAttempts; attempt++)
{
    try
    {
        return TryExecuteQuerySql(context, currentTool);
    }
    catch (SqlExecutionException ex) when (attempt < MaxSqlRepairAttempts)
    {
        // 将错误信息喂给 LLM，让 LLM 改写 SQL
        var rewrittenSql = await RepairSqlAsync(..., ex, cancellationToken);
        currentTool = currentTool with { Sql = rewrittenSql };
        // 重试
    }
}
```

修复流程：
1. 捕获 SQL 解析或执行异常
2. 将错误信息、原始 SQL、表结构上下文打包成一个修复提示
3. 调用 LLM 生成修正版 SQL
4. 用修正版 SQL 重新执行
5. 仍然失败则重复以上步骤（最多 3 次）

### 进程内 SQL 队列

所有 SQL 工具调用最终通过 `SqlExecutor.ExecuteStatement()` 在 SonnetDB 进程内执行，不走网络。这意味着：

- **零网络延迟**：从 Agent 决定执行 SQL 到结果返回，全部在同一个进程内完成
- **无序列化开销**：AST 级别的 SQL 执行，不需要字符串编解码
- **事务一致性**：后续的 `execute_sql` 写入立即可见，无需等待 WAL 刷盘

这种"AI Agent 直接嵌入数据库进程"的架构，使得 Copilot 的 SQL 执行延迟通常在毫秒级别，远低于传统的"AI + 数据库"分体部署方案。
