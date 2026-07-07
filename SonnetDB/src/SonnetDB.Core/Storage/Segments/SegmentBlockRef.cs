using SonnetDB.Storage.Format;

namespace SonnetDB.Storage.Segments;

/// <summary>
/// 跨段统一引用一个 Block：包含所属段标识 + 段内 <see cref="BlockDescriptor"/>。
/// </summary>
public readonly struct SegmentBlockRef
{
    /// <summary>所属段的唯一标识符（单调递增）。</summary>
    public long SegmentId { get; }

    /// <summary>所属段的文件路径。</summary>
    public string SegmentPath { get; }

    /// <summary>段内 Block 的元数据描述符。</summary>
    public BlockDescriptor Descriptor { get; }

    /// <summary>所属序列 ID（来自 <see cref="Descriptor"/>）。</summary>
    public ulong SeriesId => Descriptor.SeriesId;

    /// <summary>字段名称（来自 <see cref="Descriptor"/>）。</summary>
    public string FieldName => Descriptor.FieldName;

    /// <summary>Block 内最小时间戳（毫秒 UTC）（来自 <see cref="Descriptor"/>）。</summary>
    public long MinTimestamp => Descriptor.MinTimestamp;

    /// <summary>Block 内最大时间戳（毫秒 UTC）（来自 <see cref="Descriptor"/>）。</summary>
    public long MaxTimestamp => Descriptor.MaxTimestamp;

    /// <summary>字段数据类型（来自 <see cref="Descriptor"/>）。</summary>
    public FieldType FieldType => Descriptor.FieldType;

    /// <summary>本 Block 包含的数据点数量（来自 <see cref="Descriptor"/>）。</summary>
    public int Count => Descriptor.Count;

    /// <summary>
    /// 初始化 <see cref="SegmentBlockRef"/> 实例。
    /// </summary>
    /// <param name="segmentId">所属段唯一标识符。</param>
    /// <param name="segmentPath">所属段文件路径。</param>
    /// <param name="descriptor">段内 Block 元数据描述符。</param>
    internal SegmentBlockRef(long segmentId, string segmentPath, BlockDescriptor descriptor)
    {
        SegmentId = segmentId;
        SegmentPath = segmentPath;
        Descriptor = descriptor;
    }
}
