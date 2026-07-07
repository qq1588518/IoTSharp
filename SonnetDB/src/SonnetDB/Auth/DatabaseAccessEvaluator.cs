using Microsoft.AspNetCore.Http;
using SonnetDB.Configuration;

namespace SonnetDB.Auth;

/// <summary>
/// 计算当前请求对指定数据库的有效权限。
/// </summary>
internal static class DatabaseAccessEvaluator
{
    /// <summary>
    /// 解析当前请求对指定数据库的有效权限。
    /// </summary>
    public static DatabasePermission GetEffectivePermission(
        HttpContext context,
        GrantsStore grantsStore,
        string database)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(grantsStore);
        ArgumentException.ThrowIfNullOrEmpty(database);

        var user = BearerAuthMiddleware.GetUser(context);
        if (user is AuthenticatedUser authenticatedUser)
        {
            if (authenticatedUser.IsSuperuser)
                return DatabasePermission.Admin;

            return grantsStore.GetPermission(authenticatedUser.UserName, database);
        }

        return BearerAuthMiddleware.GetRole(context) switch
        {
            ServerRoles.Admin => DatabasePermission.Admin,
            ServerRoles.ReadWrite => DatabasePermission.Write,
            ServerRoles.ReadOnly => DatabasePermission.Read,
            _ => DatabasePermission.None,
        };
    }

    /// <summary>
    /// 判断有效权限是否满足最低要求。
    /// </summary>
    public static bool HasPermission(DatabasePermission actual, DatabasePermission required)
        => actual >= required;

    /// <summary>
    /// 获取当前请求可见的数据库列表。
    /// </summary>
    /// <remarks>
    /// 系统内置数据库（如 <c>__copilot__</c>）只供服务端子系统内部使用，不会出现在
    /// 任何用户可见的列表里，也不会出现在 Copilot Agent 的 <c>VisibleDatabases</c> 中，
    /// 避免 LLM 误把它当作业务库去 SHOW MEASUREMENTS / 建表。
    /// </remarks>
    public static IReadOnlyList<string> GetVisibleDatabases(
        HttpContext context,
        GrantsStore grantsStore,
        IReadOnlyList<string> allDatabases)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(grantsStore);
        ArgumentNullException.ThrowIfNull(allDatabases);

        var user = BearerAuthMiddleware.GetUser(context);
        if (user is AuthenticatedUser authenticatedUser)
        {
            if (authenticatedUser.IsSuperuser)
                return FilterSystemDatabases(allDatabases);

            var visible = new List<string>(allDatabases.Count);
            foreach (var database in allDatabases)
            {
                if (IsSystemDatabase(database))
                    continue;
                if (grantsStore.GetPermission(authenticatedUser.UserName, database) >= DatabasePermission.Read)
                    visible.Add(database);
            }
            return visible;
        }

        return BearerAuthMiddleware.GetRole(context) is ServerRoles.Admin or ServerRoles.ReadWrite or ServerRoles.ReadOnly
            ? FilterSystemDatabases(allDatabases)
            : [];
    }

    /// <summary>
    /// 判断指定数据库名是否属于系统内置库。
    /// </summary>
    /// <remarks>
    /// 当前规则：名字以双下划线开头并以双下划线结尾（如 <c>__copilot__</c>）即视为系统库。
    /// </remarks>
    public static bool IsSystemDatabase(string database)
        => !string.IsNullOrEmpty(database)
            && database.Length >= 4
            && database.StartsWith("__", StringComparison.Ordinal)
            && database.EndsWith("__", StringComparison.Ordinal);

    private static IReadOnlyList<string> FilterSystemDatabases(IReadOnlyList<string> allDatabases)
    {
        // 快路径：没有任何系统库时直接返回原列表，避免无谓拷贝。
        var hasSystem = false;
        for (var i = 0; i < allDatabases.Count; i++)
        {
            if (IsSystemDatabase(allDatabases[i]))
            {
                hasSystem = true;
                break;
            }
        }
        if (!hasSystem)
            return allDatabases;

        var visible = new List<string>(allDatabases.Count);
        foreach (var database in allDatabases)
        {
            if (!IsSystemDatabase(database))
                visible.Add(database);
        }
        return visible;
    }

    /// <summary>
    /// 判断当前请求是否具备服务端管理员权限。
    /// </summary>
    public static bool IsServerAdmin(HttpContext context)
        => BearerAuthMiddleware.IsAdmin(BearerAuthMiddleware.GetRole(context));
}
