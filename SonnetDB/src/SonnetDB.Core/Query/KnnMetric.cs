namespace SonnetDB.Query;

/// <summary>
/// KNN 搜索使用的距离度量方式。
/// 所有度量均满足"值越小 = 两向量越相似"的约定，方便统一按升序排列最近邻。
/// </summary>
public enum KnnMetric
{
    /// <summary>
    /// 余弦距离（默认）：1 − 余弦相似度，值域 [0, 2]，越小越相似。
    /// 适合文本嵌入等归一化向量场景。
    /// </summary>
    Cosine = 0,

    /// <summary>
    /// L2（欧几里得）距离：√(Σ(aᵢ − bᵢ)²)，越小越相似。
    /// 适合需要绝对距离度量的场景。
    /// </summary>
    L2 = 1,

    /// <summary>
    /// 负内积：−(a·b)，内积越大 → 负内积越小 → 越相似。
    /// 适合已归一化、希望最大化点积的场景（等同于最小化余弦距离但不带开方）。
    /// </summary>
    InnerProduct = 2,
}
