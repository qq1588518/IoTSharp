using SonnetDB.Sql.Ast;
using SonnetDB.Sql.Execution;

namespace SonnetDB.Copilot;

/// <summary>
/// 文档检索结果。
/// </summary>
internal sealed record DocsSearchResult(
    string Source,
    string Title,
    string Section,
    string Content,
    double Score,
    long Time);

/// <summary>
/// 封装 docs knowledge 库的 embedding 检索。
/// </summary>
internal sealed class DocsSearchService
{
    private readonly DocsIngestor _ingestor;
    private readonly IEmbeddingProvider _embeddingProvider;

    public DocsSearchService(DocsIngestor ingestor, IEmbeddingProvider embeddingProvider)
    {
        _ingestor = ingestor;
        _embeddingProvider = embeddingProvider;
    }

    public async Task<IReadOnlyList<DocsSearchResult>> SearchAsync(string query, int k, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (k <= 0)
            throw new InvalidOperationException("k 必须大于 0。");

        var embedding = await _embeddingProvider.EmbedAsync(query, cancellationToken).ConfigureAwait(false);
        if (embedding.Length != DocsIngestor.ExpectedEmbeddingDimensions)
            throw new InvalidOperationException($"embedding 维度必须为 {DocsIngestor.ExpectedEmbeddingDimensions}，实际为 {embedding.Length}。");

        var database = _ingestor.GetKnowledgeDb();
        if (database.Measurements.TryGet(DocsIngestor.DocsMeasurementName) is null)
            return [];

        var queryVector = new VectorLiteralExpression(embedding.Select(static value => (double)value).ToArray());
        var statement = new SelectStatement(
            [new SelectItem(StarExpression.Instance, null)],
            DocsIngestor.DocsMeasurementName,
            Where: null,
            GroupBy: [],
            TableValuedFunction: new FunctionCallExpression(
                "knn",
                [
                    new IdentifierExpression(DocsIngestor.DocsMeasurementName),
                    new IdentifierExpression("embedding"),
                    queryVector,
                    LiteralExpression.Integer(k),
                ]),
            Pagination: new PaginationSpec(0, k));

        var result = SqlExecutor.ExecuteStatement(database, statement);
        if (result is not SelectExecutionResult selectResult)
            return [];

        var rows = new List<DocsSearchResult>(selectResult.Rows.Count);
        foreach (var row in selectResult.Rows)
        {
            rows.Add(new DocsSearchResult(
                Source: row.Count > 2 ? row[2]?.ToString() ?? string.Empty : string.Empty,
                Title: row.Count > 4 ? row[4]?.ToString() ?? string.Empty : string.Empty,
                Section: row.Count > 3 ? row[3]?.ToString() ?? string.Empty : string.Empty,
                Content: row.Count > 5 ? row[5]?.ToString() ?? string.Empty : string.Empty,
                Score: row.Count > 1 && row[1] is double score ? score : 0d,
                Time: row.Count > 0 && row[0] is long time ? time : 0L));
        }

        return rows;
    }
}
