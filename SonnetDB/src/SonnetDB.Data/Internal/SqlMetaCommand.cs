using System.Text.RegularExpressions;
using SonnetDB.Sql.Execution;

namespace SonnetDB.Data.Internal;

/// <summary>
/// SonnetDB ADO.NET 提供程序在客户端拦截的 SQL Console 风格元命令。
/// </summary>
/// <remarks>
/// SonnetDB 服务端按 URL 路径 (<c>/v1/db/{db}/sql</c>) 强绑定目标库，没有连接级
/// "current database" 状态；同样 <see cref="SonnetDB.Sql.SqlParser"/> 也不识别
/// <c>USE</c> / <c>current_database()</c> 等关键字。为了让 ADO.NET 用户在通过
/// <c>CREATE DATABASE foo</c> 建库后能用熟悉的 MySQL / PostgreSQL 写法切换并查询当前库，
/// 下列命令在 ADO.NET 提供程序内被拦截、不再下发到服务端：
/// <list type="bullet">
///   <item><description><c>USE &lt;db&gt;</c> — 切换当前连接的目标库（远程模式生效；嵌入式模式因数据源等价于路径而返回错误）。</description></item>
///   <item><description><c>SELECT current_database()</c> / <c>SELECT database()</c>
///     / <c>SHOW CURRENT_DATABASE</c> / <c>SHOW CURRENT DATABASE</c>
///     — 立即返回单列单行结果集。</description></item>
/// </list>
/// </remarks>
internal static class SqlMetaCommand
{
    private static readonly Regex _useRegex = new(
        @"^\s*use\s+(`?)(?<name>[A-Za-z_][A-Za-z0-9_]*)\1\s*;?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _currentDatabaseSelectRegex = new(
        @"^\s*select\s+(current_database|database)\s*\(\s*\)\s*;?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _showCurrentDatabaseRegex = new(
        @"^\s*show\s+current[\s_]+database\s*;?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>识别一条语句是否为元命令；不匹配则返回 <see cref="MetaKind.None"/>。</summary>
    public static MetaKind TryParse(string sql, out string database)
    {
        database = string.Empty;
        if (string.IsNullOrWhiteSpace(sql))
            return MetaKind.None;

        var match = _useRegex.Match(sql);
        if (match.Success)
        {
            database = match.Groups["name"].Value;
            return MetaKind.UseDatabase;
        }
        if (_currentDatabaseSelectRegex.IsMatch(sql) || _showCurrentDatabaseRegex.IsMatch(sql))
            return MetaKind.CurrentDatabase;

        return MetaKind.None;
    }

    /// <summary>构造 <c>SELECT current_database()</c> 的单行结果集。</summary>
    public static SelectExecutionResult BuildCurrentDatabaseResult(string database)
    {
        var columns = new[] { "current_database" };
        var rows = new IReadOnlyList<object?>[] { new object?[] { database } };
        return new SelectExecutionResult(columns, rows);
    }

    /// <summary>构造 <c>USE &lt;db&gt;</c> 成功后的单行结果集。</summary>
    public static SelectExecutionResult BuildUseDatabaseResult(string database)
    {
        var columns = new[] { "database" };
        var rows = new IReadOnlyList<object?>[] { new object?[] { database } };
        return new SelectExecutionResult(columns, rows);
    }
}

/// <summary>SQL Console 元命令分类。</summary>
internal enum MetaKind
{
    /// <summary>不是元命令，按普通 SQL 走原有路径。</summary>
    None = 0,

    /// <summary><c>USE &lt;db&gt;</c>。</summary>
    UseDatabase,

    /// <summary><c>SELECT current_database()</c> / <c>SHOW CURRENT_DATABASE</c> 等。</summary>
    CurrentDatabase,
}
