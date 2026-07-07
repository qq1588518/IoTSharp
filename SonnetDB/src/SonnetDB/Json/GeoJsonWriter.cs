using System.Text.Json;
using SonnetDB.Model;

namespace SonnetDB.Json;

/// <summary>
/// GeoJSON 手写序列化工具，避免引入第三方依赖。
/// </summary>
public static class GeoJsonWriter
{
    /// <summary>
    /// 写出 GeoJSON Point geometry，坐标顺序遵循标准 <c>[lon, lat]</c>。
    /// </summary>
    /// <param name="writer">JSON 写入器。</param>
    /// <param name="point">地理点。</param>
    public static void WritePointGeometry(Utf8JsonWriter writer, GeoPoint point)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "Point");
        writer.WritePropertyName("coordinates");
        writer.WriteStartArray();
        writer.WriteNumberValue(point.Lon);
        writer.WriteNumberValue(point.Lat);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>
    /// 写出 GeoJSON Feature/Point。
    /// </summary>
    /// <param name="writer">JSON 写入器。</param>
    /// <param name="point">地理点。</param>
    /// <param name="timestamp">时间戳（毫秒）。</param>
    /// <param name="tags">序列 tag 属性。</param>
    public static void WritePointFeature(Utf8JsonWriter writer, GeoPoint point, long timestamp, IReadOnlyDictionary<string, string> tags)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "Feature");
        writer.WritePropertyName("geometry");
        WritePointGeometry(writer, point);
        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        writer.WriteNumber("time", timestamp);
        foreach (var tag in tags)
            writer.WriteString(tag.Key, tag.Value);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>
    /// 写出 GeoJSON LineString Feature。
    /// </summary>
    /// <param name="writer">JSON 写入器。</param>
    /// <param name="points">按时间排序的轨迹点。</param>
    /// <param name="tags">序列 tag 属性。</param>
    public static void WriteLineStringFeature(
        Utf8JsonWriter writer,
        IReadOnlyList<(long Timestamp, GeoPoint Point)> points,
        IReadOnlyDictionary<string, string> tags)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "Feature");
        writer.WritePropertyName("geometry");
        writer.WriteStartObject();
        writer.WriteString("type", "LineString");
        writer.WritePropertyName("coordinates");
        writer.WriteStartArray();
        foreach (var (_, point) in points)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(point.Lon);
            writer.WriteNumberValue(point.Lat);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        if (points.Count > 0)
        {
            writer.WriteNumber("from", points[0].Timestamp);
            writer.WriteNumber("to", points[^1].Timestamp);
            writer.WriteNumber("pointCount", points.Count);
        }
        foreach (var tag in tags)
            writer.WriteString(tag.Key, tag.Value);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
