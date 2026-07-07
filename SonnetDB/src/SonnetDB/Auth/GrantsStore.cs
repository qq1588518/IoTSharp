namespace SonnetDB.Auth;

/// <summary>
/// 用户对数据库的访问授权存储。基于 <c>grants.json</c> + 进程内 <see cref="System.Threading.Lock"/>。
/// </summary>
public sealed class GrantsStore
{
    private readonly string _filePath;
    private readonly Lock _lock = new();
    private GrantsFile _state;

    /// <summary>
    /// 在指定系统目录下打开（或初始化）授权存储。文件位于 <c>{systemDirectory}/grants.json</c>。
    /// </summary>
    public GrantsStore(string systemDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(systemDirectory);
        Directory.CreateDirectory(systemDirectory);
        _filePath = Path.Combine(systemDirectory, "grants.json");
        _state = AtomicJsonFile.Read(_filePath, AuthJsonContext.Default.GrantsFile, () => new GrantsFile());
    }

    /// <summary>当前授权总条数。</summary>
    public int Count
    {
        get { lock (_lock) return _state.Grants.Count; }
    }

    /// <summary>
    /// 查询用户对数据库的有效权限（含 <c>"*"</c> 通配，取最高级别）。
    /// </summary>
    public DatabasePermission GetPermission(string user, string database)
    {
        ArgumentException.ThrowIfNullOrEmpty(user);
        ArgumentException.ThrowIfNullOrEmpty(database);
        var u = user.ToLowerInvariant();
        var d = database.ToLowerInvariant();
        var max = DatabasePermission.None;
        lock (_lock)
        {
            foreach (var g in _state.Grants)
            {
                if (g.User != u) continue;
                if (g.Database != d && g.Database != "*") continue;
                if (g.Permission > max) max = g.Permission;
            }
        }
        return max;
    }

    /// <summary>
    /// 列出指定用户对所有数据库的授权。
    /// </summary>
    public IReadOnlyList<GrantRecord> ListByUser(string user)
    {
        ArgumentException.ThrowIfNullOrEmpty(user);
        var u = user.ToLowerInvariant();
        lock (_lock)
        {
            return _state.Grants
                .Where(g => g.User == u)
                .Select(g => new GrantRecord { User = g.User, Database = g.Database, Permission = g.Permission })
                .ToArray();
        }
    }

    /// <summary>列出全部 grants（按 user, database 排序）。</summary>
    public IReadOnlyList<GrantRecord> ListAll()
    {
        lock (_lock)
        {
            return _state.Grants
                .OrderBy(g => g.User, StringComparer.Ordinal)
                .ThenBy(g => g.Database, StringComparer.Ordinal)
                .Select(g => new GrantRecord { User = g.User, Database = g.Database, Permission = g.Permission })
                .ToArray();
        }
    }

    /// <summary>
    /// 授权（同 (user, database) 仅保留最高级别）。
    /// </summary>
    public void Grant(string user, string database, DatabasePermission permission)
    {
        ArgumentException.ThrowIfNullOrEmpty(user);
        ArgumentException.ThrowIfNullOrEmpty(database);
        if (permission == DatabasePermission.None)
            throw new ArgumentException("不能授予 None。", nameof(permission));
        var u = user.ToLowerInvariant();
        var d = database.ToLowerInvariant();

        lock (_lock)
        {
            var existing = _state.Grants.FirstOrDefault(g => g.User == u && g.Database == d);
            if (existing is not null)
            {
                if (permission > existing.Permission)
                {
                    existing.Permission = permission;
                    Persist();
                }
                return;
            }
            _state.Grants.Add(new GrantRecord { User = u, Database = d, Permission = permission });
            Persist();
        }
    }

    /// <summary>
    /// 撤销 (user, database) 的全部权限。
    /// </summary>
    /// <returns>true 表示有匹配并已删除；false 表示无匹配。</returns>
    public bool Revoke(string user, string database)
    {
        ArgumentException.ThrowIfNullOrEmpty(user);
        ArgumentException.ThrowIfNullOrEmpty(database);
        var u = user.ToLowerInvariant();
        var d = database.ToLowerInvariant();
        lock (_lock)
        {
            var removed = _state.Grants.RemoveAll(g => g.User == u && g.Database == d);
            if (removed > 0)
            {
                Persist();
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 删除用户的所有授权（用于 DROP USER 联动）。
    /// </summary>
    public void DeleteUserGrants(string user)
    {
        ArgumentException.ThrowIfNullOrEmpty(user);
        var u = user.ToLowerInvariant();
        lock (_lock)
        {
            var removed = _state.Grants.RemoveAll(g => g.User == u);
            if (removed > 0)
                Persist();
        }
    }

    /// <summary>
    /// 删除某数据库的所有授权（用于 DROP DATABASE 联动）。
    /// </summary>
    public void DeleteDatabaseGrants(string database)
    {
        ArgumentException.ThrowIfNullOrEmpty(database);
        var d = database.ToLowerInvariant();
        lock (_lock)
        {
            var removed = _state.Grants.RemoveAll(g => g.Database == d);
            if (removed > 0)
                Persist();
        }
    }

    private void Persist()
    {
        AtomicJsonFile.Write(_filePath, _state, AuthJsonContext.Default.GrantsFile);
    }
}
