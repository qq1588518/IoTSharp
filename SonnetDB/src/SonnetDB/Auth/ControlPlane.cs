using SonnetDB.Auth;
using SonnetDB.Hosting;
using SonnetDB.Sql.Ast;
using SonnetDB.Sql.Execution;

namespace SonnetDB.Auth;

/// <summary>
/// 服务端 <see cref="IControlPlane"/> 实现：把控制面 DDL 翻译为
/// <see cref="UserStore"/> / <see cref="GrantsStore"/> / <see cref="TsdbRegistry"/> 操作。
/// </summary>
/// <remarks>
/// 所有方法是线程安全的，依赖底层 store 的内部锁；
/// 不在执行器层做权限校验，调用者（鉴权中间件）需保证只有超级用户才能触发。
/// </remarks>
public sealed class ControlPlane : IControlPlane
{
    private readonly UserStore _users;
    private readonly GrantsStore _grants;
    private readonly TsdbRegistry _registry;

    /// <summary>构造服务端控制面。</summary>
    public ControlPlane(UserStore users, GrantsStore grants, TsdbRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(registry);
        _users = users;
        _grants = grants;
        _registry = registry;
    }

    /// <inheritdoc />
    public void CreateUser(string userName, string password, bool isSuperuser)
        => _users.CreateUser(userName, password, isSuperuser);

    /// <inheritdoc />
    public void AlterUserPassword(string userName, string newPassword)
        => _users.ChangePassword(userName, newPassword);

    /// <inheritdoc />
    public void DropUser(string userName)
    {
        if (!_users.DeleteUser(userName))
            throw new InvalidOperationException($"用户 '{userName}' 不存在。");
        _grants.DeleteUserGrants(userName);
    }

    /// <inheritdoc />
    public void Grant(string userName, string database, GrantPermission permission)
    {
        EnsureUserExists(userName);
        EnsureDatabaseExistsOrWildcard(database);
        _grants.Grant(userName, database, MapPermission(permission));
    }

    /// <inheritdoc />
    public void Revoke(string userName, string database)
    {
        EnsureUserExists(userName);
        _grants.Revoke(userName, database);
    }

    /// <inheritdoc />
    public void CreateDatabase(string databaseName)
    {
        if (!_registry.TryCreate(databaseName, out _))
            throw new InvalidOperationException($"数据库 '{databaseName}' 已存在。");
    }

    /// <inheritdoc />
    public void DropDatabase(string databaseName)
    {
        if (!_registry.Drop(databaseName))
            throw new InvalidOperationException($"数据库 '{databaseName}' 不存在。");
        _grants.DeleteDatabaseGrants(databaseName);
    }

    /// <inheritdoc />
    public IReadOnlyList<UserSummary> ListUsers()
    {
        var detailed = _users.ListUsersDetailed();
        var result = new UserSummary[detailed.Count];
        for (int i = 0; i < detailed.Count; i++)
        {
            var u = detailed[i];
            result[i] = new UserSummary(u.Name, u.IsSuperuser, u.CreatedUtc, u.TokenCount);
        }
        return result;
    }

    /// <inheritdoc />
    public IReadOnlyList<GrantSummary> ListGrants(string? userName)
    {
        var raw = userName is null ? _grants.ListAll() : _grants.ListByUser(userName);
        var result = new GrantSummary[raw.Count];
        for (int i = 0; i < raw.Count; i++)
        {
            var g = raw[i];
            result[i] = new GrantSummary(g.User, g.Database, MapPermissionBack(g.Permission));
        }
        return result;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListDatabases() => _registry.ListDatabases();

    /// <inheritdoc />
    public IReadOnlyList<TokenSummary> ListTokens(string? userName)
    {
        var raw = _users.ListTokensDetailed(userName);
        var result = new TokenSummary[raw.Count];
        for (int i = 0; i < raw.Count; i++)
        {
            var t = raw[i];
            result[i] = new TokenSummary(t.TokenId, t.UserName, t.CreatedUtc, t.LastUsedUtc);
        }
        return result;
    }

    /// <inheritdoc />
    public (string TokenId, string TokenPlain) IssueToken(string userName)
    {
        EnsureUserExists(userName);
        var (token, id) = _users.IssueToken(userName);
        return (id, token);
    }

    /// <inheritdoc />
    public void RevokeToken(string tokenId)
    {
        if (!_users.RevokeTokenById(tokenId))
            throw new InvalidOperationException($"token '{tokenId}' 不存在。");
    }

    private void EnsureUserExists(string userName)
    {
        if (!_users.Exists(userName))
            throw new InvalidOperationException($"用户 '{userName}' 不存在。");
    }

    private void EnsureDatabaseExistsOrWildcard(string database)
    {
        if (database == "*") return;
        if (!_registry.TryGet(database, out _))
            throw new InvalidOperationException($"数据库 '{database}' 不存在。");
    }

    private static DatabasePermission MapPermission(GrantPermission permission) => permission switch
    {
        GrantPermission.Read => DatabasePermission.Read,
        GrantPermission.Write => DatabasePermission.Write,
        GrantPermission.Admin => DatabasePermission.Admin,
        _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, "未知的 GRANT 权限级别。"),
    };

    private static GrantPermission MapPermissionBack(DatabasePermission permission) => permission switch
    {
        DatabasePermission.Read => GrantPermission.Read,
        DatabasePermission.Write => GrantPermission.Write,
        DatabasePermission.Admin => GrantPermission.Admin,
        _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, "无法映射的服务端权限级别。"),
    };
}
