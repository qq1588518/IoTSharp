namespace SonnetDB.Wal;

/// <summary>
/// WAL segment 文件描述，包含起始 LSN、文件路径、文件长度与可选 LastLsn 元数据。
/// </summary>
/// <param name="StartLsn">本 segment 首条记录的 LSN（由文件名解析）。</param>
/// <param name="Path">segment 文件的完整路径。</param>
/// <param name="FileLength">segment 文件的字节长度（打开时快照；可为 0 表示空或刚创建）。</param>
public readonly record struct WalSegmentInfo(
    long StartLsn,
    string Path,
    long FileLength)
{
    /// <summary>是否已知本 segment 最后一条合法记录的 LSN。</summary>
    public bool HasLastLsn { get; init; }

    /// <summary>本 segment 最后一条合法记录的 LSN；仅当 <see cref="HasLastLsn"/> 为 true 时有效。</summary>
    public long LastLsn { get; init; }
}
