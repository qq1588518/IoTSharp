namespace SonnetDB.Engine.Compaction;

/// <summary>
/// Compaction 触发策略（Size-Tiered v1）。
/// </summary>
public sealed record CompactionPolicy
{
    /// <summary>是否启用 Compaction。默认 true。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>同一 tier 触发合并所需的最小段数。默认 4。</summary>
    public int MinTierSize { get; init; } = 4;

    /// <summary>tier 划分的字节阈值倍率（相邻 tier 大小比，例如 4 表示每升一级 ~4×）。默认 4。</summary>
    public int TierSizeRatio { get; init; } = 4;

    /// <summary>第一级 tier 的字节上限。默认 4 MB。</summary>
    public long FirstTierMaxBytes { get; init; } = 4 * 1024 * 1024;

    /// <summary>后台轮询周期。默认 5s。</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Dispose 时等待 Compaction 退出的超时。默认 60s。</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>默认配置实例。</summary>
    public static CompactionPolicy Default { get; } = new();
}
