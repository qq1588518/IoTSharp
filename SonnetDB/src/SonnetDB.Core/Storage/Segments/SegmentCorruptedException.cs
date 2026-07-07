namespace SonnetDB.Storage.Segments;

/// <summary>
/// 段文件损坏或格式不一致时抛出的异常。
/// </summary>
public sealed class SegmentCorruptedException : IOException
{
    /// <summary>发生错误的段文件路径。</summary>
    public string SegmentPath { get; }

    /// <summary>损坏位置在文件中的字节偏移；若不确定则为 null。</summary>
    public long? Offset { get; }

    /// <summary>
    /// 创建 <see cref="SegmentCorruptedException"/> 实例。
    /// </summary>
    /// <param name="path">发生错误的段文件路径。</param>
    /// <param name="offset">损坏位置在文件中的字节偏移；不确定时传 null。</param>
    /// <param name="message">描述损坏原因的消息。</param>
    public SegmentCorruptedException(string path, long? offset, string message)
        : base($"[{path}@{offset?.ToString("X") ?? "?"}] {message}")
    {
        SegmentPath = path;
        Offset = offset;
    }
}
