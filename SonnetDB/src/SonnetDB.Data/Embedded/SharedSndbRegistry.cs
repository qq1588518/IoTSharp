using System.Collections.Concurrent;
using SonnetDB.Engine;

namespace SonnetDB.Data.Embedded;

/// <summary>
/// 按规范化路径共享 <see cref="Tsdb"/> 实例的引用计数注册表。
/// </summary>
/// <remarks>
/// <para>
/// 同一进程内的多个 <see cref="SndbConnection"/> 打开同一根目录时会复用同一 <see cref="Tsdb"/> 实例，
/// 避免 WAL 文件锁冲突；最后一个连接关闭时引擎才被 <see cref="Tsdb.Dispose"/>。
/// </para>
/// <para>
/// 不同进程仍受 WAL active segment 文件句柄锁的保护，重复打开同一目录会失败。
/// </para>
/// </remarks>
internal static class SharedSndbRegistry
{
    private static readonly ConcurrentDictionary<string, Entry> _entries
        = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object _sync = new();

    /// <summary>
    /// 获取或打开指定根目录上的共享 <see cref="Tsdb"/>，并把引用计数加 1。
    /// </summary>
    public static Tsdb Acquire(TsdbOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var key = NormalizeKey(options.RootDirectory);

        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                entry.RefCount++;
                return entry.Tsdb;
            }

            var tsdb = Tsdb.Open(options);
            _entries[key] = new Entry(tsdb);
            return tsdb;
        }
    }

    /// <summary>
    /// 释放对指定 <see cref="Tsdb"/> 的一次引用；归零时关闭底层引擎。
    /// </summary>
    public static void Release(Tsdb tsdb)
    {
        ArgumentNullException.ThrowIfNull(tsdb);
        var key = NormalizeKey(tsdb.RootDirectory);

        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out var entry))
                return;

            entry.RefCount--;
            if (entry.RefCount > 0)
                return;

            _entries.TryRemove(key, out _);
            entry.Tsdb.Dispose();
        }
    }

    private static string NormalizeKey(string root)
        => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed class Entry
    {
        public Entry(Tsdb tsdb) { Tsdb = tsdb; RefCount = 1; }
        public Tsdb Tsdb { get; }
        public int RefCount { get; set; }
    }
}
