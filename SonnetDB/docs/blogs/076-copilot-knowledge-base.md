## 自动文档摄入管道：Chunking、Embedding 与向量检索

SonnetDB Copilot 的知识库系统是一条完整的 ETL 管道：从扫描源文件到向量检索，全程自动化，零人工干预。

### 三阶段摄入流程

`DocsIngestor` 是知识库的核心引擎，在服务启动时由 `CopilotDocsIngestionService` 后台自动触发：

```
扫描 → 分块 → 嵌入 → 写入
```

**阶段一：扫描（Scan）**

`DocsSourceScanner` 递归扫描配置的文档根目录（默认为 `./docs`、`./web/help`、`./src/SonnetDB/wwwroot/help`），提取 Markdown 和 HTML 文件的元数据，计算 SHA-256 指纹用于增量识别。

**阶段二：分块（Chunk）**

`DocsChunker` 是精细的文档分割器：

- Markdown 文件按标题层级（`#`、`##`、`###`）切分为语义段落
- 超大段落按配置的 `ChunkSize`（默认 800 字符）做软分割，在最近的单词边界切断
- 相邻块之间保留 `ChunkOverlap`（默认 100 字符）的重叠，避免语义断裂

```csharp
public IReadOnlyList<DocsChunk> Chunk(DocsSourceFile sourceFile)
{
    var sections = SplitMarkdownSections(text, sourceFile.Title);
    foreach (var section in sections)
    {
        var pieces = SplitOversizedSection(section.Content);
        foreach (var piece in pieces)
            chunks.Add(new DocsChunk(..., piece));
    }
}
```

**阶段三：嵌入与写入（Embed & Write）**

每个块调用 `IEmbeddingProvider` 生成 384 维向量，然后通过 SQL 语句写入 `__copilot__.docs` 表：

```csharp
var embedding = await _embeddingProvider.EmbedAsync(chunk.Content, cancellationToken);
var statement = new InsertStatement(
    DocsMeasurementName,
    ["source", "section", "title", "content", "time", "embedding"],
    rows);
SqlExecutor.ExecuteStatement(database, statement);
```

### 增量摄入与状态追踪

系统维护 `docs_state` 表记录每个文件的指纹和修改时间。下次启动时只有指纹变化的文件会被重新嵌入，未变动的文件直接跳过，大幅提升启动速度。

### 向量检索

`DocsSearchService.SearchAsync()` 接收用户问题，先调用 `IEmbeddingProvider` 将其转为向量，然后用 SQL 的 `knn()` 表值函数在 `__copilot__.docs` 上执行近似最近邻搜索：

```sql
SELECT * FROM docs
WHERE knn(docs, embedding, <query_vector>, 5);
```

返回前 K 条最相似的文档段落，连同它们的来源、标题和章节信息，供 Agent 在回答时引用。

### 配置调优

通过 `appsettings.json` 可以调整知识库行为：

```json
{
  "SonnetDBServer": {
    "Copilot": {
      "Docs": {
        "AutoIngestOnStartup": true,
        "Roots": ["./docs", "./web/help"],
        "ChunkSize": 800,
        "ChunkOverlap": 100
      }
    }
  }
}
```

对于大型文档库，可以调低 `ChunkSize` 获得更精准的检索粒度，或调高 `ChunkOverlap` 让块间过渡更自然。
