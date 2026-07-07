namespace SonnetDB.Copilot;

/// <summary>
/// 扫描技能根目录，递归收集所有合法 markdown 技能文件（PR #65）。
/// </summary>
internal sealed class SkillSourceScanner
{
    public IReadOnlyList<SkillDocument> Scan(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var resolved = ResolveRoot(root);
        if (resolved is null || !Directory.Exists(resolved))
            return [];

        var skills = new Dictionary<string, SkillDocument>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(resolved, "*.md", SearchOption.AllDirectories))
        {
            var skill = SkillFrontmatter.TryParse(file, resolved);
            if (skill is null)
                continue;

            // 同名技能取后扫描的覆盖；目录顺序由 Directory.EnumerateFiles 决定，
            // 与 docs scanner 一致：以 OrdinalIgnoreCase 的 path 顺序为准。
            skills[skill.Name] = skill;
        }

        return skills.Values
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveRoot(string raw)
    {
        var normalized = raw.Trim();
        if (Path.IsPathRooted(normalized))
            return Path.GetFullPath(normalized);

        foreach (var seed in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(seed))
                continue;
            var current = Path.GetFullPath(seed);
            for (var depth = 0; depth < 8 && !string.IsNullOrEmpty(current); depth++)
            {
                var candidate = Path.GetFullPath(normalized, current);
                if (Directory.Exists(candidate))
                    return candidate;

                var parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                    break;
                current = parent;
            }
        }

        return null;
    }
}
