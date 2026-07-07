using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace SonnetDB.Copilot;

/// <summary>
/// 加载嵌入式 Copilot prompt 模板。模板以 .md 文件形式放在 <c>SonnetDB/Copilot/Prompts/</c> 目录下，
/// 通过 <c>&lt;EmbeddedResource&gt;</c> 嵌入程序集，运行时通过 <see cref="Assembly.GetManifestResourceStream(string)"/>
/// 读取并缓存。支持 <c>{{name}}</c> 形式的占位符替换。
/// </summary>
internal static class PromptTemplates
{
    private const string ResourcePrefix = "SonnetDB.Copilot.Prompts.";
    private const string ResourceSuffix = ".md";

    private static readonly Assembly _assembly = typeof(PromptTemplates).Assembly;
    private static readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// 加载指定名称的模板原文（带缓存，按程序集生命周期单次读取）。
    /// </summary>
    /// <param name="name">模板文件基名，例如 <c>"sql-gen"</c> 对应 <c>Prompts/sql-gen.md</c>。</param>
    /// <exception cref="InvalidOperationException">模板不存在时抛出（部署/打包遗漏的硬错误）。</exception>
    public static string Load(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _cache.GetOrAdd(name, ReadResource);
    }

    /// <summary>
    /// 加载模板并按 <paramref name="variables"/> 替换 <c>{{key}}</c> 占位符。
    /// 未提供的占位符保持原样（便于在调用方观察缺失）。
    /// </summary>
    public static string Render(string name, IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        var template = Load(name);
        if (variables.Count == 0)
            return template;

        var sb = new StringBuilder(template);
        foreach (var (key, value) in variables)
            sb.Replace("{{" + key + "}}", value ?? string.Empty);
        return sb.ToString();
    }

    private static string ReadResource(string name)
    {
        var resourceName = ResourcePrefix + name + ResourceSuffix;
        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"找不到嵌入式 prompt 资源 '{resourceName}'，请检查 SonnetDB.csproj 中的 <EmbeddedResource> 声明。");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
