using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SonnetDB.Auth;

/// <summary>
/// 跨进程文件原子写工具：unique tempfile + fsync + File.Move(overwrite=true)。
/// 唯一临时文件名避免同一路径的多个存储实例并发持久化时互相抢占 <c>.tmp</c>。
/// </summary>
internal static class AtomicJsonFile
{
    /// <summary>
    /// 反序列化 JSON 文件；若不存在或为空，返回 <paramref name="initialFactory"/>() 的结果。
    /// </summary>
    /// <param name="path">文件绝对路径。</param>
    /// <param name="typeInfo">System.Text.Json 源生成器 TypeInfo，避免反射。</param>
    /// <param name="initialFactory">文件不存在时调用，构造默认值。</param>
    public static T Read<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, Func<T> initialFactory)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(initialFactory);

        if (!File.Exists(path))
            return initialFactory();

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (fs.Length == 0)
            return initialFactory();
        var result = JsonSerializer.Deserialize(fs, typeInfo);
        return result ?? initialFactory();
    }

    /// <summary>
    /// 原子写 JSON 文件：先写到唯一 <c>.tmp</c>，flush + fsync，再 <see cref="File.Move(string, string, bool)"/> 覆盖目标。
    /// </summary>
    public static void Write<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(typeInfo);

        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(fs, value, typeInfo);
                fs.Flush(flushToDisk: true);
            }
            MoveWithRetry(tmp, path);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    private static void MoveWithRetry(string sourceFileName, string destFileName)
    {
        const int MaxAttempts = 4;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(sourceFileName, destFileName, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < MaxAttempts)
            {
                Thread.Sleep(attempt * 10);
            }
            catch (UnauthorizedAccessException) when (attempt < MaxAttempts)
            {
                Thread.Sleep(attempt * 10);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
