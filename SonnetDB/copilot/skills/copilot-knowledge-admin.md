---
name: copilot-knowledge-admin
description: SonnetDB Copilot 知识库管理：文档摄入、索引重建、技能召回排查、模型选择、知识库状态查询、Copilot 自身诊断。
triggers:
  - 知识库
  - knowledge
  - 技能
  - skill
  - copilot
  - 召回
  - 摄入
  - ingest
  - 索引
  - index
  - 重建
  - rebuild
  - 模型选择
  - embedding model
  - 嵌入模型
  - __copilot__
  - skill_search
  - skill_load
  - docs_search
  - 文档更新
  - 技能不触发
  - 回答不准确
requires_tools:
  - query_sql
  - list_measurements
  - skill_search
  - skill_load
  - docs_search
---

# Copilot 知识库管理指南

SonnetDB Copilot 的知识库存储在 `__copilot__` 数据库中，技能文件位于 `copilot/skills/`，文档知识库来自 `docs/` 目录的摄入。

---

## 1. 系统架构

```
docs/*.md                    copilot/skills/*.md
    │                               │
    ▼ 按 H2/H3 切片                 ▼ 启动时加载
    │ ≤800字/100字重叠              │
    ▼ 嵌入模型生成向量              ▼ SkillRegistry
    │                               │
    ▼                               ▼
__copilot__ 数据库             Agent System Prompt
├─ docs (measurement)          （技能描述+触发词注入）
│  ├─ embedding VECTOR(N)
│  ├─ content FIELD STRING
│  ├─ source TAG
│  └─ heading FIELD STRING
└─ skills (measurement)
   ├─ name TAG
   ├─ description FIELD STRING
   └─ triggers FIELD STRING
```

---

## 2. 知识库状态查询

### 查看知识库数据库

```sql
-- 切换到 __copilot__ 数据库后查看
SHOW MEASUREMENTS;
-- 预期：docs, skills（或类似名称）

-- 查看文档分块数量
SELECT count(*) FROM docs;

-- 查看技能数量
SELECT count(*) FROM skills;

-- 查看最近摄入的文档
SELECT time, source, heading FROM docs
ORDER BY time DESC
LIMIT 20;
```

### 通过 MCP 工具查询

```
# 搜索知识库（调试召回效果）
skill_search(query="WAL 崩溃恢复", k=5)

# 加载特定技能
skill_load(name="wal-recovery")

# 搜索文档知识库
docs_search(query="向量搜索 KNN 语法", k=5)
```

### 通过 HTTP API 查询

```bash
# 查看 Copilot 状态
GET /v1/copilot/status
Authorization: Bearer <admin-token>

# 响应示例
{
  "status": "ready",
  "knowledgeChunks": 342,
  "skillsLoaded": 14,
  "embeddingModel": "nomic-embed-text",
  "llmModel": "qwen2.5:7b"
}
```

---

## 3. 文档摄入（知识库更新）

### 摄入流程

```
docs/*.md 文件更新
    │
    ▼ 触发重新摄入
    │  - 服务重启（自动）
    │  - 手动触发 API
    │
    ▼ 文档切片（按 H2/H3 标题）
    │  每片 ≤800 字，100 字重叠
    │
    ▼ 嵌入模型生成向量
    │  （Ollama / OpenAI）
    │
    ▼ 写入 __copilot__.docs measurement
    │
    ▼ 知识库就绪
```

### 手动触发重新摄入

```bash
# 触发全量重新摄入
POST /v1/copilot/ingest
Authorization: Bearer <admin-token>
Content-Type: application/json

{"force": true}

# 触发特定文档重新摄入
POST /v1/copilot/ingest
Authorization: Bearer <admin-token>
Content-Type: application/json

{"sources": ["docs/sql-reference.md", "docs/vector-search.md"]}
```

### 摄入新文档

1. 将 Markdown 文件放入 `docs/` 目录
2. 确保文件有清晰的 H2/H3 标题结构（切片依据）
3. 触发重新摄入或重启服务
4. 验证：`SELECT count(*) FROM docs WHERE source = 'docs/new-file.md'`

**文档格式建议：**
```markdown
# 文档标题（H1，不用于切片）

## 主要章节（H2，切片边界）

内容不超过 800 字...

### 子章节（H3，也是切片边界）

更详细的内容...
```

---

## 4. 技能管理

### 技能文件结构

```yaml
---
name: skill-name              # 技能唯一标识
description: 一句话描述       # 用于召回匹配
triggers:                     # 触发关键词列表
  - keyword1
  - keyword2
requires_tools:               # 需要的 MCP 工具
  - query_sql
  - list_measurements
---

# 技能内容（Markdown 格式）
```

### 添加新技能

1. 在 `copilot/skills/` 创建 `<skill-name>.md`
2. 填写完整的 frontmatter（name/description/triggers/requires_tools）
3. 重启服务或触发技能重新加载
4. 验证：`skill_search(query="新技能的触发词")`

### 技能召回排查

**问题：技能不触发**

```
排查步骤：
1. 确认技能文件存在且 frontmatter 格式正确
   - name 字段不能有空格
   - triggers 必须是 YAML 列表格式
   - 缩进必须用空格（不能用 Tab）

2. 检查触发词是否匹配用户意图
   - 触发词应覆盖中英文变体
   - 包含常见同义词和缩写

3. 用 skill_search 测试召回
   skill_search(query="用户的实际问题", k=10)
   - 查看目标技能是否在结果中
   - 查看 distance 值（< 0.5 为高相关）

4. 确认 SkillRegistry 已加载该技能
   - 检查服务启动日志
   - 查看 /v1/copilot/status 中的 skillsLoaded 数量
```

