using System.Collections;
using System.Data.Common;

namespace SonnetDB.Data;

/// <summary>
/// <see cref="SndbCommand"/> 上的参数集合。仅接受 <see cref="SndbParameter"/>。
/// </summary>
public sealed class SndbParameterCollection : DbParameterCollection
{
    private readonly List<SndbParameter> _items = [];
    private readonly object _sync = new();

    /// <inheritdoc />
    public override int Count => _items.Count;

    /// <inheritdoc />
    public override object SyncRoot => _sync;

    /// <inheritdoc />
    public override int Add(object value)
    {
        var p = Cast(value);
        _items.Add(p);
        return _items.Count - 1;
    }

    /// <summary>添加一个强类型参数。</summary>
    public SndbParameter Add(SndbParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        _items.Add(parameter);
        return parameter;
    }

    /// <summary>便捷重载：按名称与值添加。</summary>
    public SndbParameter AddWithValue(string parameterName, object? value)
    {
        var p = new SndbParameter(parameterName, value);
        _items.Add(p);
        return p;
    }

    /// <inheritdoc />
    public override void AddRange(Array values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var v in values) Add(v!);
    }

    /// <inheritdoc />
    public override void Clear() => _items.Clear();

    /// <inheritdoc />
    public override bool Contains(object value) => value is SndbParameter p && _items.Contains(p);

    /// <inheritdoc />
    public override bool Contains(string value) => IndexOf(value) >= 0;

    /// <inheritdoc />
    public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    /// <inheritdoc />
    public override IEnumerator GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    protected override DbParameter GetParameter(int index) => _items[index];

    /// <inheritdoc />
    protected override DbParameter GetParameter(string parameterName)
    {
        var idx = IndexOf(parameterName);
        if (idx < 0) throw new IndexOutOfRangeException($"未找到参数 '{parameterName}'。");
        return _items[idx];
    }

    /// <inheritdoc />
    public override int IndexOf(object value) => value is SndbParameter p ? _items.IndexOf(p) : -1;

    /// <inheritdoc />
    public override int IndexOf(string parameterName)
    {
        for (int i = 0; i < _items.Count; i++)
            if (string.Equals(NormalizeName(_items[i].ParameterName), NormalizeName(parameterName), StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <inheritdoc />
    public override void Insert(int index, object value) => _items.Insert(index, Cast(value));

    /// <inheritdoc />
    public override void Remove(object value)
    {
        if (value is SndbParameter p) _items.Remove(p);
    }

    /// <inheritdoc />
    public override void RemoveAt(int index) => _items.RemoveAt(index);

    /// <inheritdoc />
    public override void RemoveAt(string parameterName)
    {
        var idx = IndexOf(parameterName);
        if (idx >= 0) _items.RemoveAt(idx);
    }

    /// <inheritdoc />
    protected override void SetParameter(int index, DbParameter value) => _items[index] = Cast(value);

    /// <inheritdoc />
    protected override void SetParameter(string parameterName, DbParameter value)
    {
        var idx = IndexOf(parameterName);
        if (idx < 0) _items.Add(Cast(value));
        else _items[idx] = Cast(value);
    }

    private static SndbParameter Cast(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value as SndbParameter
            ?? throw new InvalidCastException("仅支持 SndbParameter。");
    }

    internal static string NormalizeName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var span = name.AsSpan();
        if (span[0] == '@' || span[0] == ':') span = span[1..];
        return span.ToString();
    }

    internal IReadOnlyList<SndbParameter> Items => _items;
}
