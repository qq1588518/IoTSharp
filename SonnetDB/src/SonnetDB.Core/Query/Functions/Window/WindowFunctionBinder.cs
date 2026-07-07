using SonnetDB.Catalog;
using SonnetDB.Model;
using SonnetDB.Sql.Ast;
using SonnetDB.Storage.Format;

namespace SonnetDB.Query.Functions.Window;

/// <summary>窗口函数解析与共用工具。</summary>
internal static class WindowFunctionBinder
{
    /// <summary>把 <see cref="FieldValue"/> 转 double；不支持 String，遇到 String 抛错。</summary>
    public static bool TryToDouble(FieldValue? value, out double result)
    {
        if (value is not { } v)
        {
            result = 0;
            return false;
        }

        if (v.TryGetNumeric(out result))
            return true;

        // String / 其他不可数值化
        result = 0;
        return false;
    }

    /// <summary>把 <see cref="FieldValue"/> 转换为可比较的盒装值（支持 String / Bool / 数值）。</summary>
    public static object? UnboxValue(FieldValue? value)
    {
        if (value is not { } v) return null;
        return v.Type switch
        {
            FieldType.Float64 => v.AsDouble(),
            FieldType.Int64 => v.AsLong(),
            FieldType.Boolean => v.AsBool(),
            FieldType.String => v.AsString(),
            _ => throw new InvalidOperationException($"窗口函数遇到不支持的 FieldType {v.Type}。"),
        };
    }

    /// <summary>解析窗口函数的字段参数（必填，必须为 IDENTIFIER 列名，且为 FIELD 角色）。</summary>
    public static MeasurementColumn ResolveFieldArgument(
        FunctionCallExpression call,
        MeasurementSchema schema,
        string functionName,
        int argumentIndex,
        bool allowString = false)
    {
        if (call.IsStar)
            throw new InvalidOperationException($"窗口函数 {functionName}(*) 非法。");

        if (call.Arguments.Count <= argumentIndex
            || call.Arguments[argumentIndex] is not IdentifierExpression id)
        {
            throw new InvalidOperationException(
                $"窗口函数 {functionName} 第 {argumentIndex + 1} 个参数必须是字段列名。");
        }

        var col = schema.TryGetColumn(id.Name)
            ?? throw new InvalidOperationException(
                $"窗口函数 {functionName} 引用了未知列 '{id.Name}'。");
        if (col.Role != MeasurementColumnRole.Field)
            throw new InvalidOperationException(
                $"窗口函数 {functionName} 的参数 '{id.Name}' 必须是 FIELD 列。");
        if (!allowString && col.DataType == FieldType.String)
            throw new InvalidOperationException(
                $"窗口函数 {functionName} 不支持 String 字段 '{id.Name}'。");
        return col;
    }

    /// <summary>校验函数实参个数。</summary>
    public static void RequireArgumentCount(
        FunctionCallExpression call,
        string functionName,
        int min,
        int max)
    {
        int n = call.Arguments.Count;
        if (n < min || n > max)
        {
            string expected = min == max ? min.ToString() : $"{min}~{max}";
            throw new InvalidOperationException(
                $"窗口函数 {functionName} 需要 {expected} 个参数，实际为 {n}。");
        }
    }

    /// <summary>解析正整数常量（用于 N 点窗口）。</summary>
    public static int ResolvePositiveIntArgument(
        FunctionCallExpression call,
        int argumentIndex,
        string functionName)
    {
        if (call.Arguments[argumentIndex] is not LiteralExpression lit
            || lit.Kind != SqlLiteralKind.Integer
            || lit.IntegerValue <= 0
            || lit.IntegerValue > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"窗口函数 {functionName} 第 {argumentIndex + 1} 个参数必须是正整数。");
        }

        return (int)lit.IntegerValue;
    }

    /// <summary>解析数值常量（用于 alpha、fill value 等）。允许 <c>-1</c> 这种带负号的字面量。</summary>
    public static double ResolveNumericArgument(
        FunctionCallExpression call,
        int argumentIndex,
        string functionName)
    {
        var arg = call.Arguments[argumentIndex];
        double sign = 1.0;
        if (arg is UnaryExpression { Operator: SqlUnaryOperator.Negate, Operand: var operand })
        {
            sign = -1.0;
            arg = operand;
        }

        if (arg is not LiteralExpression lit)
            throw new InvalidOperationException(
                $"窗口函数 {functionName} 第 {argumentIndex + 1} 个参数必须是数值字面量。");

        return lit.Kind switch
        {
            SqlLiteralKind.Integer => sign * lit.IntegerValue,
            SqlLiteralKind.Float => sign * lit.FloatValue,
            _ => throw new InvalidOperationException(
                $"窗口函数 {functionName} 第 {argumentIndex + 1} 个参数必须是数值字面量。"),
        };
    }

    /// <summary>
    /// 解析时间单位（用于 derivative / rate / integral 等）。默认 1 秒。
    /// 接受 <see cref="DurationLiteralExpression"/> 形式（如 <c>1s</c> / <c>100ms</c>）。
    /// </summary>
    public static long ResolveUnitMillisecondsArgument(
        FunctionCallExpression call,
        int argumentIndex,
        string functionName,
        long defaultMs = 1000)
    {
        if (call.Arguments.Count <= argumentIndex)
            return defaultMs;

        if (call.Arguments[argumentIndex] is not DurationLiteralExpression duration
            || duration.Milliseconds <= 0)
        {
            throw new InvalidOperationException(
                $"窗口函数 {functionName} 第 {argumentIndex + 1} 个参数必须是 > 0 的 duration 字面量（如 1s / 100ms）。");
        }

        return duration.Milliseconds;
    }
}
