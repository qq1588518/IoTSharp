namespace SonnetDB.Auth;

/// <summary>
/// 数据库级访问权限等级。高级别隐含低级别（Admin &gt; Write &gt; Read &gt; None）。
/// </summary>
public enum DatabasePermission
{
    /// <summary>无任何权限。</summary>
    None = 0,
    /// <summary>仅可执行 SELECT。</summary>
    Read = 1,
    /// <summary>可执行 SELECT / INSERT / DELETE / CREATE MEASUREMENT。</summary>
    Write = 2,
    /// <summary>可执行所有 DML/DDL（不含用户管理）。</summary>
    Admin = 3,
}
