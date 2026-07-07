using SonnetDB.Model;

namespace SonnetDB.Query.Functions;

/// <summary>
/// 以 <see cref="double"/> 数组输出的窗口函数求值器。
/// </summary>
/// <remarks>
/// 该接口用于数值型窗口函数的热路径，避免 <see cref="IWindowEvaluator.Compute"/>
/// 为每一行提前装箱成 <see cref="object"/>。SQL 输出层仍会在最终行结果中按需装箱，
/// 以保持既有 <c>object?</c> 结果模型兼容。
/// </remarks>
public interface IWindowDoubleEvaluator : IWindowEvaluator
{
    /// <summary>
    /// 计算数值型窗口输出。
    /// </summary>
    /// <param name="timestamps">按时间递增排列的时间戳数组。</param>
    /// <param name="values">与 <paramref name="timestamps"/> 等长的输入值；缺失行为 <c>null</c>。</param>
    /// <returns>与输入等长的双精度输出及有效位图。</returns>
    WindowDoubleOutput ComputeDouble(long[] timestamps, FieldValue?[] values);
}

/// <summary>
/// 数值型窗口函数的 typed 输出。
/// </summary>
public sealed class WindowDoubleOutput
{
    /// <summary>
    /// 创建 typed 窗口输出。
    /// </summary>
    /// <param name="values">每行的双精度输出值。</param>
    /// <param name="hasValue">每行是否存在有效输出。</param>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> 或 <paramref name="hasValue"/> 为 null。</exception>
    /// <exception cref="ArgumentException">两个数组长度不一致。</exception>
    public WindowDoubleOutput(double[] values, bool[] hasValue)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(hasValue);
        if (values.Length != hasValue.Length)
            throw new ArgumentException("窗口函数 typed 输出的 values 与 hasValue 长度必须一致。");

        Values = values;
        HasValue = hasValue;
    }

    /// <summary>每行的双精度输出值；仅当对应 <see cref="HasValue"/> 为 true 时有效。</summary>
    public double[] Values { get; }

    /// <summary>每行是否存在有效输出；false 表示该行 SQL 输出为 NULL。</summary>
    public bool[] HasValue { get; }

    /// <summary>输出行数。</summary>
    public int Length => Values.Length;

    /// <summary>
    /// 尝试获取指定行的数值输出。
    /// </summary>
    /// <param name="index">行号。</param>
    /// <param name="value">存在有效输出时返回该行数值。</param>
    /// <returns>该行存在有效输出时返回 true；否则返回 false。</returns>
    public bool TryGetValue(int index, out double value)
    {
        if (HasValue[index])
        {
            value = Values[index];
            return true;
        }

        value = 0d;
        return false;
    }
}
