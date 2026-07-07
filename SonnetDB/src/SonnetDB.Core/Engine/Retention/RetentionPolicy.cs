namespace SonnetDB.Engine.Retention;

/// <summary>
/// 数据保留策略。
/// <list type="bullet">
///   <item><description>全局 TTL：所有 series 共享同一保留时长；</description></item>
///   <item><description>per-measurement / per-series 粒度的策略 v1 不实现（PR #28+ 接 SQL 后再加）。</description></item>
/// </list>
/// </summary>
public sealed record RetentionPolicy
{
    /// <summary>是否启用 Retention。默认 <c>false</c>（保持向后兼容）。</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>全局 TTL；timestamp &lt; (NowFn() - Ttl) 的点视为过期。默认 30 天。</summary>
    public TimeSpan Ttl { get; init; } = TimeSpan.FromDays(30);

    /// <summary>后台 Retention worker 的轮询周期。默认 10 分钟。</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 单次 Retention 扫描最多注入的墓碑数（保护 manifest 体量）。默认 1024。
    /// 超过则本轮处理一部分，下轮继续。
    /// </summary>
    public int MaxTombstonesPerRound { get; init; } = 1024;

    /// <summary>Dispose 等待 worker 退出的超时。默认 60s。</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// "现在"的时间戳来源（unit 与 DataPoint.Timestamp 一致；测试可注入虚拟时钟）。
    /// 默认返回 Unix 毫秒时间戳。
    /// </summary>
    public Func<long> NowFn { get; init; } = static () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// TTL 在与 NowFn 同单位下的数值（默认按 ms 换算）。
    /// 可显式覆盖以适配秒级时间戳。为 null 时自动按毫秒换算 <see cref="Ttl"/>。
    /// </summary>
    public long? TtlInTimestampUnits { get; init; }

    /// <summary>默认配置实例（Retention 禁用）。</summary>
    public static RetentionPolicy Default { get; } = new();
}
