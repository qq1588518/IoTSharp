namespace SonnetDB.Model;

/// <summary>
/// 数值聚合的累加器，支持 Count / Sum / Min / Max / Avg。
/// 仅适用于可数值化字段（Float64 / Int64 / Boolean）。
/// </summary>
public sealed class AggregateResult
{
    /// <summary>已累加的数据点数量。</summary>
    public long Count { get; private set; }

    /// <summary>所有值的累加和。</summary>
    public double Sum { get; private set; }

    /// <summary>所有值中的最小值（初始为 <see cref="double.PositiveInfinity"/>）。</summary>
    public double Min { get; private set; } = double.PositiveInfinity;

    /// <summary>所有值中的最大值（初始为 <see cref="double.NegativeInfinity"/>）。</summary>
    public double Max { get; private set; } = double.NegativeInfinity;

    /// <summary>平均值；若 <see cref="Count"/> 为 0 则返回 0。</summary>
    public double Avg => Count == 0 ? 0d : Sum / Count;

    // ── 操作方法 ────────────────────────────────────────────────────────────

    /// <summary>累加一个数值。</summary>
    /// <param name="value">要累加的数值。</param>
    public void Add(double value)
    {
        Count++;
        Sum += value;
        if (value < Min) Min = value;
        if (value > Max) Max = value;
    }

    /// <summary>
    /// 将另一个 <see cref="AggregateResult"/> 合并到当前实例。
    /// 等价于将 <paramref name="other"/> 的所有值依次 <see cref="Add"/>。
    /// </summary>
    /// <param name="other">要合并的聚合结果。</param>
    public void Merge(AggregateResult other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other.Count == 0)
            return;

        Count += other.Count;
        Sum += other.Sum;
        if (other.Min < Min) Min = other.Min;
        if (other.Max > Max) Max = other.Max;
    }

    /// <summary>将累加器重置为初始状态。</summary>
    public void Reset()
    {
        Count = 0;
        Sum = 0;
        Min = double.PositiveInfinity;
        Max = double.NegativeInfinity;
    }
}
