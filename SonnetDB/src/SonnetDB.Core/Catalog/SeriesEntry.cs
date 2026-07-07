using System.Collections.Frozen;
using SonnetDB.Model;

namespace SonnetDB.Catalog;

/// <summary>
/// SeriesCatalog 中的一条目录项：序列的全部元数据。
/// </summary>
public sealed class SeriesEntry
{
    /// <summary>序列唯一标识（<see cref="SeriesId.Compute(SeriesKey)"/> 返回的 XxHash64 值）。</summary>
    public ulong Id { get; }

    /// <summary>规范化序列键。</summary>
    public SeriesKey Key { get; }

    /// <summary>Measurement 名称。</summary>
    public string Measurement { get; }

    /// <summary>Tag 键值对集合（不可变，调用方修改原字典不影响此属性）。</summary>
    public IReadOnlyDictionary<string, string> Tags { get; }

    /// <summary>条目创建时间（UTC Ticks）。</summary>
    public long CreatedAtUtcTicks { get; }

    /// <summary>
    /// 初始化 <see cref="SeriesEntry"/>。
    /// </summary>
    /// <param name="id">序列唯一标识。</param>
    /// <param name="key">规范化序列键。</param>
    /// <param name="measurement">Measurement 名称。</param>
    /// <param name="tags">Tag 键值对集合（将被复制为不可变字典）。</param>
    /// <param name="createdAtUtcTicks">条目创建时间（UTC Ticks）。</param>
    internal SeriesEntry(
        ulong id,
        SeriesKey key,
        string measurement,
        IReadOnlyDictionary<string, string> tags,
        long createdAtUtcTicks)
    {
        Id = id;
        Key = key;
        Measurement = measurement;
        Tags = tags.ToFrozenDictionary(StringComparer.Ordinal);
        CreatedAtUtcTicks = createdAtUtcTicks;
    }
}
