using System.Security.Cryptography;
using System.Text;

namespace SonnetDB.Copilot;

/// <summary>
/// 单个技能文档的解析结果（PR #65）。
/// </summary>
internal sealed record SkillDocument(
    string Name,
    string Description,
    IReadOnlyList<string> Triggers,
    IReadOnlyList<string> RequiresTools,
    string Body,
    string Path,
    string RelativePath,
    DateTimeOffset LastWriteTimeUtc,
    string Fingerprint)
{
    /// <summary>
    /// 拼接用于嵌入的文本（name + description + triggers）。
    /// </summary>
    public string ToEmbeddingText()
    {
        var builder = new StringBuilder();
        builder.AppendLine(Name);
        if (!string.IsNullOrWhiteSpace(Description))
            builder.AppendLine(Description);
        if (Triggers.Count > 0)
            builder.AppendLine(string.Join(", ", Triggers));
        return builder.ToString().Trim();
    }
}

/// <summary>
/// 解析 markdown 文件头部的 YAML frontmatter（PR #65）。
/// 仅实现 SonnetDB 技能库所需的最小子集：单行字符串、单行列表 <c>[a, b]</c>、
/// 多行列表 <c>- item</c>，不依赖外部 YAML 库。
/// </summary>
internal static class SkillFrontmatter
{
    private const string Delimiter = "---";

    public static SkillDocument? TryParse(string fullPath, string rootDirectory)
    {
        var info = new FileInfo(fullPath);
        if (!info.Exists)
            return null;

        var raw = File.ReadAllText(fullPath).Replace("\r\n", "\n");
        if (!raw.StartsWith(Delimiter + "\n", StringComparison.Ordinal)
            && !raw.StartsWith(Delimiter + "\r\n", StringComparison.Ordinal))
        {
            return null;
        }

        var afterFirst = raw[(Delimiter.Length + 1)..];
        var endIndex = afterFirst.IndexOf("\n" + Delimiter, StringComparison.Ordinal);
        if (endIndex < 0)
            return null;

        var frontmatter = afterFirst[..endIndex];
        var bodyStart = endIndex + ("\n" + Delimiter).Length;
        var body = afterFirst[bodyStart..].TrimStart('\n', '\r').Trim();

        var values = ParseFrontmatter(frontmatter);
        if (!values.TryGetValue("name", out var nameValues) || nameValues.Count == 0
            || string.IsNullOrWhiteSpace(nameValues[0]))
        {
            return null;
        }

        var name = nameValues[0];
        var description = values.TryGetValue("description", out var descValues) && descValues.Count > 0
            ? descValues[0]
            : string.Empty;
        var triggers = values.TryGetValue("triggers", out var triggerValues) ? triggerValues : [];
        var requires = values.TryGetValue("requires_tools", out var toolValues) ? toolValues : [];

        var relative = Path.GetRelativePath(rootDirectory, fullPath).Replace('\\', '/');
        var fingerprint = ComputeFingerprint(raw);

        return new SkillDocument(
            Name: name.Trim(),
            Description: description.Trim(),
            Triggers: triggers,
            RequiresTools: requires,
            Body: body,
            Path: info.FullName,
            RelativePath: relative,
            LastWriteTimeUtc: new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            Fingerprint: fingerprint);
    }

    private static Dictionary<string, List<string>> ParseFrontmatter(string text)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var rawLine = lines[i];
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("  - ", StringComparison.Ordinal))
            {
                if (currentKey is null)
                    continue;
                var item = line.TrimStart().Substring(1).Trim();
                item = StripQuotes(item);
                if (!string.IsNullOrWhiteSpace(item))
                {
                    if (!result.TryGetValue(currentKey, out var bucket))
                    {
                        bucket = [];
                        result[currentKey] = bucket;
                    }
                    bucket.Add(item);
                }
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            currentKey = key;

            if (value.Length == 0)
            {
                if (!result.ContainsKey(key))
                    result[key] = [];
                continue;
            }

            if (value.StartsWith('[') && value.EndsWith(']'))
            {
                var inner = value[1..^1];
                var bucket = new List<string>();
                foreach (var part in inner.Split(','))
                {
                    var item = StripQuotes(part.Trim());
                    if (!string.IsNullOrWhiteSpace(item))
                        bucket.Add(item);
                }
                result[key] = bucket;
                continue;
            }

            result[key] = [StripQuotes(value)];
        }

        return result;
    }

    private static string StripQuotes(string value)
    {
        if (value.Length >= 2)
        {
            if ((value[0] == '"' && value[^1] == '"')
                || (value[0] == '\'' && value[^1] == '\''))
            {
                return value[1..^1];
            }
        }
        return value;
    }

    private static string ComputeFingerprint(string text)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(text), hash);
        return Convert.ToHexString(hash);
    }
}
