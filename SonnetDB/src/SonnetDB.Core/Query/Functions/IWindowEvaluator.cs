using SonnetDB.Model;

namespace SonnetDB.Query.Functions;

/// <summary>
/// 窗口函数求值器：对一个 series 的时间有序数据点序列计算等长输出。
/// </summary>
public interface IWindowEvaluator
{
    /// <summary>本求值器依赖的字段列名（驱动数据源）。</summary>
    string FieldName { get; }

    /// <summary>
    /// 计算窗口输出。<paramref name="timestamps"/> 严格递增，
    /// <paramref name="values"/> 与 <paramref name="timestamps"/> 等长，
    /// 缺失行（外连接产生）使用 <c>null</c>。
    /// </summary>
    /// <returns>与输入等长的输出数组；可空元素表示该行无法计算（例如首行差分）。</returns>
    object?[] Compute(long[] timestamps, FieldValue?[] values);
}
