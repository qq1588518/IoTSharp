using System.Globalization;
using System.Text.Json;

namespace SonnetDB.Accuracy.Tests;

internal static class AccuracyResultNormalizer
{
    public static IReadOnlyList<string> NormalizeRows(IEnumerable<IReadOnlyList<object?>> rows, bool sortRows = true)
    {
        var normalized = rows.Select(NormalizeRow).ToList();
        if (sortRows)
            normalized.Sort(StringComparer.Ordinal);
        return normalized;
    }

    public static IReadOnlyList<object?> ConvertSonnetRow(JsonElement row)
    {
        var values = new List<object?>();
        foreach (var element in row.EnumerateArray())
            values.Add(ConvertJsonScalar(element));
        return values;
    }

    private static string NormalizeRow(IReadOnlyList<object?> row)
        => string.Join("|", row.Select(NormalizeScalar));

    private static object? ConvertJsonScalar(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => element.GetDouble(),
            _ => element.GetRawText(),
        };
    }

    private static string NormalizeScalar(object? value)
    {
        return value switch
        {
            null => "NULL",
            JsonElement jsonElement => NormalizeScalar(ConvertJsonScalar(jsonElement)),
            DateTime dateTime => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                .Subtract(DateTime.UnixEpoch)
                .TotalMilliseconds
                .ToString("F0", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            bool boolValue => boolValue ? "true" : "false",
            string stringValue => stringValue,
            double doubleValue => doubleValue.ToString("G17", CultureInfo.InvariantCulture),
            float floatValue => floatValue.ToString("G9", CultureInfo.InvariantCulture),
            decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture),
            byte byteValue => byteValue.ToString(CultureInfo.InvariantCulture),
            sbyte sbyteValue => sbyteValue.ToString(CultureInfo.InvariantCulture),
            short shortValue => shortValue.ToString(CultureInfo.InvariantCulture),
            ushort ushortValue => ushortValue.ToString(CultureInfo.InvariantCulture),
            int intValue => intValue.ToString(CultureInfo.InvariantCulture),
            uint uintValue => uintValue.ToString(CultureInfo.InvariantCulture),
            long longValue => longValue.ToString(CultureInfo.InvariantCulture),
            ulong ulongValue => ulongValue.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "NULL",
        };
    }
}
