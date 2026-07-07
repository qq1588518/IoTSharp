using System.Text.Json;
using System.Text.Json.Serialization;
using SonnetDB.Model;

namespace SonnetDB.Json;

/// <summary>
/// 将 <see cref="GeoPoint"/> 序列化为 GeoJSON Point 对象。
/// </summary>
public sealed class GeoJsonConverter : JsonConverter<GeoPoint>
{
    /// <inheritdoc />
    public override GeoPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("GeoJSON Point 反序列化暂未开放。局部写入请使用 SQL POINT(lat, lon) 或 ADO.NET GeoPoint 参数。");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, GeoPoint value, JsonSerializerOptions options)
        => GeoJsonWriter.WritePointGeometry(writer, value);
}
