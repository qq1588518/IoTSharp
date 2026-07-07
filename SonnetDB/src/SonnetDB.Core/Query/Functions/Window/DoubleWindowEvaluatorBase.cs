using SonnetDB.Model;

namespace SonnetDB.Query.Functions.Window;

internal abstract class DoubleWindowEvaluatorBase : IWindowDoubleEvaluator, IWindowStreamingEvaluator
{
    public abstract string FieldName { get; }

    public abstract IWindowState CreateState();

    public WindowDoubleOutput ComputeDouble(long[] timestamps, FieldValue?[] values)
    {
        ArgumentNullException.ThrowIfNull(timestamps);
        ArgumentNullException.ThrowIfNull(values);
        if (timestamps.Length != values.Length)
            throw new ArgumentException("timestamps 与 values 长度不一致。", nameof(values));

        var output = new double[timestamps.Length];
        var hasValue = new bool[timestamps.Length];
        ComputeDoubleCore(timestamps, values, output, hasValue);
        return new WindowDoubleOutput(output, hasValue);
    }

    public object?[] Compute(long[] timestamps, FieldValue?[] values)
    {
        ArgumentNullException.ThrowIfNull(timestamps);
        ArgumentNullException.ThrowIfNull(values);
        if (timestamps.Length != values.Length)
            throw new ArgumentException("timestamps 与 values 长度不一致。", nameof(values));

        var output = new object?[timestamps.Length];
        ComputeObjectCore(timestamps, values, output);
        return output;
    }

    protected virtual void ComputeDoubleCore(
        ReadOnlySpan<long> timestamps,
        ReadOnlySpan<FieldValue?> values,
        Span<double> output,
        Span<bool> hasValue)
    {
        var state = CreateState();
        for (int i = 0; i < timestamps.Length; i++)
        {
            var value = state.Update(timestamps[i], values[i]);
            if (!value.HasValue)
                continue;

            if (!value.TryGetDouble(out var doubleValue))
                throw new InvalidOperationException("数值型窗口函数状态返回了非 double 输出。");

            output[i] = doubleValue;
            hasValue[i] = true;
        }
    }

    protected virtual void ComputeObjectCore(
        ReadOnlySpan<long> timestamps,
        ReadOnlySpan<FieldValue?> values,
        Span<object?> output)
    {
        var state = CreateState();
        for (int i = 0; i < timestamps.Length; i++)
            output[i] = state.Update(timestamps[i], values[i]).ToObject();
    }
}
