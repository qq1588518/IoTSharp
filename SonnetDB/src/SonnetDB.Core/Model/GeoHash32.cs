namespace SonnetDB.Model;

/// <summary>
/// 32-bit geohash 前缀编码工具，用于 GEOPOINT Block 级空间剪枝。
/// </summary>
public static class GeoHash32
{
    /// <summary>
    /// 将经纬度编码为 32-bit Morton geohash 前缀。
    /// </summary>
    /// <param name="point">地理点。</param>
    /// <returns>32-bit geohash 前缀；有效经纬度输入不会返回 0。</returns>
    public static uint Encode(GeoPoint point) => Encode(point.Lat, point.Lon);

    /// <summary>
    /// 将经纬度编码为 32-bit Morton geohash 前缀。
    /// </summary>
    /// <param name="lat">纬度，范围 [-90, 90]。</param>
    /// <param name="lon">经度，范围 [-180, 180]。</param>
    /// <returns>32-bit geohash 前缀；有效经纬度输入不会返回 0。</returns>
    public static uint Encode(double lat, double lon)
    {
        var normalizedLat = Math.Clamp((lat + 90d) / 180d, 0d, 1d);
        var normalizedLon = Math.Clamp((lon + 180d) / 360d, 0d, 1d);
        uint latBits = Quantize16(normalizedLat);
        uint lonBits = Quantize16(normalizedLon);
        uint hash = Interleave(lonBits, latBits);
        return hash == 0 ? 1u : hash;
    }

    /// <summary>
    /// 判断两个 32-bit geohash 闭区间是否重叠。
    /// </summary>
    /// <param name="leftMin">左区间下界。</param>
    /// <param name="leftMax">左区间上界。</param>
    /// <param name="rightMin">右区间下界。</param>
    /// <param name="rightMax">右区间上界。</param>
    /// <returns>重叠返回 true。</returns>
    public static bool Overlaps(uint leftMin, uint leftMax, uint rightMin, uint rightMax)
        => leftMin <= rightMax && leftMax >= rightMin;

    /// <summary>
    /// 计算圆形围栏的经纬度外包框。
    /// </summary>
    /// <param name="centerLat">中心纬度。</param>
    /// <param name="centerLon">中心经度。</param>
    /// <param name="radiusMeters">半径，单位米。</param>
    /// <returns>外包框。</returns>
    public static GeoBoundingBox BoundingBoxForCircle(double centerLat, double centerLon, double radiusMeters)
    {
        const double EarthRadiusMeters = 6_371_008.8;
        double latDelta = radiusMeters / EarthRadiusMeters * 180d / Math.PI;
        double cosLat = Math.Cos(centerLat * Math.PI / 180d);
        double lonDelta = Math.Abs(cosLat) < 1e-12 ? 180d : latDelta / Math.Abs(cosLat);
        return new GeoBoundingBox(
            Math.Clamp(centerLat - latDelta, -90d, 90d),
            Math.Clamp(centerLon - lonDelta, -180d, 180d),
            Math.Clamp(centerLat + latDelta, -90d, 90d),
            Math.Clamp(centerLon + lonDelta, -180d, 180d));
    }

    /// <summary>
    /// 计算矩形外包框对应的 geohash 闭区间近似。
    /// </summary>
    /// <param name="box">经纬度外包框。</param>
    /// <returns>geohash 最小值与最大值。</returns>
    public static (uint Min, uint Max) RangeForBox(GeoBoundingBox box)
    {
        uint h1 = Encode(box.LatMin, box.LonMin);
        uint h2 = Encode(box.LatMin, box.LonMax);
        uint h3 = Encode(box.LatMax, box.LonMin);
        uint h4 = Encode(box.LatMax, box.LonMax);
        uint min = Math.Min(Math.Min(h1, h2), Math.Min(h3, h4));
        uint max = Math.Max(Math.Max(h1, h2), Math.Max(h3, h4));
        return (min, max);
    }

    private static uint Quantize16(double normalized)
        => (uint)Math.Round(normalized * ushort.MaxValue, MidpointRounding.AwayFromZero);

    private static uint Interleave(uint lon, uint lat)
    {
        uint result = 0;
        for (int i = 0; i < 16; i++)
        {
            result |= ((lon >> i) & 1u) << (2 * i + 1);
            result |= ((lat >> i) & 1u) << (2 * i);
        }
        return result;
    }
}

/// <summary>
/// 地理空间经纬度外包框。
/// </summary>
/// <param name="LatMin">最小纬度。</param>
/// <param name="LonMin">最小经度。</param>
/// <param name="LatMax">最大纬度。</param>
/// <param name="LonMax">最大经度。</param>
public readonly record struct GeoBoundingBox(double LatMin, double LonMin, double LatMax, double LonMax);
