namespace SonnetDB.Query.Functions;

/// <summary>SQL 函数类别。</summary>
public enum FunctionKind
{
    Unknown,
    Scalar,
    Aggregate,
    Window,
    TableValued,
}
