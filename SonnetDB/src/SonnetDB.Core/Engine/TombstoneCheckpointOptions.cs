namespace SonnetDB.Engine;

/// <summary>
/// Tombstone manifest 周期性 checkpoint 选项。
/// </summary>
public sealed record TombstoneCheckpointOptions
{
    /// <summary>
    /// 是否启用 Delete 路径上的周期性 manifest checkpoint。默认启用。
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 自上次 manifest checkpoint 后累计多少条 Delete 触发一次持久化。默认 1024。
    /// </summary>
    public int MaxDeletesSinceCheckpoint { get; init; } = 1024;

    /// <summary>
    /// 自上次 manifest checkpoint 后经过多久，在下一条 Delete 到来时触发一次持久化。默认 30 秒。
    /// </summary>
    public TimeSpan MaxInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>默认配置实例。</summary>
    public static TombstoneCheckpointOptions Default { get; } = new();
}
