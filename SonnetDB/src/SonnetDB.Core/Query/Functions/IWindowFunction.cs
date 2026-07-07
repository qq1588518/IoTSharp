using SonnetDB.Catalog;
using SonnetDB.Sql.Ast;

namespace SonnetDB.Query.Functions;

/// <summary>
/// 行级窗口函数（Tier 3，PR #53+）。每个 series 独立运行，输入是按时间递增的
/// (timestamp, FieldValue?) 序列，输出与输入一一对应。
/// <para>
/// 与 <see cref="IAggregateFunction"/> 不同，窗口函数不会折叠行数；
/// 也与 <see cref="IScalarFunction"/> 不同，窗口函数依赖前后行（差分、滑动平均、累计和等）。
/// </para>
/// </summary>
public interface IWindowFunction : ISqlFunction
{
    /// <summary>校验调用语法并返回为本调用专属的求值器实例。</summary>
    /// <param name="call">SQL 中的函数调用 AST。</param>
    /// <param name="schema">所属 measurement 的 schema。</param>
    /// <returns>求值器；其 <see cref="IWindowEvaluator.FieldName"/> 决定本投影的字段依赖。</returns>
    IWindowEvaluator CreateEvaluator(FunctionCallExpression call, MeasurementSchema schema);
}
