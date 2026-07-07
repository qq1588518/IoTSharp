namespace SonnetDB.Model;

/// <summary>
/// 引擎内部的最小数据点：单个 field 在某个时间戳上的取值。
/// 在写入路径中由 <see cref="Point"/> 拆分而来。
/// </summary>
public readonly record struct DataPoint(long Timestamp, FieldValue Value)
    : IComparable<DataPoint>
{
    /// <summary>按时间戳升序比较两个数据点。</summary>
    public int CompareTo(DataPoint other) => Timestamp.CompareTo(other.Timestamp);
}
