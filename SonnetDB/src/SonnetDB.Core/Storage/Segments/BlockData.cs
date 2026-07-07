namespace SonnetDB.Storage.Segments;

/// <summary>
/// Block 的 payload 视图（<c>readonly ref struct</c>），仅在调用栈内有效。
/// <para>
/// 默认 <c>byte[]</c> reader 下三段 span 直接指向 <see cref="SegmentReader"/> 内部数组；
/// memory-mapped reader 下三段 span 指向按需读取的托管缓冲区，避免把整个 segment 放入 LOH。
/// </para>
/// </summary>
public readonly ref struct BlockData
{
    /// <summary>本 Block 的元数据与物理位置描述符。</summary>
    public BlockDescriptor Descriptor { get; init; }

    /// <summary>字段名的 UTF-8 字节视图。</summary>
    public ReadOnlySpan<byte> FieldNameUtf8 { get; init; }

    /// <summary>时间戳载荷的字节视图。</summary>
    public ReadOnlySpan<byte> TimestampPayload { get; init; }

    /// <summary>值载荷的字节视图。</summary>
    public ReadOnlySpan<byte> ValuePayload { get; init; }
}
