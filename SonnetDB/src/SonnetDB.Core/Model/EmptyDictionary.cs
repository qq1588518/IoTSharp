namespace SonnetDB.Model;

/// <summary>
/// 内部空字典单例，避免在 <see cref="Point.Create"/> 默认参数路径上分配对象。
/// </summary>
internal static class EmptyDictionary<TKey, TValue> where TKey : notnull
{
    /// <summary>可复用的空只读字典实例。</summary>
    public static readonly IReadOnlyDictionary<TKey, TValue> Instance =
        new Dictionary<TKey, TValue>(0);
}
