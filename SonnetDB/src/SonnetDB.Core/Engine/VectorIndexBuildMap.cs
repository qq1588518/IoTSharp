using SonnetDB.Catalog;
using SonnetDB.Memory;
using SonnetDB.Model;

namespace SonnetDB.Engine;

/// <summary>
/// 为 Flush / Compaction 计算需要构建向量索引的字段集合。
/// </summary>
internal static class VectorIndexBuildMap
{
    /// <summary>
    /// 根据当前待写出的 MemTable 桶，解析出需要构建向量索引的字段集合。
    /// </summary>
    /// <param name="seriesList">待写出的桶列表。</param>
    /// <param name="catalog">Series 目录。</param>
    /// <param name="measurements">Measurement schema 目录。</param>
    /// <returns>按 <see cref="SeriesFieldKey"/> 索引的向量索引定义。</returns>
    public static IReadOnlyDictionary<SeriesFieldKey, VectorIndexDefinition> Build(
        IReadOnlyList<MemTableSeries> seriesList,
        SeriesCatalog catalog,
        MeasurementCatalog measurements)
    {
        ArgumentNullException.ThrowIfNull(seriesList);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(measurements);

        var result = new Dictionary<SeriesFieldKey, VectorIndexDefinition>();
        foreach (var series in seriesList)
        {
            if (series.FieldType != Storage.Format.FieldType.Vector)
                continue;

            var vectorIndex = Resolve(series.Key.SeriesId, series.Key.FieldName, catalog, measurements);
            if (vectorIndex is not null)
                result[series.Key] = vectorIndex;
        }

        return result;
    }

    /// <summary>
    /// 解析指定 (SeriesId, FieldName) 是否声明了向量索引。
    /// </summary>
    /// <param name="seriesId">目标序列 ID。</param>
    /// <param name="fieldName">目标字段名。</param>
    /// <param name="catalog">Series 目录。</param>
    /// <param name="measurements">Measurement schema 目录。</param>
    /// <returns>若该字段声明了向量索引则返回定义，否则返回 null。</returns>
    public static VectorIndexDefinition? Resolve(
        ulong seriesId,
        string fieldName,
        SeriesCatalog catalog,
        MeasurementCatalog measurements)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(measurements);

        var series = catalog.TryGet(seriesId);
        if (series is null)
            return null;

        var schema = measurements.TryGet(series.Measurement);
        var column = schema?.TryGetColumn(fieldName);
        return column?.VectorIndex;
    }
}
