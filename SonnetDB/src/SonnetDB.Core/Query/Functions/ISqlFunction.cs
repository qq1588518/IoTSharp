namespace SonnetDB.Query.Functions;

/// <summary>SQL 内置函数基接口。</summary>
public interface ISqlFunction
{
    /// <summary>函数名。</summary>
    string Name { get; }
}
