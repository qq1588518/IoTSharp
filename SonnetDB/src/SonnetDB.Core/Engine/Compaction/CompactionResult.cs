namespace SonnetDB.Engine.Compaction;

/// <summary>
/// 一次 Compaction 的执行结果统计。
/// </summary>
/// <param name="NewSegmentId">合并产出的新段 ID。</param>
/// <param name="NewSegmentPath">合并产出的新段文件路径。</param>
/// <param name="RemovedSegmentIds">被合并消除的旧段 ID 列表。</param>
/// <param name="InputBlockCount">输入 Block 总数（所有源段 Block 之和）。</param>
/// <param name="OutputBlockCount">输出 Block 数量（合并后去重 (SeriesId, FieldName) 桶数）。</param>
/// <param name="OutputBytes">输出段文件字节数。</param>
/// <param name="DurationMicros">本次 Compaction 耗时（微秒）。</param>
public sealed record CompactionResult(
    long NewSegmentId,
    string NewSegmentPath,
    IReadOnlyList<long> RemovedSegmentIds,
    int InputBlockCount,
    int OutputBlockCount,
    long OutputBytes,
    long DurationMicros);
