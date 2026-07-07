using System.Security.Cryptography;
using System.Text;

namespace SonnetDB.Auth;

/// <summary>
/// 密码哈希工具：PBKDF2-HMAC-SHA256。
/// </summary>
public static class PasswordHasher
{
    /// <summary>盐字节数。</summary>
    public const int SaltBytes = 16;

    /// <summary>派生密钥字节数。</summary>
    public const int HashBytes = 32;

    /// <summary>默认 PBKDF2 迭代次数。</summary>
    public const int DefaultIterations = 100_000;

    /// <summary>
    /// 为指定密码派生 (salt, hash, iterations) 三元组。
    /// </summary>
    /// <param name="password">明文密码（不可为 null 或空字符串）。</param>
    /// <param name="iterations">PBKDF2 迭代次数。</param>
    public static (byte[] Salt, byte[] Hash, int Iterations) Hash(string password, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1000);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        return (salt, hash, iterations);
    }

    /// <summary>
    /// 验证密码与 (salt, expectedHash, iterations) 是否匹配（恒定时比较）。
    /// </summary>
    public static bool Verify(string password, byte[] salt, byte[] expectedHash, int iterations)
    {
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(expectedHash);
        if (string.IsNullOrEmpty(password))
            return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        return CryptographicOperations.FixedTimeEquals(actual, expectedHash);
    }
}
