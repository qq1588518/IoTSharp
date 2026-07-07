namespace SonnetDB.Hosting;

/// <summary>
/// 静态资源 Content-Type 推断工具。
/// </summary>
internal static class StaticAssetContentTypes
{
    /// <summary>
    /// 根据扩展名猜测 Content-Type。
    /// </summary>
    /// <param name="path">资源路径。</param>
    /// <returns>HTTP Content-Type。</returns>
    public static string Guess(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".js" or ".mjs" => "application/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".map" => "application/json",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream",
        };
    }
}
