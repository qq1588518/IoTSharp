namespace SonnetDB.Model;

/// <summary>
/// 时序数据库写入的最小逻辑单位：一个 measurement 在某个时间戳上、
/// 指定一组 tag 取值时，所携带的若干 field 值。
/// </summary>
public sealed class Point
{
    /// <summary>Measurement 名称（非空，长度 ≤ 255，不含保留字符）。</summary>
    public required string Measurement { get; init; }

    /// <summary>Tag 键值对集合（key/value 均非空、非空白，不含保留字符）。</summary>
    public required IReadOnlyDictionary<string, string> Tags { get; init; }

    /// <summary>Field 键值对集合（至少包含一个字段）。</summary>
    public required IReadOnlyDictionary<string, FieldValue> Fields { get; init; }

    /// <summary>Unix epoch 毫秒时间戳（≥ 0）。</summary>
    public required long Timestamp { get; init; }

    // ── 工厂方法 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 创建并校验一个 <see cref="Point"/> 实例。
    /// </summary>
    /// <param name="measurement">Measurement 名称。</param>
    /// <param name="timestampUnixMs">Unix epoch 毫秒时间戳（≥ 0）。</param>
    /// <param name="tags">Tag 键值对；为 null 时使用空字典。</param>
    /// <param name="fields">Field 键值对；为 null 时使用空字典（但仍需至少 1 个 field）。</param>
    /// <returns>已校验的 <see cref="Point"/> 实例。</returns>
    /// <exception cref="ArgumentException">任何校验规则不满足时抛出。</exception>
    public static Point Create(
        string measurement,
        long timestampUnixMs,
        IReadOnlyDictionary<string, string>? tags = null,
        IReadOnlyDictionary<string, FieldValue>? fields = null)
    {
        PointValidation.ValidateMeasurement(measurement);

        if (timestampUnixMs < 0)
            throw new ArgumentException(
                $"Timestamp must be >= 0. Value: {timestampUnixMs}", nameof(timestampUnixMs));

        var resolvedTags = tags ?? EmptyDictionary<string, string>.Instance;
        var resolvedFields = fields ?? EmptyDictionary<string, FieldValue>.Instance;

        foreach (var (key, value) in resolvedTags)
        {
            PointValidation.ValidateName(key, "tag key");
            PointValidation.ValidateName(value, "tag value");
        }

        foreach (var key in resolvedFields.Keys)
        {
            PointValidation.ValidateName(key, "field key");
        }

        if (resolvedFields.Count < 1)
            throw new ArgumentException("A Point must have at least one field.", nameof(fields));

        return new Point
        {
            Measurement = measurement,
            Tags = resolvedTags,
            Fields = resolvedFields,
            Timestamp = timestampUnixMs,
        };
    }

    // ── 输出 ────────────────────────────────────────────────────────────────

    /// <summary>返回行协议风格的 debug 字符串。</summary>
    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(Measurement);

        if (Tags.Count > 0)
        {
            foreach (var (k, v) in Tags)
            {
                sb.Append(',');
                sb.Append(k);
                sb.Append('=');
                sb.Append(v);
            }
        }

        sb.Append(' ');
        bool first = true;
        foreach (var (k, v) in Fields)
        {
            if (!first) sb.Append(',');
            sb.Append(k);
            sb.Append('=');
            sb.Append(v);
            first = false;
        }

        sb.Append(' ');
        sb.Append(Timestamp);

        return sb.ToString();
    }
}
