namespace SonnetDB.Copilot;

/// <summary>
/// 文本嵌入 provider 抽象。
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>
    /// 为单条文本生成 embedding 向量。
    /// </summary>
    /// <param name="text">输入文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>embedding 向量。</returns>
    ValueTask<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
