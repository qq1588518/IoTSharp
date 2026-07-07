using SonnetDB.Model;

namespace SonnetDB.Query.Functions;

/// <summary>
/// 窗口函数的流式状态机。
/// </summary>
public interface IWindowState
{
    /// <summary>
    /// 推进一行输入并返回该行窗口函数输出。
    /// </summary>
    /// <param name="timestamp">当前行时间戳。</param>
    /// <param name="value">当前行输入值；外连接缺失时为 <c>null</c>。</param>
    /// <returns>当前行窗口函数输出。</returns>
    WindowStateOutput Update(long timestamp, FieldValue? value);
}

/// <summary>
/// 支持创建流式状态机的窗口函数求值器。
/// </summary>
public interface IWindowStreamingEvaluator : IWindowEvaluator
{
    /// <summary>
    /// 为一个独立 series 创建新的窗口状态机。
    /// </summary>
    /// <returns>初始状态机。</returns>
    IWindowState CreateState();
}

/// <summary>
/// 窗口函数流式输出的值类型包装。
/// </summary>
public readonly struct WindowStateOutput
{
    private readonly object? _objectValue;

    private WindowStateOutput(WindowStateOutputKind kind, double doubleValue, object? objectValue)
    {
        Kind = kind;
        DoubleValue = doubleValue;
        _objectValue = objectValue;
    }

    /// <summary>输出种类。</summary>
    public WindowStateOutputKind Kind { get; }

    /// <summary>双精度输出值；仅当 <see cref="Kind"/> 为 <see cref="WindowStateOutputKind.Double"/> 时有效。</summary>
    public double DoubleValue { get; }

    /// <summary>是否存在有效输出。</summary>
    public bool HasValue => Kind != WindowStateOutputKind.Null;

    /// <summary>创建 NULL 输出。</summary>
    public static WindowStateOutput Null() => new(WindowStateOutputKind.Null, 0d, null);

    /// <summary>创建双精度输出。</summary>
    /// <param name="value">输出值。</param>
    public static WindowStateOutput FromDouble(double value)
        => new(WindowStateOutputKind.Double, value, null);

    /// <summary>创建兼容对象输出。</summary>
    /// <param name="value">输出值；为 null 时等价于 <see cref="Null"/>。</param>
    public static WindowStateOutput FromObject(object? value)
        => value is null ? Null() : new(WindowStateOutputKind.Object, 0d, value);

    /// <summary>
    /// 尝试读取双精度输出。
    /// </summary>
    /// <param name="value">成功时返回双精度值。</param>
    /// <returns>当前输出是双精度值时返回 true。</returns>
    public bool TryGetDouble(out double value)
    {
        if (Kind == WindowStateOutputKind.Double)
        {
            value = DoubleValue;
            return true;
        }

        value = 0d;
        return false;
    }

    /// <summary>
    /// 转换为 SQL 输出层使用的 <see cref="object"/> 兼容值。
    /// </summary>
    /// <returns>NULL 输出返回 null；双精度输出按需装箱为 <see cref="double"/>。</returns>
    public object? ToObject()
    {
        return Kind switch
        {
            WindowStateOutputKind.Null => null,
            WindowStateOutputKind.Double => DoubleValue,
            WindowStateOutputKind.Object => _objectValue,
            _ => null,
        };
    }
}

/// <summary>
/// 窗口函数流式输出种类。
/// </summary>
public enum WindowStateOutputKind
{
    /// <summary>SQL NULL。</summary>
    Null,

    /// <summary>双精度数值。</summary>
    Double,

    /// <summary>兼容对象值。</summary>
    Object,
}
