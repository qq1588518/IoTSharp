namespace SonnetDB.Model;

/// <summary>
/// 引擎内对 (series, field) 的复合键：MemTable / Block / Index 都按此键组织。
/// </summary>
public readonly record struct SeriesFieldKey(ulong SeriesId, string FieldName)
{
    /// <summary>返回 <c>{SeriesId:X16}/{FieldName}</c> 格式的字符串。</summary>
    public override string ToString() => $"{SeriesId:X16}/{FieldName}";
}
