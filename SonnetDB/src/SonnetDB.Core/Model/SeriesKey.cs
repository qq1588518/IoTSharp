using System.Text;

namespace SonnetDB.Model;

/// <summary>
/// 规范化的序列键：将 <c>(measurement, tags)</c> 折叠为一个确定性字符串，
/// 是引擎内部标识一条逻辑时序序列的唯一标识。
/// </summary>
/// <remarks>
/// <para><b>规范化规则（hard spec）：</b></para>
/// <list type="number">
///   <item><description><c>measurement</c> 原样（已在 <see cref="PointValidation"/> 中校验）。</description></item>
///   <item><description><c>tags</c> 按 key 的 <see cref="StringComparison.Ordinal"/> 升序排序。</description></item>
///   <item><description>
///     拼接格式（UTF-8）：
///     <list type="bullet">
///       <item><description>无 tag：<c>{measurement}</c></description></item>
///       <item><description>有 tag：<c>{measurement},{k1}={v1},{k2}={v2},…</c>（按 key Ordinal 升序）</description></item>
///     </list>
///   </description></item>
/// </list>
/// <para>
/// 规范化后的字符串通过 <see cref="SeriesId.Compute(SeriesKey)"/> 计算
/// <c>XxHash64</c> 得到 <c>ulong SeriesId</c>，作为 MemTable / Block / Index 的主键。
/// </para>
/// </remarks>
public readonly struct SeriesKey : IEquatable<SeriesKey>
{
    /// <summary>Measurement 名称。</summary>
    public string Measurement { get; }

    /// <summary>Tag 键值对集合（按 key Ordinal 升序已排序的只读视图）。</summary>
    public IReadOnlyDictionary<string, string> Tags { get; }

    /// <summary>规范化字符串：<c>measurement</c> 或 <c>measurement,k1=v1,k2=v2</c>。</summary>
    public string Canonical { get; }

    /// <summary>
    /// 从 <paramref name="measurement"/> 和可选的 <paramref name="tags"/> 构造规范化序列键。
    /// </summary>
    /// <param name="measurement">Measurement 名称（非空，已通过 <see cref="PointValidation"/> 校验）。</param>
    /// <param name="tags">Tag 键值对；为 null 时等同于空字典。</param>
    /// <exception cref="ArgumentException"><paramref name="measurement"/> 为空白或含保留字符。</exception>
    public SeriesKey(string measurement, IReadOnlyDictionary<string, string>? tags = null)
    {
        PointValidation.ValidateMeasurement(measurement);
        Measurement = measurement;

        var resolvedTags = tags ?? EmptyDictionary<string, string>.Instance;
        Tags = resolvedTags;
        Canonical = BuildCanonical(measurement, resolvedTags);
    }

    /// <summary>
    /// 从 <see cref="Point"/> 构造规范化序列键。
    /// </summary>
    /// <param name="point">已校验的数据点。</param>
    /// <exception cref="ArgumentNullException"><paramref name="point"/> 为 null。</exception>
    public static SeriesKey FromPoint(Point point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return new SeriesKey(point.Measurement, point.Tags);
    }

    // ── 私有辅助 ────────────────────────────────────────────────────────────

    private static string BuildCanonical(
        string measurement,
        IReadOnlyDictionary<string, string> tags)
    {
        if (tags.Count == 0)
            return measurement;

        var sortedKeys = new string[tags.Count];
        var i = 0;
        foreach (var key in tags.Keys)
            sortedKeys[i++] = key;

        Array.Sort(sortedKeys, StringComparer.Ordinal);

        // 预估容量：measurement + 每个 tag 的 "," + key + "=" + value
        int capacity = measurement.Length;
        foreach (var key in sortedKeys)
            capacity += 1 + key.Length + 1 + tags[key].Length; // ',' + key + '=' + value

        var sb = new StringBuilder(capacity);
        sb.Append(measurement);

        foreach (var key in sortedKeys)
        {
            sb.Append(',');
            sb.Append(key);
            sb.Append('=');
            sb.Append(tags[key]);
        }

        return sb.ToString();
    }

    // ── 相等性 ──────────────────────────────────────────────────────────────

    /// <summary>按 <see cref="Canonical"/>（Ordinal）比较两个序列键是否相等。</summary>
    public bool Equals(SeriesKey other)
        => string.Equals(Canonical, other.Canonical, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is SeriesKey other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => string.GetHashCode(Canonical, StringComparison.Ordinal);

    /// <summary>相等运算符。</summary>
    public static bool operator ==(SeriesKey left, SeriesKey right) => left.Equals(right);

    /// <summary>不等运算符。</summary>
    public static bool operator !=(SeriesKey left, SeriesKey right) => !left.Equals(right);

    // ── 输出 ─────────────────────────────────────────────────────────────────

    /// <summary>返回规范化字符串（即 <see cref="Canonical"/>）。</summary>
    public override string ToString() => Canonical;
}
