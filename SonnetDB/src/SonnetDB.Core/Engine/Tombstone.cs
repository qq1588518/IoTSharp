namespace SonnetDB.Engine;

/// <summary>
/// 墓碑：声明 "(SeriesId, FieldName)" 在时间窗 [FromTimestamp, ToTimestamp]（闭区间）
/// 内的所有数据点已被删除。
/// <para>
/// <b>v1 时间窗永久标记语义：</b>
/// 一个墓碑 <c>T</c> 隐藏所有满足
/// <c>point.SeriesId == T.SeriesId &amp;&amp; point.FieldName == T.FieldName &amp;&amp;
/// T.FromTimestamp ≤ point.Timestamp ≤ T.ToTimestamp</c>
/// 的数据点——不论该点是在墓碑之前还是之后写入的。
/// 也就是说：<b>删除不是"截止此刻之前的数据"，而是"该时间窗已被永久标记为删除"</b>。
/// 如果用户在墓碑之后又向该时间窗写入新点，那些新点也会被该墓碑隐藏，
/// 直到墓碑被 Compaction 自然回收（v1 不提供显式撤销 API）。
/// </para>
/// <para>
/// 这是一种<b>简单一致</b>的语义，避免引入 perPoint LSN 比对；
/// 想要"恢复写入"的用户必须先等待墓碑过期或通过 Compaction 自然回收。
/// </para>
/// <para>
/// <c>CreatedLsn</c> 表示墓碑自身的 WAL LSN，用于：
/// <list type="bullet">
///   <item><description>崩溃恢复时从 WAL replay 重建 in-memory 墓碑集合（仅 LSN &gt; CheckpointLsn 的记录需要重放）。</description></item>
///   <item><description>Compaction 与墓碑消化判定。</description></item>
/// </list>
/// </para>
/// </summary>
/// <param name="SeriesId">序列唯一标识（XxHash64 值）。</param>
/// <param name="FieldName">字段名称。</param>
/// <param name="FromTimestamp">删除时间窗起始时间戳（Unix 毫秒，闭区间）。</param>
/// <param name="ToTimestamp">删除时间窗结束时间戳（Unix 毫秒，闭区间）。</param>
/// <param name="CreatedLsn">墓碑自身写入 WAL 时分配的 LSN。</param>
public readonly record struct Tombstone(
    ulong SeriesId,
    string FieldName,
    long FromTimestamp,
    long ToTimestamp,
    long CreatedLsn);
