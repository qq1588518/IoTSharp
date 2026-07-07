using SonnetDB.Sql.Ast;

namespace SonnetDB.Sql.Execution;

/// <summary>
/// 控制面（用户、权限、数据库管理）操作抽象。
/// </summary>
/// <remarks>
/// <para>
/// 仅在<b>服务端模式</b>由 <c>SonnetDB</c> 注入实现；嵌入式 <see cref="Tsdb"/> 调用方
/// 不应实现该接口。<see cref="SqlExecutor"/> 在执行控制面 DDL（CREATE USER / GRANT 等）时，
/// 若未传入 <see cref="IControlPlane"/>，将抛出 <see cref="NotSupportedException"/>。
/// </para>
/// <para>
/// 实现需保证线程安全；持久化由实现自身负责（典型实现是原子写 JSON 文件）。
/// </para>
/// </remarks>
public interface IControlPlane
{
    /// <summary>创建用户；若同名用户已存在抛 <see cref="InvalidOperationException"/>。</summary>
    /// <param name="userName">用户名。</param>
    /// <param name="password">明文密码（不持久化，立即 PBKDF2 哈希）。</param>
    /// <param name="isSuperuser">是否超级用户。</param>
    void CreateUser(string userName, string password, bool isSuperuser);

    /// <summary>修改用户密码；若用户不存在抛 <see cref="InvalidOperationException"/>。</summary>
    /// <param name="userName">用户名。</param>
    /// <param name="newPassword">新密码。</param>
    void AlterUserPassword(string userName, string newPassword);

    /// <summary>删除用户及其所有 token、grant；若用户不存在抛 <see cref="InvalidOperationException"/>。</summary>
    /// <param name="userName">用户名。</param>
    void DropUser(string userName);

    /// <summary>授予用户在某数据库上的权限（已存在更高权限则保持不变）。</summary>
    /// <param name="userName">用户名。</param>
    /// <param name="database">数据库名（<c>*</c> 表示全部）。</param>
    /// <param name="permission">权限级别。</param>
    void Grant(string userName, string database, GrantPermission permission);

    /// <summary>撤销用户在某数据库上的全部权限。</summary>
    /// <param name="userName">用户名。</param>
    /// <param name="database">数据库名（<c>*</c> 表示全部）。</param>
    void Revoke(string userName, string database);

    /// <summary>创建数据库；若同名数据库已存在抛 <see cref="InvalidOperationException"/>。</summary>
    /// <param name="databaseName">数据库名。</param>
    void CreateDatabase(string databaseName);

    /// <summary>删除数据库（含其所有 measurement、segment、grant）。</summary>
    /// <param name="databaseName">数据库名。</param>
    void DropDatabase(string databaseName);

    /// <summary>列出所有用户（按用户名排序）。</summary>
    /// <returns>用户摘要序列。</returns>
    IReadOnlyList<UserSummary> ListUsers();

    /// <summary>列出 grants（按 user_name, database 排序）。</summary>
    /// <param name="userName">可选用户名筛选；<c>null</c> 表示列出全部。</param>
    /// <returns>grant 三元组序列。</returns>
    IReadOnlyList<GrantSummary> ListGrants(string? userName);

    /// <summary>列出所有已注册数据库（按名称排序）。</summary>
    /// <returns>数据库名序列。</returns>
    IReadOnlyList<string> ListDatabases();

    // PR #34b-3-tokens：API token 管理。

    /// <summary>列出 token 元数据（按 user_name, created_utc 排序），永不返回明文。</summary>
    /// <param name="userName">可选用户名筛选；<c>null</c> 表示列出全部。</param>
    /// <returns>token 摘要序列。</returns>
    IReadOnlyList<TokenSummary> ListTokens(string? userName);

    /// <summary>为指定用户颁发一个新 token。</summary>
    /// <param name="userName">目标用户名。</param>
    /// <returns>token id 与明文 token；明文仅此处一次性返回。</returns>
    /// <exception cref="InvalidOperationException">用户不存在。</exception>
    (string TokenId, string TokenPlain) IssueToken(string userName);

    /// <summary>按 token id 吊销一个已颁发的 token。</summary>
    /// <param name="tokenId">token 短 id。</param>
    /// <exception cref="InvalidOperationException">token id 不存在。</exception>
    void RevokeToken(string tokenId);
}

/// <summary>SHOW TOKENS 行。仅元数据，不含明文。</summary>
/// <param name="TokenId">token 短 id。</param>
/// <param name="UserName">所属用户名。</param>
/// <param name="CreatedUtc">颁发时间（UTC）。</param>
/// <param name="LastUsedUtc">最近一次使用时间（UTC），从未使用为 <c>null</c>。</param>
public sealed record TokenSummary(
    string TokenId,
    string UserName,
    DateTime CreatedUtc,
    DateTime? LastUsedUtc);

/// <summary>SHOW USERS 行。</summary>
/// <param name="Name">用户名。</param>
/// <param name="IsSuperuser">是否超级用户。</param>
/// <param name="CreatedUtc">创建时间（UTC）。</param>
/// <param name="TokenCount">当前有效 token 数。</param>
public sealed record UserSummary(
    string Name,
    bool IsSuperuser,
    DateTime CreatedUtc,
    int TokenCount);

/// <summary>SHOW GRANTS 行。</summary>
/// <param name="UserName">被授权用户名。</param>
/// <param name="Database">数据库名（<c>*</c> 表示通配）。</param>
/// <param name="Permission">权限级别（<c>Read</c> / <c>Write</c> / <c>Admin</c>）。</param>
public sealed record GrantSummary(
    string UserName,
    string Database,
    GrantPermission Permission);
