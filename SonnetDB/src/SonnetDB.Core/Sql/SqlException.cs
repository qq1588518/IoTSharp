namespace SonnetDB.Sql;

/// <summary>
/// SQL 词法或语法分析阶段抛出的异常。
/// </summary>
public sealed class SqlParseException : Exception
{
    /// <summary>错误在源 SQL 中的字符索引（0-based）。</summary>
    public int Position { get; }

    /// <summary>构造一个新的 <see cref="SqlParseException"/>。</summary>
    /// <param name="message">错误消息（中文）。</param>
    /// <param name="position">在源 SQL 中的字符索引。</param>
    public SqlParseException(string message, int position)
        : base($"{message}（位置 {position}）")
    {
        Position = position;
    }
}
