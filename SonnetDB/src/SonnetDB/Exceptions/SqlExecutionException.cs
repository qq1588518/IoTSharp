namespace SonnetDB.Exceptions;

/// <summary>
/// 表示 Copilot 在执行只读 SQL 工具时遇到可反馈给模型的解析/校验/执行错误。
/// </summary>
internal sealed class SqlExecutionException : Exception
{
    /// <summary>
    /// 出错时尝试执行的 SQL 文本。
    /// </summary>
    public string Sql { get; }

    /// <summary>
    /// 失败阶段：<c>parse</c> / <c>validate</c> / <c>execute</c>。
    /// </summary>
    public string Phase { get; }

    public SqlExecutionException(string sql, string phase, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Sql = sql;
        Phase = phase;
    }
}
