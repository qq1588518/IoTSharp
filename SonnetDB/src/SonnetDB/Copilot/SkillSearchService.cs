namespace SonnetDB.Copilot;

/// <summary>
/// 将自然语言查询嵌入为向量并在 <c>__copilot__.skills</c> 上做 knn 检索（PR #65）。
/// </summary>
internal sealed class SkillSearchService
{
    private readonly SkillRegistry _registry;
    private readonly IEmbeddingProvider _embeddingProvider;

    public SkillSearchService(SkillRegistry registry, IEmbeddingProvider embeddingProvider)
    {
        _registry = registry;
        _embeddingProvider = embeddingProvider;
    }

    public async Task<IReadOnlyList<SkillSearchHit>> SearchAsync(string query, int k, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (k <= 0)
            throw new InvalidOperationException("k 必须大于 0。");

        var embedding = await _embeddingProvider.EmbedAsync(query, cancellationToken).ConfigureAwait(false);
        return _registry.Search(embedding, k);
    }
}
