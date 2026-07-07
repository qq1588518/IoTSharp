using SonnetDB.Catalog;
using SonnetDB.Engine;

namespace SonnetDB.Ingest;

/// <summary>
/// 把 <see cref="BulkValuesReader"/> 与 <see cref="Tsdb.Measurements"/> schema 桥接起来的工厂。
/// 列角色（Tag/Field/Time）通过查 measurement 的列定义解析；
/// 调用方无需重复实现该桥接逻辑。
/// </summary>
public static class SchemaBoundBulkValuesReader
{
    /// <summary>
    /// 构造一个 schema 感知的 <see cref="BulkValuesReader"/>。
    /// </summary>
    /// <param name="tsdb">目标数据库。</param>
    /// <param name="sql">完整 <c>INSERT INTO ... VALUES (...)</c> 文本。</param>
    /// <param name="measurementOverride">可选的 measurement 覆盖名；非 <c>null</c> 时忽略 SQL 中的 measurement。</param>
    /// <returns>已完成 header 解析、ready-to-stream 的 reader。</returns>
    /// <exception cref="BulkIngestException">SQL 头部无效时抛出。</exception>
    public static BulkValuesReader Create(Tsdb tsdb, string sql, string? measurementOverride = null)
    {
        ArgumentNullException.ThrowIfNull(tsdb);
        ArgumentNullException.ThrowIfNull(sql);

        // resolver 会在 BulkValuesReader ctor 内被立即调用，
        // 此时 reader.Measurement 尚未对外暴露 → 通过 PeekMeasurementName 提前轻量解析。
        MeasurementSchema? cachedSchema = null;
        var resolver = (string col) =>
        {
            if (string.Equals(col, "time", StringComparison.OrdinalIgnoreCase))
                return BulkValuesColumnRole.Time;

            if (cachedSchema is null)
            {
                var name = measurementOverride ?? PeekMeasurementName(sql);
                cachedSchema = tsdb.Measurements.TryGet(name);
                if (cachedSchema is null)
                    return BulkValuesColumnRole.Auto;
            }

            var column = cachedSchema.TryGetColumn(col);
            if (column is null)
                return BulkValuesColumnRole.Auto;
            return column.Role == MeasurementColumnRole.Tag
                ? BulkValuesColumnRole.Tag
                : BulkValuesColumnRole.Field;
        };

        return new BulkValuesReader(sql, resolver, measurementOverride);
    }

    /// <summary>
    /// 轻量解析 <c>INSERT INTO &lt;measurement&gt;</c> 中的 measurement 名（不进入完整 SQL 解析器）。
    /// </summary>
    /// <exception cref="BulkIngestException">SQL 头部不符合 <c>INSERT INTO &lt;name&gt;</c> 格式时抛出。</exception>
    public static string PeekMeasurementName(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var span = sql.AsSpan();
        int i = 0;
        SkipWhitespace(span, ref i);
        ConsumeKeyword(span, ref i, "INSERT");
        SkipWhitespace(span, ref i);
        ConsumeKeyword(span, ref i, "INTO");
        SkipWhitespace(span, ref i);
        if (i >= span.Length) throw new BulkIngestException("Bulk INSERT: 期望 measurement 名。");
        char c = span[i];
        if (c == '"' || c == '`')
        {
            int s = ++i;
            while (i < span.Length && span[i] != c) i++;
            return new string(span[s..i]);
        }
        int start = i;
        while (i < span.Length && (char.IsLetterOrDigit(span[i]) || span[i] == '_')) i++;
        if (i == start) throw new BulkIngestException("Bulk INSERT: 无法读取 measurement 名。");
        return new string(span[start..i]);
    }

    private static void SkipWhitespace(ReadOnlySpan<char> span, ref int i)
    {
        while (i < span.Length && (span[i] == ' ' || span[i] == '\t' || span[i] == '\r' || span[i] == '\n')) i++;
    }

    private static void ConsumeKeyword(ReadOnlySpan<char> span, ref int i, string kw)
    {
        if (i + kw.Length > span.Length)
            throw new BulkIngestException($"Bulk INSERT: 期望关键字 '{kw}'。");
        for (int k = 0; k < kw.Length; k++)
        {
            if (char.ToUpperInvariant(span[i + k]) != kw[k])
                throw new BulkIngestException($"Bulk INSERT: 期望关键字 '{kw}'。");
        }
        i += kw.Length;
    }
}
