using System.Text.Json;

namespace SonnetDB.Cli;

internal sealed class CliProfileStore
{
    private readonly string _configPath;

    public CliProfileStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".sndb",
            "profiles.json"))
    {
    }

    internal CliProfileStore(string configPath)
    {
        _configPath = configPath;
    }

    public CliProfilesDocument Load()
    {
        if (!File.Exists(_configPath))
            return new CliProfilesDocument();

        using var stream = File.OpenRead(_configPath);
        return JsonSerializer.Deserialize(stream, CliJsonContext.Default.CliProfilesDocument) ?? new CliProfilesDocument();
    }

    // ── Remote ────────────────────────────────────────────────────────────────

    public CliRemoteProfile? Get(string profileName)
    {
        var document = Load();
        return FindRemote(document, profileName);
    }

    public void Upsert(CliRemoteProfile profile)
    {
        var document = Load();
        var profiles = document.Profiles
            .Where(p => !Eq(p.Name, profile.Name))
            .ToList();
        profiles.Add(profile);
        Save(document with { Profiles = profiles });
    }

    public bool Remove(string profileName)
    {
        var document = Load();
        var profiles = document.Profiles
            .Where(p => !Eq(p.Name, profileName))
            .ToList();

        if (profiles.Count == document.Profiles.Count)
            return false;

        var defaultProfile = Eq(document.DefaultProfile, profileName) ? null : document.DefaultProfile;
        Save(document with { Profiles = profiles, DefaultProfile = defaultProfile });
        return true;
    }

    // ── Local ─────────────────────────────────────────────────────────────────

    public CliLocalProfile? GetLocal(string profileName)
    {
        var document = Load();
        return FindLocal(document, profileName);
    }

    public void UpsertLocal(CliLocalProfile profile)
    {
        var document = Load();
        var profiles = document.LocalProfiles
            .Where(p => !Eq(p.Name, profile.Name))
            .ToList();
        profiles.Add(profile);
        Save(document with { LocalProfiles = profiles.Count > 0 ? profiles : [] });
    }

    public bool RemoveLocal(string profileName)
    {
        var document = Load();
        var profiles = document.LocalProfiles
            .Where(p => !Eq(p.Name, profileName))
            .ToList();

        if (profiles.Count == document.LocalProfiles.Count)
            return false;

        var defaultProfile = Eq(document.DefaultProfile, profileName) ? null : document.DefaultProfile;
        Save(document with
        {
            LocalProfiles = profiles.Count > 0 ? profiles : [],
            DefaultProfile = defaultProfile,
        });
        return true;
    }

    // ── Default / unified ─────────────────────────────────────────────────────

    public void SetDefault(string profileName)
    {
        var document = Load();
        var existsRemote = document.Profiles.Any(p => Eq(p.Name, profileName));
        var existsLocal = document.LocalProfiles.Any(p => Eq(p.Name, profileName));
        if (!existsRemote && !existsLocal)
            throw new CliUsageException($"未找到 profile '{profileName}'。");

        Save(document with { DefaultProfile = profileName });
    }

    public (CliLocalProfile? Local, CliRemoteProfile? Remote) GetDefault()
    {
        var document = Load();
        if (string.IsNullOrWhiteSpace(document.DefaultProfile))
            return (null, null);

        return (FindLocal(document, document.DefaultProfile), FindRemote(document, document.DefaultProfile));
    }

    public (CliLocalProfile? Local, CliRemoteProfile? Remote) GetByName(string profileName)
    {
        var document = Load();
        return (FindLocal(document, profileName), FindRemote(document, profileName));
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private void Save(CliProfilesDocument document)
    {
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var stream = File.Create(_configPath);
        JsonSerializer.Serialize(stream, document, CliJsonContext.Default.CliProfilesDocument);
    }

    private static CliRemoteProfile? FindRemote(CliProfilesDocument doc, string? name)
        => name is null ? null : doc.Profiles.FirstOrDefault(p => Eq(p.Name, name));

    private static CliLocalProfile? FindLocal(CliProfilesDocument doc, string? name)
        => name is null ? null : doc.LocalProfiles.FirstOrDefault(p => Eq(p.Name, name));

    private static bool Eq(string? a, string? b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

internal sealed record CliProfilesDocument(
    string? DefaultProfile = null,
    List<CliRemoteProfile>? Profiles = null,
    List<CliLocalProfile>? LocalProfiles = null)
{
    public List<CliRemoteProfile> Profiles { get; init; } = Profiles ?? [];
    // LocalProfiles stays nullable so WhenWritingNull omits the key when empty
    public List<CliLocalProfile> LocalProfiles { get; init; } = LocalProfiles ?? [];
}

internal sealed record CliRemoteProfile(
    string Name,
    string BaseUrl,
    string Database,
    string? Token,
    int Timeout);

internal sealed record CliLocalProfile(
    string Name,
    string Path);
