using System.Security.Cryptography;

namespace SonnetDB.Auth;

/// <summary>
/// API Token 工具：生成 256 位随机串（Base64Url 编码），并用 SHA-256 派生哈希做服务端比对。
/// 服务端只持久化哈希，从不保留明文 token。
/// </summary>
public static class ApiToken
{
    /// <summary>原始 token 字节数（256 位）。</summary>
    public const int TokenBytes = 32;

    /// <summary>
    /// 生成新的 Base64Url 编码 token（无填充）。
    /// </summary>
    public static string Generate()
    {
        Span<byte> buf = stackalloc byte[TokenBytes];
        RandomNumberGenerator.Fill(buf);
        return Base64Url(buf);
    }

    /// <summary>
    /// 计算 token 的 SHA-256 哈希，返回小写 16 进制字符串（64 字符）。
    /// </summary>
    /// <exception cref="ArgumentException">token 为 null 或空。</exception>
    public static string HashHex(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token), hash);
        return Convert.ToHexStringLower(hash);
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes)
    {
        var s = Convert.ToBase64String(bytes);
        // Base64Url：去填充、'+' → '-'、'/' → '_'
        return s.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
