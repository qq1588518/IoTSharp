namespace SonnetDB.Storage.Segments;

/// <summary>
/// <see cref="SegmentWriter"/> 构建完成后返回的结果描述。
/// </summary>
/// <param name="Path">最终写入的目标文件路径。</param>
/// <param name="SegmentId">段唯一标识符。</param>
/// <param name="BlockCount">写入的 Block 数量。</param>
/// <param name="TotalBytes">文件总字节数。</param>
/// <param name="MinTimestamp">所有 Block 中最小时间戳（毫秒 UTC）；若无 Block 则为 <see cref="long.MaxValue"/>。</param>
/// <param name="MaxTimestamp">所有 Block 中最大时间戳（毫秒 UTC）；若无 Block 则为 <see cref="long.MinValue"/>。</param>
/// <param name="IndexOffset">BlockIndexEntry 数组在文件中的起始偏移。</param>
/// <param name="FooterOffset">SegmentFooter 在文件中的起始偏移。</param>
/// <param name="DurationMicros">构建耗时（微秒）。</param>
public sealed record SegmentBuildResult(
    string Path,
    long SegmentId,
    int BlockCount,
    long TotalBytes,
    long MinTimestamp,
    long MaxTimestamp,
    long IndexOffset,
    long FooterOffset,
    long DurationMicros);
