using SonnetDB.Model;

namespace SonnetDB.Storage.Segments;

internal readonly record struct BlockDecodeCacheKey(long SegmentId, int BlockIndex, uint Crc32);

internal sealed class BlockDecodeCache
{
    private readonly object _lock = new();
    private readonly long _maxBytes;
    private readonly Dictionary<BlockDecodeCacheKey, LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _lru = new();
    private long _currentBytes;
    private long _hitCount;
    private long _missCount;

    public BlockDecodeCache(long maxBytes)
    {
        _maxBytes = Math.Max(0L, maxBytes);
    }

    public long MaxBytes => _maxBytes;

    public long CurrentBytes
    {
        get
        {
            lock (_lock)
                return _currentBytes;
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
                return _entries.Count;
        }
    }

    public long HitCount
    {
        get
        {
            lock (_lock)
                return _hitCount;
        }
    }

    public long MissCount
    {
        get
        {
            lock (_lock)
                return _missCount;
        }
    }

    public bool CanStore(long estimatedBytes)
        => _maxBytes > 0 && estimatedBytes <= _maxBytes;

    public bool TryGet(BlockDecodeCacheKey key, out DataPoint[] points)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                _hitCount++;
                points = node.Value.Points;
                return true;
            }

            _missCount++;
            points = [];
            return false;
        }
    }

    public bool TryAdd(BlockDecodeCacheKey key, DataPoint[] points, long estimatedBytes)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (!CanStore(estimatedBytes))
            return false;

        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
                _entries.Remove(key);
                _currentBytes -= existing.Value.EstimatedBytes;
            }

            var entry = new Entry(key, points, estimatedBytes);
            var node = new LinkedListNode<Entry>(entry);
            _lru.AddFirst(node);
            _entries.Add(key, node);
            _currentBytes += estimatedBytes;

            EvictOverBudget();
            return _entries.ContainsKey(key);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _lru.Clear();
            _currentBytes = 0L;
        }
    }

    private void EvictOverBudget()
    {
        while (_currentBytes > _maxBytes && _lru.Last is not null)
        {
            var node = _lru.Last;
            _lru.RemoveLast();
            _entries.Remove(node.Value.Key);
            _currentBytes -= node.Value.EstimatedBytes;
        }
    }

    private sealed class Entry
    {
        public Entry(BlockDecodeCacheKey key, DataPoint[] points, long estimatedBytes)
        {
            Key = key;
            Points = points;
            EstimatedBytes = estimatedBytes;
        }

        public BlockDecodeCacheKey Key { get; }

        public DataPoint[] Points { get; }

        public long EstimatedBytes { get; }
    }
}
