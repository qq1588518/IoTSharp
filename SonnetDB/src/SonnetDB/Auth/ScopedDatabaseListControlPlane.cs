using SonnetDB.Exceptions;
using SonnetDB.Sql.Ast;
using SonnetDB.Sql.Execution;

namespace SonnetDB.Auth;

/// <summary>
/// 按当前请求视角包装 <see cref="IControlPlane"/>，用于过滤数据库、授权与 token 的可见性，
/// 并把普通动态用户的控制面能力限制为“只操作自己”。
/// </summary>
internal sealed class ScopedDatabaseListControlPlane(
    IControlPlane inner,
    UserStore userStore,
    Func<IReadOnlyList<string>> databaseProvider,
    AuthenticatedUser? currentUser) : IControlPlane
{
    private readonly IControlPlane _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly UserStore _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));
    private readonly Func<IReadOnlyList<string>> _databaseProvider = databaseProvider ?? throw new ArgumentNullException(nameof(databaseProvider));
    private readonly AuthenticatedUser? _currentUser = currentUser;

    public void CreateUser(string userName, string password, bool isSuperuser)
    {
        EnsureAdminOnlyAllowed();
        _inner.CreateUser(userName, password, isSuperuser);
    }

    public void AlterUserPassword(string userName, string newPassword)
    {
        EnsureAdminOnlyAllowed();
        _inner.AlterUserPassword(userName, newPassword);
    }

    public void DropUser(string userName)
    {
        EnsureAdminOnlyAllowed();
        _inner.DropUser(userName);
    }

    public void Grant(string userName, string database, GrantPermission permission)
    {
        EnsureAdminOnlyAllowed();
        _inner.Grant(userName, database, permission);
    }

    public void Revoke(string userName, string database)
    {
        EnsureAdminOnlyAllowed();
        _inner.Revoke(userName, database);
    }

    public void CreateDatabase(string databaseName)
    {
        EnsureAdminOnlyAllowed();
        _inner.CreateDatabase(databaseName);
    }

    public void DropDatabase(string databaseName)
    {
        EnsureAdminOnlyAllowed();
        _inner.DropDatabase(databaseName);
    }

    public IReadOnlyList<UserSummary> ListUsers()
    {
        EnsureAdminOnlyAllowed();
        return _inner.ListUsers();
    }

    public IReadOnlyList<GrantSummary> ListGrants(string? userName)
    {
        if (!ShouldRestrictToSelf())
            return _inner.ListGrants(userName);

        return _inner.ListGrants(ResolveSelfUser(userName, "仅可查看当前用户自己的授权。"));
    }

    public IReadOnlyList<string> ListDatabases()
        => _databaseProvider();

    public IReadOnlyList<TokenSummary> ListTokens(string? userName)
    {
        if (!ShouldRestrictToSelf())
            return _inner.ListTokens(userName);

        return _inner.ListTokens(ResolveSelfUser(userName, "仅可查看当前用户自己的 token。"));
    }

    public (string TokenId, string TokenPlain) IssueToken(string userName)
    {
        if (!ShouldRestrictToSelf())
            return _inner.IssueToken(userName);

        return _inner.IssueToken(ResolveSelfUser(userName, "仅可为当前用户自己签发 token。"));
    }

    public void RevokeToken(string tokenId)
    {
        if (!ShouldRestrictToSelf())
        {
            _inner.RevokeToken(tokenId);
            return;
        }

        ArgumentException.ThrowIfNullOrEmpty(tokenId);
        if (!OwnsToken(tokenId))
            throw new ControlPlaneAccessDeniedException("仅可吊销当前用户自己的 token。");

        _inner.RevokeToken(tokenId);
    }

    private void EnsureAdminOnlyAllowed()
    {
        if (ShouldRestrictToSelf())
            throw new ControlPlaneAccessDeniedException("该控制面操作仅 admin 可执行。");
    }

    private bool ShouldRestrictToSelf()
        => _currentUser is AuthenticatedUser { IsSuperuser: false };

    private string ResolveSelfUser(string? userName, string deniedMessage)
    {
        var currentUserName = GetCurrentUserName();
        if (string.IsNullOrEmpty(userName))
            return currentUserName;
        if (!string.Equals(userName, currentUserName, StringComparison.OrdinalIgnoreCase))
            throw new ControlPlaneAccessDeniedException(deniedMessage);
        return currentUserName;
    }

    private bool OwnsToken(string tokenId)
    {
        var currentUserName = GetCurrentUserName();
        var tokens = _userStore.ListTokensDetailed(currentUserName);
        for (int i = 0; i < tokens.Count; i++)
        {
            if (string.Equals(tokens[i].TokenId, tokenId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private string GetCurrentUserName()
    {
        if (_currentUser is AuthenticatedUser currentUser)
            return currentUser.UserName;

        throw new InvalidOperationException("当前请求不包含动态用户上下文。");
    }
}
