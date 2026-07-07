namespace SonnetDB.Engine;

/// <summary>
/// 后台 Flush 线程的运行参数。
/// </summary>
public sealed record BackgroundFlushOptions
{
    /// <summary>是否启用后台 Flush 线程。默认 true。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>主动轮询周期（即使没收到信号也扫描一次 ShouldFlush）。默认 1s。</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>关闭时等待后台线程退出的超时。默认 30s。</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>默认配置实例。</summary>
    public static BackgroundFlushOptions Default { get; } = new();
}
