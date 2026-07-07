using SonnetDB.Storage.Format;

namespace SonnetDB.Catalog;

/// <summary>
/// Measurement schema 中的一列。Tag 列固定为 <see cref="FieldType.String"/>。
/// </summary>
/// <param name="Name">列名（区分大小写，非空、非空白）。</param>
/// <param name="Role">列角色（Tag 或 Field）。</param>
/// <param name="DataType">数据类型；Tag 列必须为 <see cref="FieldType.String"/>。</param>
/// <param name="VectorDimension">
/// 向量列的维度（仅当 <see cref="DataType"/> 为 <see cref="FieldType.Vector"/> 时非 <c>null</c>，且必须 &gt; 0）；
/// 其他类型恒为 <c>null</c>。
/// </param>
/// <param name="VectorIndex">
/// 向量列的可选索引定义；仅当 <see cref="DataType"/> 为 <see cref="FieldType.Vector"/> 时允许非 <c>null</c>。
/// </param>
public sealed record MeasurementColumn(
    string Name,
    MeasurementColumnRole Role,
    FieldType DataType,
    int? VectorDimension = null,
    VectorIndexDefinition? VectorIndex = null);
