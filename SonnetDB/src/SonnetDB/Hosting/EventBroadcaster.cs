using System.Collections.Concurrent;
using System.Threading.Channels;
using SonnetDB.Contracts;

namespace SonnetDB.Hosting;

/// <summary>
/// 进程级事件广播器：把指标 / 慢查询 / 数据库事件多路广播给当前所有 SSE 订阅者。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>每个订阅者持有一个有界 <see cref="Channel{T}"/>，慢消费者会被丢弃事件
///       （drop-oldest，避免拖慢生产者）。</item>
///   <item>事件类型用字符串标识（<c>metrics</c> / <c>slow_query</c> / <c>db</c>），
///       订阅者可在 <see cref="Subscribe"/> 时按集合过滤。</item>
///   <item>线程安全：订阅者集合用 <see cref="ConcurrentDictionary{TKey, TValue}"/>，
///       发布与订阅完全无锁。</item>
/// </list>
/// </remarks>
public sealed class EventBroadcaster : IDisposable
{
    private readonly ConcurrentDictionary<long, Subscription> _subscribers = new();
    private long _nextId;
    private bool _disposed;

    /// <summary>当前订阅者数量。</summary>
    public int SubscriberCount => _subscribers.Count;

    /// <summary>
    /// 订阅事件。返回的 <see cref="Subscription"/> 在 <see cref="IDisposable.Dispose"/> 时
    /// 自动从广播器中移除。
    /// </summary>
    /// <param name="channels">关心的事件类型集合；传 <c>null</c> 或空集表示订阅所有。</param>
    /// <param name="capacity">订阅者本地缓冲区容量。默认 64，溢出时丢弃最早的事件。</param>
    public Subscription Subscribe(IReadOnlySet<string>? channels = null, int capacity = 64)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var id = Interlocked.Increment(ref _nextId);
        var channel = Channel.CreateBounded<ServerEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        var sub = new Subscription(this, id, channel, channels);
        _subscribers[id] = sub;
        return sub;
    }

    /// <summary>
    /// 广播一个事件。同步调用，O(N) 写入每个订阅者的本地通道。
    /// </summary>
    public void Publish(ServerEvent evt)
    {
        if (_disposed) return;
        foreach (var sub in _subscribers.Values)
        {
            if (!sub.Accepts(evt.Type))
                continue;
            // bounded + DropOldest，永远不会阻塞。
            sub.TryWrite(evt);
        }
    }

    /// <summary>
    /// 释放所有订阅者通道。后续 <see cref="Publish"/> 不再生效。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var sub in _subscribers.Values)
        {
            sub.Complete();
        }
        _subscribers.Clear();
    }

    internal void Unregister(long id)
    {
        _subscribers.TryRemove(id, out _);
    }

    /// <summary>
    /// 订阅句柄。<see cref="ChannelReader"/> 暴露给 SSE endpoint 直接 await。
    /// </summary>
    public sealed class Subscription : IDisposable
    {
        private readonly EventBroadcaster _owner;
        private readonly long _id;
        private readonly Channel<ServerEvent> _channel;
        private readonly IReadOnlySet<string>? _filter;
        private bool _disposed;

        internal Subscription(EventBroadcaster owner, long id, Channel<ServerEvent> channel, IReadOnlySet<string>? filter)
        {
            _owner = owner;
            _id = id;
            _channel = channel;
            _filter = filter;
        }

        /// <summary>本订阅的事件通道读端。</summary>
        public ChannelReader<ServerEvent> Reader => _channel.Reader;

        internal bool Accepts(string type)
            => _filter is null || _filter.Count == 0 || _filter.Contains(type);

        internal void TryWrite(ServerEvent evt)
        {
            // 失败（disposed / completed）忽略
            _ = _channel.Writer.TryWrite(evt);
        }

        internal void Complete()
        {
            _channel.Writer.TryComplete();
        }

        /// <summary>
        /// 解除订阅并完成内部通道；SSE 写循环会随后退出。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _channel.Writer.TryComplete();
            _owner.Unregister(_id);
        }
    }
}
