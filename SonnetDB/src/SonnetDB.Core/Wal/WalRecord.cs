using SonnetDB.Model;

namespace SonnetDB.Wal;

/// <summary>
/// WAL 记录的抽象基类，所有具体记录类型均继承自此类。
/// </summary>
/// <param name="Lsn">日志序列号（单调递增）。</param>
/// <param name="TimestampUtcTicks">记录写入时刻（UTC Ticks）。</param>
public abstract record WalRecord(long Lsn, long TimestampUtcTicks);

/// <summary>
/// 数据写入记录，包含一个时序数据点的所有信息。
/// </summary>
/// <param name="Lsn">日志序列号。</param>
/// <param name="TimestampUtcTicks">记录写入时刻（UTC Ticks）。</param>
/// <param name="SeriesId">序列唯一标识（XxHash64 值）。</param>
/// <param name="PointTimestamp">数据点时间戳（Unix 毫秒）。</param>
/// <param name="FieldName">字段名称。</param>
/// <param name="Value">字段值。</param>
public sealed record WritePointRecord(
    long Lsn,
    long TimestampUtcTicks,
    ulong SeriesId,
    long PointTimestamp,
    string FieldName,
    FieldValue Value)
    : WalRecord(Lsn, TimestampUtcTicks);

/// <summary>
/// 序列创建记录，包含序列的 measurement 和 tag 信息。
/// </summary>
/// <param name="Lsn">日志序列号。</param>
/// <param name="TimestampUtcTicks">记录写入时刻（UTC Ticks）。</param>
/// <param name="SeriesId">序列唯一标识。</param>
/// <param name="Measurement">Measurement 名称。</param>
/// <param name="Tags">Tag 键值对。</param>
public sealed record CreateSeriesRecord(
    long Lsn,
    long TimestampUtcTicks,
    ulong SeriesId,
    string Measurement,
    IReadOnlyDictionary<string, string> Tags)
    : WalRecord(Lsn, TimestampUtcTicks);

/// <summary>
/// 检查点记录，标记截至指定 LSN 的数据已落盘到 segment。
/// </summary>
/// <param name="Lsn">日志序列号。</param>
/// <param name="TimestampUtcTicks">记录写入时刻（UTC Ticks）。</param>
/// <param name="CheckpointLsn">检查点 LSN（截止该 LSN 的数据已落盘）。</param>
public sealed record CheckpointRecord(
    long Lsn,
    long TimestampUtcTicks,
    long CheckpointLsn)
    : WalRecord(Lsn, TimestampUtcTicks);

/// <summary>
/// WAL 截断记录，标记该位置之前的所有记录已全部落盘，可以安全截断。
/// </summary>
/// <param name="Lsn">日志序列号。</param>
/// <param name="TimestampUtcTicks">记录写入时刻（UTC Ticks）。</param>
public sealed record TruncateRecord(long Lsn, long TimestampUtcTicks)
    : WalRecord(Lsn, TimestampUtcTicks);

/// <summary>
/// 删除记录，声明 (SeriesId, FieldName) 在 [FromTimestamp, ToTimestamp] 闭区间内的所有数据点已被墓碑标记。
/// </summary>
/// <param name="Lsn">日志序列号。</param>
/// <param name="TimestampUtcTicks">记录写入时刻（UTC Ticks）。</param>
/// <param name="SeriesId">序列唯一标识。</param>
/// <param name="FieldName">字段名称。</param>
/// <param name="FromTimestamp">删除时间窗起始时间戳（Unix 毫秒，闭区间）。</param>
/// <param name="ToTimestamp">删除时间窗结束时间戳（Unix 毫秒，闭区间）。</param>
public sealed record DeleteRecord(
    long Lsn,
    long TimestampUtcTicks,
    ulong SeriesId,
    string FieldName,
    long FromTimestamp,
    long ToTimestamp)
    : WalRecord(Lsn, TimestampUtcTicks);