**问题：技能触发但回答不准确**

```
排查步骤：
1. 检查技能内容是否与项目实际情况一致
   - 技能文件中的 SQL 语法是否正确
   - API 端点是否最新
   - 示例代码是否可运行

2. 检查知识库文档是否最新
   docs_search(query="相关主题", k=5)
   - 查看返回的文档内容是否准确

3. 检查嵌入模型是否一致
   - 摄入时用的模型 vs 查询时用的模型必须相同
   - 模型变更后需要全量重新摄入
```

---

## 5. 嵌入模型配置

### 支持的嵌入模型

| 模型 | 提供商 | 维度 | 适用场景 |
|------|--------|------|----------|
| `nomic-embed-text` | Ollama（本地） | 768 | 中英文混合，推荐 |
| `mxbai-embed-large` | Ollama（本地） | 1024 | 高质量，较慢 |
| `text-embedding-3-small` | OpenAI | 1536 | 云端，需 API Key |
| `text-embedding-3-large` | OpenAI | 3072 | 最高质量，成本高 |
| `text-embedding-ada-002` | OpenAI | 1536 | 旧版，兼容性好 |

### 配置示例

```json
// appsettings.json 或 Copilot 配置
{
  "Copilot": {
    "EmbeddingProvider": "ollama",
    "EmbeddingModel": "nomic-embed-text",
    "EmbeddingEndpoint": "http://127.0.0.1:11434",
    "LlmProvider": "ollama",
    "LlmModel": "qwen2.5:7b",
    "LlmEndpoint": "http://127.0.0.1:11434"
  }
}
```

**⚠️ 重要：** 更换嵌入模型后，必须全量重新摄入知识库，否则向量维度不匹配会导致召回失败。

### 模型变更流程

```bash
# 1. 停止服务
# 2. 修改配置文件中的嵌入模型
# 3. 清空旧的知识库向量
# （通过 DELETE 或重建 __copilot__ 数据库）
# 4. 重启服务（自动重新摄入）
# 5. 验证知识库状态
curl http://127.0.0.1:5080/v1/copilot/status
```

---

## 6. LLM 模型配置

### 支持的 LLM

| 模型 | 提供商 | 特点 |
|------|--------|------|
| `qwen2.5:7b` | Ollama（本地） | 中文优秀，推荐 |
| `qwen2.5:14b` | Ollama（本地） | 更强，需要更多内存 |
| `llama3.2:3b` | Ollama（本地） | 轻量快速 |
| `gpt-4o-mini` | OpenAI | 云端，性价比高 |
| `gpt-4o` | OpenAI | 最强，成本高 |

### 流式响应配置

SonnetDB Copilot 支持 SSE 流式输出：

```bash
POST /v1/copilot/chat
Authorization: Bearer <token>
Content-Type: application/json
Accept: text/event-stream

{
  "message": "如何查询最近 1 小时的 CPU 使用率？",
  "database": "metrics",
  "stream": true
}
```

---

## 7. 知识库重建

### 完整重建流程

```bash
# 1. 备份当前知识库（可选）
# 2. 删除 __copilot__ 数据库中的旧数据
curl -X POST "http://127.0.0.1:5080/v1/sql" \
  -H "Authorization: Bearer <admin-token>" \
  -d '{"sql": "DROP DATABASE __copilot__"}'

# 3. 重启服务（自动重建）
# 或触发重新摄入
curl -X POST "http://127.0.0.1:5080/v1/copilot/ingest" \
  -H "Authorization: Bearer <admin-token>" \
  -d '{"force": true}'

# 4. 等待摄入完成（观察日志）
# 5. 验证
curl http://127.0.0.1:5080/v1/copilot/status
```

### 增量更新（只更新变更的文档）

```bash
# 只重新摄入特定文件
curl -X POST "http://127.0.0.1:5080/v1/copilot/ingest" \
  -H "Authorization: Bearer <admin-token>" \
  -d '{"sources": ["docs/sql-reference.md"]}'
```

---

## 8. 常见问题

**Q: Copilot 回答"我不知道"但文档中有答案？**  
A: 知识库召回失败。用 `docs_search` 测试，检查文档是否已摄入，嵌入模型是否一致。

**Q: 技能文件更新后不生效？**  
A: 技能在服务启动时加载，需要重启服务或触发技能重新加载 API。

**Q: 知识库摄入很慢？**  
A: 嵌入模型运行在 CPU 上时较慢。使用 GPU 加速的 Ollama 或切换到 OpenAI API 可显著提速。

**Q: 向量维度不匹配错误？**  
A: 嵌入模型已更换但知识库未重建。执行完整重建流程。

**Q: `__copilot__` 数据库占用磁盘过大？**  
A: 知识库文档过多或分块粒度过细。检查 `docs/` 目录文件数量，考虑增大切片大小（减少分块数）。

**Q: skill_search 返回结果 distance 都很大（> 1.5）？**  
A: 嵌入模型与摄入时不同，或知识库为空。检查 `/v1/copilot/status` 中的 `knowledgeChunks` 和 `embeddingModel`。
