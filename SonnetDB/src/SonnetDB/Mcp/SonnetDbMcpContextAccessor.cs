using Microsoft.AspNetCore.Http;
using SonnetDB.Engine;

namespace SonnetDB.Mcp;

/// <summary>
/// 访问当前 MCP HTTP 请求绑定的数据库上下文。
/// </summary>
internal sealed class SonnetDbMcpContextAccessor(IHttpContextAccessor httpContextAccessor)
{
    internal const string DatabaseNameItemKey = "sndb.mcp.db";
    internal const string TsdbItemKey = "sndb.mcp.tsdb";

    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    /// <summary>
    /// 获取当前 MCP 请求的 <see cref="HttpContext"/>。
    /// </summary>
    public HttpContext GetHttpContext()
        => _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("当前请求不在 HTTP 上下文中。");

    /// <summary>
    /// 获取当前 MCP 会话绑定的数据库名。
    /// </summary>
    public string GetDatabaseName()
    {
        var context = GetHttpContext();
        if (context.Items.TryGetValue(DatabaseNameItemKey, out var value) && value is string databaseName)
            return databaseName;
        throw new InvalidOperationException("当前请求未绑定 SonnetDB MCP 数据库。");
    }

    /// <summary>
    /// 获取当前 MCP 会话绑定的 <see cref="Tsdb"/> 实例。
    /// </summary>
    public Tsdb GetDatabase()
    {
        var context = GetHttpContext();
        if (context.Items.TryGetValue(TsdbItemKey, out var value) && value is Tsdb tsdb)
            return tsdb;
        throw new InvalidOperationException("当前请求未绑定 SonnetDB MCP 数据库实例。");
    }
}
