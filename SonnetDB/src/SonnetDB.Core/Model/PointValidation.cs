namespace SonnetDB.Model;

/// <summary>
/// <see cref="Point"/> 参数校验辅助方法（内部使用）。
/// </summary>
internal static class PointValidation
{
    /// <summary>保留字符集合：不允许出现在 tag/field 键值中。</summary>
    private static readonly System.Buffers.SearchValues<char> _reservedChars =
        System.Buffers.SearchValues.Create(",=\n\r\t\"");

    /// <summary>
    /// 校验 Measurement / Tag key / Tag value / Field key 名称。
    /// 名称必须非空、非空白，且不含保留字符。
    /// </summary>
    /// <param name="name">待校验的名称。</param>
    /// <param name="paramName">参数名称（用于异常消息）。</param>
    /// <exception cref="ArgumentException">名称为空白或含保留字符。</exception>
    public static void ValidateName(string name, string paramName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException($"'{paramName}' must not be null or whitespace.", paramName);

        if (name.AsSpan().IndexOfAny(_reservedChars) >= 0)
            throw new ArgumentException(
                $"'{paramName}' contains reserved characters (,=\\n\\r\\t\"). Value: \"{name}\"",
                paramName);
    }

    /// <summary>
    /// 校验 Measurement 名称（还需满足长度 ≤ 255 限制）。
    /// </summary>
    /// <param name="measurement">待校验的 Measurement 名称。</param>
    /// <exception cref="ArgumentException">名称非法。</exception>
    public static void ValidateMeasurement(string measurement)
    {
        ValidateName(measurement, nameof(measurement));
        if (measurement.Length > 255)
            throw new ArgumentException(
                $"Measurement name must not exceed 255 characters. Length: {measurement.Length}",
                nameof(measurement));
    }
}
