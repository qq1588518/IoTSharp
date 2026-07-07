namespace SonnetDB.Query.Functions;

/// <summary>内置标量函数描述。</summary>
public interface IScalarFunction : ISqlFunction
{
    /// <summary>最少参数个数。</summary>
    int MinArgumentCount { get; }

    /// <summary>最多参数个数。</summary>
    int MaxArgumentCount { get; }

    /// <summary>计算函数结果。</summary>
    object? Evaluate(IReadOnlyList<object?> args);
}
