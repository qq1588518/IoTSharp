using SonnetDB.Storage.Format;

namespace SonnetDB.Model;

/// <summary>
/// 字段值的统一表示，支持 Float64 / Int64 / Boolean / String / Vector / GeoPoint 类型，
/// 通过显式 <see cref="FieldType"/> 标签区分，零装箱。
/// </summary>
/// <remarks>
/// 设计要点：使用一个 <c>long _numeric</c> 字段以位模式（bit pattern）同时承载
/// Double（<see cref="BitConverter.DoubleToInt64Bits"/>）、Long 和 Bool（0/1），
/// 避免装箱到 <c>object</c>。String 单独保存在 <c>_string</c> 字段中。
/// </remarks>
public readonly struct FieldValue : IEquatable<FieldValue>
{
    /// <summary>字段的实际类型标签。</summary>
    public FieldType Type { get; }

    /// <summary>存储 Float64 / Int64 / Boolean 的数值位模式。</summary>
    private readonly long _numeric;

    /// <summary>仅当 <see cref="Type"/> 为 <see cref="FieldType.String"/> 时有值。</summary>
    private readonly string? _string;

    /// <summary>仅当 <see cref="Type"/> 为 <see cref="FieldType.Vector"/> 时有值（dim = <c>_vector.Length</c>）。</summary>
    private readonly ReadOnlyMemory<float> _vector;

    /// <summary>仅当 <see cref="Type"/> 为 <see cref="FieldType.GeoPoint"/> 时有值。</summary>
    private readonly GeoPoint _geoPoint;

    private FieldValue(FieldType type, long numeric, string? str)
    {
        Type = type;
        _numeric = numeric;
        _string = str;
        _vector = default;
        _geoPoint = default;
    }

    private FieldValue(ReadOnlyMemory<float> vector)
    {
        Type = FieldType.Vector;
        _numeric = vector.Length;
        _string = null;
        _vector = vector;
        _geoPoint = default;
    }

    private FieldValue(GeoPoint geoPoint)
    {
        Type = FieldType.GeoPoint;
        _numeric = 0;
        _string = null;
        _vector = default;
        _geoPoint = geoPoint;
    }

    // ── 工厂方法 ────────────────────────────────────────────────────────────

    /// <summary>从 64 位双精度浮点数创建字段值。</summary>
    public static FieldValue FromDouble(double value)
        => new(FieldType.Float64, BitConverter.DoubleToInt64Bits(value), null);

    /// <summary>从 64 位有符号整数创建字段值。</summary>
    public static FieldValue FromLong(long value)
        => new(FieldType.Int64, value, null);

    /// <summary>从布尔值创建字段值。</summary>
    public static FieldValue FromBool(bool value)
        => new(FieldType.Boolean, value ? 1L : 0L, null);

    /// <summary>从字符串创建字段值。</summary>
    /// <param name="value">非空字符串。</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> 为 null。</exception>
    public static FieldValue FromString(string value)
        => new(FieldType.String, 0L, value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>从 32 位浮点向量创建字段值。</summary>
    /// <param name="vector">向量数据；维度 = <see cref="ReadOnlyMemory{T}.Length"/>，必须 ≥ 1。</param>
    /// <exception cref="ArgumentException">向量维度小于 1。</exception>
    public static FieldValue FromVector(ReadOnlyMemory<float> vector)
    {
        if (vector.Length < 1)
            throw new ArgumentException("Vector dimension must be >= 1.", nameof(vector));
        return new FieldValue(vector);
    }

    /// <summary>从 32 位浮点向量数组创建字段值（不复制底层数组，调用方需保证不再修改）。</summary>
    /// <param name="vector">非空数组；长度即维度。</param>
    /// <exception cref="ArgumentNullException"><paramref name="vector"/> 为 null。</exception>
    /// <exception cref="ArgumentException">数组长度小于 1。</exception>
    public static FieldValue FromVector(float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);
        return FromVector(vector.AsMemory());
    }

    /// <summary>从地理空间点创建字段值。</summary>
    /// <param name="geoPoint">WGS84 地理点。</param>
    public static FieldValue FromGeoPoint(GeoPoint geoPoint)
        => new(GeoPoint.Create(geoPoint.Lat, geoPoint.Lon));

    /// <summary>从经纬度创建地理空间字段值。</summary>
    /// <param name="lat">纬度，范围 [-90, 90]。</param>
    /// <param name="lon">经度，范围 [-180, 180]。</param>
    /// <exception cref="ArgumentOutOfRangeException">纬度或经度超出合法范围。</exception>
    public static FieldValue FromGeoPoint(double lat, double lon)
        => new(GeoPoint.Create(lat, lon));

    // ── 取值方法 ────────────────────────────────────────────────────────────

    /// <summary>以 double 形式返回字段值。</summary>
    /// <exception cref="InvalidOperationException">字段类型不是 Float64。</exception>
    public double AsDouble() => Type == FieldType.Float64
        ? BitConverter.Int64BitsToDouble(_numeric)
        : throw new InvalidOperationException($"Field is {Type}, not Float64.");

    /// <summary>以 long 形式返回字段值。</summary>
    /// <exception cref="InvalidOperationException">字段类型不是 Int64。</exception>
    public long AsLong() => Type == FieldType.Int64
        ? _numeric
        : throw new InvalidOperationException($"Field is {Type}, not Int64.");

    /// <summary>以 bool 形式返回字段值。</summary>
    /// <exception cref="InvalidOperationException">字段类型不是 Boolean。</exception>
    public bool AsBool() => Type == FieldType.Boolean
        ? _numeric != 0
        : throw new InvalidOperationException($"Field is {Type}, not Boolean.");

    /// <summary>以 string 形式返回字段值。</summary>
    /// <exception cref="InvalidOperationException">字段类型不是 String。</exception>
    public string AsString() => Type == FieldType.String
        ? _string!
        : throw new InvalidOperationException($"Field is {Type}, not String.");

    /// <summary>以只读向量形式返回字段值。</summary>
    /// <exception cref="InvalidOperationException">字段类型不是 Vector。</exception>
    public ReadOnlyMemory<float> AsVector() => Type == FieldType.Vector
        ? _vector
        : throw new InvalidOperationException($"Field is {Type}, not Vector.");

    /// <summary>当字段类型为 Vector 时返回向量维度，否则抛出。</summary>
    /// <exception cref="InvalidOperationException">字段类型不是 Vector。</exception>
    public int VectorDimension => Type == FieldType.Vector
        ? (int)_numeric
        : throw new InvalidOperationException($"Field is {Type}, not Vector.");

    /// <summary>以地理点形式返回字段值。</summary>
    /// <exception cref="InvalidOperationException">字段类型不是 GeoPoint。</exception>
    public GeoPoint AsGeoPoint() => Type == FieldType.GeoPoint
        ? _geoPoint
        : throw new InvalidOperationException($"Field is {Type}, not GeoPoint.");

    // ── 辅助方法 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 尝试以 double 形式获取数值。
    /// Float64 直接转换，Int64 / Boolean 自动转换，String 返回 false。
    /// </summary>
    /// <param name="value">转换后的 double 值；失败时为 0。</param>
    /// <returns>转换成功返回 true，否则返回 false。</returns>
    public bool TryGetNumeric(out double value)
    {
        switch (Type)
        {
            case FieldType.Float64:
                value = BitConverter.Int64BitsToDouble(_numeric);
                return true;
            case FieldType.Int64:
                value = (double)_numeric;
                return true;
            case FieldType.Boolean:
                value = _numeric != 0 ? 1.0 : 0.0;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    // ── 相等性 ──────────────────────────────────────────────────────────────

    /// <summary>按类型和值比较两个 <see cref="FieldValue"/> 是否相等。</summary>
    public bool Equals(FieldValue other)
    {
        if (Type != other.Type)
            return false;
        return Type switch
        {
            FieldType.String => string.Equals(_string, other._string, StringComparison.Ordinal),
            FieldType.Vector => _vector.Span.SequenceEqual(other._vector.Span),
            FieldType.GeoPoint => _geoPoint.Equals(other._geoPoint),
            _ => _numeric == other._numeric,
        };
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is FieldValue v && Equals(v);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (Type == FieldType.Vector)
        {
            var hc = new HashCode();
            hc.Add(Type);
            hc.Add(_vector.Length);
            // 仅采样首/中/末三个分量参与哈希，避免长向量遍历；Equals 仍按全量比较。
            var span = _vector.Span;
            if (span.Length >= 1) hc.Add(span[0]);
            if (span.Length >= 2) hc.Add(span[span.Length / 2]);
            if (span.Length >= 3) hc.Add(span[^1]);
            return hc.ToHashCode();
        }
        if (Type == FieldType.GeoPoint)
            return HashCode.Combine(Type, _geoPoint);
        return HashCode.Combine(Type, _numeric, _string);
    }

    /// <inheritdoc/>
    public override string ToString() => Type switch
    {
        FieldType.Float64 => BitConverter.Int64BitsToDouble(_numeric).ToString("G"),
        FieldType.Int64 => _numeric.ToString(),
        FieldType.Boolean => _numeric != 0 ? "true" : "false",
        FieldType.String => _string!,
        FieldType.Vector => FormatVector(_vector.Span),
        FieldType.GeoPoint => _geoPoint.ToString(),
        _ => $"Unknown({Type})",
    };

    private static string FormatVector(ReadOnlySpan<float> span)
    {
        // 形如 "vector(3)[0.1,0.2,0.3]"；超过 8 维时截断展示前 8 个分量。
        var sb = new System.Text.StringBuilder();
        sb.Append("vector(").Append(span.Length).Append(")[");
        int show = Math.Min(span.Length, 8);
        for (int i = 0; i < show; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(span[i].ToString("G"));
        }
        if (span.Length > show) sb.Append(",...");
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>相等运算符。</summary>
    public static bool operator ==(FieldValue l, FieldValue r) => l.Equals(r);

    /// <summary>不等运算符。</summary>
    public static bool operator !=(FieldValue l, FieldValue r) => !l.Equals(r);
}
