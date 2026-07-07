namespace SonnetDB.Catalog;

/// <summary>列在 measurement 中扮演的角色。</summary>
public enum MeasurementColumnRole : byte
{
    /// <summary>Tag 列：参与 SeriesKey 规范化与索引。</summary>
    Tag = 0,
    /// <summary>Field 列：承载时序数据值。</summary>
    Field = 1,
}
