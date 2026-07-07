## 案例：用 SonnetDB + AI Copilot 构建无代码数据分析平台

### 背景

某工业 SaaS 创业公司为中小制造企业提供"设备数据分析"服务。目标客户是没有专业数据工程师的中小工厂，他们有传感器数据，但不会写 SQL，更不懂机器学习。公司希望构建一个"让普通工厂员工也能用自然语言分析数据"的 SaaS 平台，核心能力是将用户的自然语言问题自动转换为正确的 SonnetDB SQL，并呈现可视化结果。

### 挑战

- **用户门槛**：目标用户是工厂质检员、班组长，不懂技术，只会说"最近良品率为什么下降了"
- **SQL 准确性**：自动生成的 SQL 必须可执行且结果正确，不能让用户看到报错
- **多租户**：每个工厂客户的数据结构略有不同，需要动态感知 Schema
- **成本控制**：每次自然语言查询都需要调用 AI，API 成本需要控制

### 整体架构

```
用户自然语言输入
       ↓
SonnetDB Copilot（知识库检索 + SQL 生成）
       ↓
SQL 自纠正执行引擎（最多重试 3 次）
       ↓
SonnetDB 时序查询
       ↓
结果 + 自然语言解释
       ↓
Web 图表 / 地图可视化
```

### 方案设计

**多租户数据模型**：每个工厂客户一个数据库，SonnetDB 的 `CREATE DATABASE` 即可完成隔离。

```sql
-- 为新客户"东方精工"开通数据库
CREATE DATABASE dong_fang_jg;

-- 创建客户专属的数据分析账号
CREATE USER dfjg_analyst WITH PASSWORD 'xxx';
GRANT READ ON dong_fang_jg TO dfjg_analyst;

-- 颁发 90 天 Token 给客户 Web 应用
ISSUE TOKEN 'dfjg_webapp_token' FOR DATABASE dong_fang_jg WITH ROLE readonly EXPIRE IN 90d;
```

**知识库自动构建**：将客户的数据字典、业务术语、历史问题写入 Copilot 知识库。

```
copilot/
  skills/
    dong_fang_jg/
      schema.md      -- 该客户的 Measurement 结构说明
      glossary.md    -- 业务术语（如"良品率"对应哪个 FIELD）
      common_queries.md  -- 常用分析模板
```

Copilot 启动时自动扫描并索引这些文档，生成 SQL 时能准确使用客户特定的表名和字段名。

**自然语言转 SQL 示例**：

用户提问："最近一周 2 号线的良品率是多少？"

Copilot 根据知识库中的 Schema 说明（`quality_score > 95` 定义为良品）生成：

```sql
SELECT strftime('%Y-%m-%d', time) AS day,
       count(*) FILTER (WHERE quality_score >= 95) * 100.0 / count(*) AS pass_rate_pct,
       count(*) AS total_count
FROM production_quality
WHERE line_id = 'LINE-02'
  AND time > NOW() - INTERVAL '7d'
GROUP BY day
ORDER BY day;
```

**SQL 自纠正机制**：Copilot 内置 SQL 执行验证，如果生成的 SQL 有语法错误或返回空结果，自动重试最多 3 次，确保用户看到的永远是可执行的有效查询。

```
生成 SQL → 执行 → 失败（语法错）
→ 携带错误信息重新生成 → 执行 → 成功
→ 返回结果 + 自然语言解释
```

**MCP 协议集成**：平台还通过 MCP 协议将 SonnetDB 接入客户的 Claude Desktop，让工厂工程师可以直接在 AI 助手中查询数据。

MCP 配置示例：

```json
{
  "mcpServers": {
    "dong_fang_jg": {
      "url": "https://api.saas.example.com/mcp/dong_fang_jg",
      "headers": {
        "Authorization": "Bearer dfjg_webapp_token"
      }
    }
  }
}
```

工程师在 Claude Desktop 中直接问："帮我查一下今天早班的设备故障率" — Claude 自动调用 `query_sql` 工具执行查询，返回结构化结果和分析建议。

**页面感知 CopilotDock**：在 Web 管理界面中，CopilotDock 会感知当前页面上下文。当用户在"良品率趋势图"页面打开 Copilot，AI 自动理解当前正在查看的是哪条产线的哪个指标，提供上下文相关的快捷分析操作。

**核心分析功能 SQL 模板**：

```sql
-- 良品率趋势（7 日）
SELECT strftime('%Y-%m-%d', time) AS day,
       count(*) FILTER (WHERE quality_score >= 95) * 100.0 / count(*) AS pass_rate
FROM production_quality
WHERE line_id = @line_id AND time > NOW() - INTERVAL '7d'
GROUP BY day ORDER BY day;

-- 不良品分类占比
SELECT defect_type, count(*) AS count,
       count(*) * 100.0 / sum(count(*)) OVER () AS pct
FROM production_quality
WHERE quality_score < 95 AND time > NOW() - INTERVAL '7d'
GROUP BY defect_type ORDER BY count DESC;

-- 设备停机时长排名
SELECT machine_id,
       state_duration(CASE WHEN status = 0 THEN 1 ELSE 0 END, 1) / 3600 AS downtime_hours
FROM machine_status
WHERE time > NOW() - INTERVAL '7d'
GROUP BY machine_id ORDER BY downtime_hours DESC LIMIT 10;
```

**嵌入式集成**：SaaS 平台的后端服务直接嵌入 SonnetDB 引擎，无需单独部署数据库服务：

```csharp
// SaaS 平台后端
public class AnalyticsService
{
    private readonly IExecutor _executor;

    public AnalyticsService(Tsdb db)
    {
        _executor = db.GetExecutor();
    }

    public async Task<QueryResult> NaturalLanguageQueryAsync(
        string tenantDb, string userQuestion, string userId)
    {
        // 通过 Copilot API 将自然语言转为 SQL
        var sql = await _copilot.GenerateSqlAsync(tenantDb, userQuestion);

        // 执行（内置重试）
        return await _executor.QueryAsync(sql);
    }
}
```

**成本优化**：通过知识库检索（RAG）减少发送给 LLM 的上下文量，同时对高频相同问题进行 SQL 缓存：

```sql
-- 缓存高频查询的结果（存回 SonnetDB 自身）
INSERT INTO query_cache (time, query_hash, result_json, expires_at)
VALUES (NOW(), @hash, @result, NOW() + INTERVAL '5m');
```

### 实施效果

上线 6 个月，服务 45 家中小工厂客户：

| 指标 | 数据 |
| --- | --- |
| 平均每日 Copilot 对话次数 | 320 次/客户 |
| SQL 生成首次成功率 | 87% |
| 经过自纠正后总成功率 | 98.2% |
| 用户平均找到答案时间 | 从 2 天（人工报表）→ 45 秒 |
| 客户续费率 | 91%（行业平均 65%） |
| 每次查询平均 API 成本 | ¥0.03（知识库 RAG 优化后） |

"以前要让数据分析师做报表，等两天。现在我直接问 AI，30 秒就有答案。"——某客户质检主管

SonnetDB 的 Copilot 架构——内置知识库、SQL 自纠正、MCP 协议、页面感知 CopilotDock——让这家 SaaS 公司用一个工程师就构建并维护了覆盖 45 家客户的"无代码分析平台"，而不是为每家客户定制开发报表系统。
