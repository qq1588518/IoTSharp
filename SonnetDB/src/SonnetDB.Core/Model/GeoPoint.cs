namespace SonnetDB.Model;

/// <summary>
/// 地理空间点，使用 WGS84 经纬度表示。
/// </summary>
/// <param name="Lat">纬度，范围 [-90, 90]。</param>
/// <param name="Lon">经度，范围 [-180, 180]。</param>
public readonly record struct GeoPoint(double Lat, double Lon)
{
    /// <summary>
    /// 验证经纬度范围并创建地理点。
    /// </summary>
    /// <param name="lat">纬度，范围 [-90, 90]。</param>
    /// <param name="lon">经度，范围 [-180, 180]。</param>
    /// <returns>验证通过的地理点。</returns>
    /// <exception cref="ArgumentOutOfRangeException">纬度或经度超出合法范围。</exception>
    public static GeoPoint Create(double lat, double lon)
    {
        if (double.IsNaN(lat) || lat < -90d || lat > 90d)
            throw new ArgumentOutOfRangeException(nameof(lat), lat, "纬度必须位于 [-90, 90]。");
        if (double.IsNaN(lon) || lon < -180d || lon > 180d)
            throw new ArgumentOutOfRangeException(nameof(lon), lon, "经度必须位于 [-180, 180]。");
        return new GeoPoint(lat, lon);
    }

    /// <inheritdoc/>
    public override string ToString() => $"POINT({Lat:G},{Lon:G})";
}
