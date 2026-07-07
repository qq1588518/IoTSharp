namespace SonnetDB.Wal;

/// <summary>
/// WAL segment 滚动策略配置。
/// </summary>
public sealed record WalRollingPolicy
{
    /// <summary>
    /// 是否启用多 segment 滚动。默认 <c>true</c>；设为 <c>false</c> 时退化为单文件模式（兼容旧行为）。
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 单 WAL segment 字节上限；超过即自动 Roll。默认 64 MB。
    /// </summary>
    public long MaxBytesPerSegment { get; init; } = 64L * 1024 * 1024;

    /// <summary>
    /// 单 WAL segment 最大记录数；超过即自动 Roll。默认 1,000,000。
    /// </summary>
    public long MaxRecordsPerSegment { get; init; } = 1_000_000;

    /// <summary>默认策略实例（启用，64MB / 百万条双阈值）。</summary>
    public static WalRollingPolicy Default { get; } = new();
}
