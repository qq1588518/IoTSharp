using System.Text.RegularExpressions;

namespace SonnetDB.Copilot;

/// <summary>
/// Provisioning 场景中的单个 schema 列定义。
/// </summary>
/// <param name="Name">列名。</param>
/// <param name="Type">SonnetDB SQL 类型（TAG 固定为 STRING，FIELD 使用 FLOAT / INT / BOOL / STRING / VECTOR(N)）。</param>
/// <param name="IsTag">是否为 TAG 列。</param>
internal sealed record CopilotProvisionColumn(string Name, string Type, bool IsTag);

/// <summary>
/// 从自然语言中抽取出的“建库 + 建表 + 定义字段”意图。
/// </summary>
/// <param name="DatabaseName">目标数据库名。</param>
/// <param name="MeasurementName">目标 measurement 名。</param>
/// <param name="Tags">TAG 列集合。</param>
/// <param name="Fields">FIELD 列集合。</param>
/// <param name="ExecuteNow">是否倾向于立即执行（否则只起草 SQL）。</param>
internal sealed record CopilotProvisionIntent(
    string DatabaseName,
    string? MeasurementName,
    IReadOnlyList<CopilotProvisionColumn> Tags,
    IReadOnlyList<CopilotProvisionColumn> Fields,
    bool ExecuteNow)
{
    public bool CreateMeasurement => !string.IsNullOrWhiteSpace(MeasurementName);
}

/// <summary>
/// 将“帮我新建一个仓库，并把这些指标建成表”类自然语言拆解为结构化 provisioning 意图。
/// </summary>
internal static partial class CopilotProvisioning
{
    private static readonly Regex ExplicitDatabaseNameRegex = new(
        @"(?:数据库|仓库|库)\s*(?:叫|名为|叫做|叫作)?\s*([A-Za-z][A-Za-z0-9_]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ExplicitMeasurementNameRegex = new(
        @"(?:measurement|表)\s*(?:叫|名为|叫做|叫作)?\s*([A-Za-z][A-Za-z0-9_]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool LooksLikeProvisioningRequest(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var lowered = message.Trim().ToLowerInvariant();
        var asksToCreate = lowered.Contains("新建", StringComparison.Ordinal)
            || lowered.Contains("创建", StringComparison.Ordinal)
            || lowered.Contains("建一个", StringComparison.Ordinal)
            || lowered.Contains("建一套", StringComparison.Ordinal)
            || lowered.Contains("create database", StringComparison.Ordinal);
        var mentionsDatabase = lowered.Contains("数据库", StringComparison.Ordinal)
            || lowered.Contains("仓库", StringComparison.Ordinal)
            || lowered.Contains("库", StringComparison.Ordinal)
            || lowered.Contains("database", StringComparison.Ordinal);

        return asksToCreate && mentionsDatabase;
    }

    public static CopilotProvisionIntent? TryExtractIntent(string? message)
    {
        if (!LooksLikeProvisioningRequest(message))
            return null;

        var text = message!.Trim();
        var lowered = text.ToLowerInvariant();
        var fields = BuildFields(text, lowered);
        var createMeasurement = fields.Count > 0
            || lowered.Contains("建表", StringComparison.Ordinal)
            || lowered.Contains("表", StringComparison.Ordinal)
            || lowered.Contains("measurement", StringComparison.Ordinal);
        var measurementName = createMeasurement ? ResolveMeasurementName(text, lowered) : null;
        var tags = createMeasurement
            ? new[] { new CopilotProvisionColumn("host", "STRING", IsTag: true) }
            : Array.Empty<CopilotProvisionColumn>();

        return new CopilotProvisionIntent(
            DatabaseName: ResolveDatabaseName(text, lowered),
            MeasurementName: measurementName,
            Tags: tags,
            Fields: fields,
            ExecuteNow: ShouldExecuteNow(lowered));
    }

    public static string BuildCreateDatabaseSql(CopilotProvisionIntent intent)
        => $"CREATE DATABASE {intent.DatabaseName}";

    public static string? BuildCreateMeasurementSql(CopilotProvisionIntent intent)
    {
        if (!intent.CreateMeasurement || string.IsNullOrWhiteSpace(intent.MeasurementName))
            return null;

        var columns = new List<string>(intent.Tags.Count + intent.Fields.Count);
        columns.AddRange(intent.Tags.Select(static tag => $"{tag.Name} TAG"));
        columns.AddRange(intent.Fields.Select(static field => $"{field.Name} FIELD {field.Type}"));

        return $"CREATE MEASUREMENT {intent.MeasurementName} ({string.Join(", ", columns)})";
    }

    private static string ResolveDatabaseName(string message, string lowered)
    {
        var explicitName = TryMatchIdentifier(ExplicitDatabaseNameRegex, message);
        if (explicitName is not null)
            return explicitName;

        if (LooksLikeComputerPerformance(lowered))
            return "computer_perf";
        if (LooksLikeEnvironmentTelemetry(lowered))
            return "sensor_metrics";
        return "metrics";
    }

    private static string ResolveMeasurementName(string message, string lowered)
    {
        var explicitName = TryMatchIdentifier(ExplicitMeasurementNameRegex, message);
        if (explicitName is not null)
            return explicitName;

        if (LooksLikeComputerPerformance(lowered))
            return "host_perf";
        if (LooksLikeEnvironmentTelemetry(lowered))
            return "environment";
        return "metrics";
    }

    private static IReadOnlyList<CopilotProvisionColumn> BuildFields(string message, string lowered)
    {
        var fields = new List<CopilotProvisionColumn>(4);

        AddFieldIf(fields,
            (lowered.Contains("cpu", StringComparison.Ordinal) || lowered.Contains("处理器", StringComparison.Ordinal))
                && ContainsUsageIntent(lowered),
            "cpu_usage",
            "FLOAT");

        AddFieldIf(fields,
            lowered.Contains("内存", StringComparison.Ordinal) && ContainsUsageIntent(lowered),
            "memory_usage",
            "FLOAT");

        AddFieldIf(fields,
            (lowered.Contains("cpu", StringComparison.Ordinal) || lowered.Contains("处理器", StringComparison.Ordinal))
                && ContainsTemperatureIntent(lowered),
            "cpu_temp_celsius",
            "FLOAT");

        AddFieldIf(fields,
            !fields.Any(static field => string.Equals(field.Name, "temperature", StringComparison.OrdinalIgnoreCase))
                && !fields.Any(static field => string.Equals(field.Name, "cpu_temp_celsius", StringComparison.OrdinalIgnoreCase))
                && ContainsTemperatureIntent(lowered),
            "temperature",
            "FLOAT");

        AddFieldIf(fields,
            lowered.Contains("湿度", StringComparison.Ordinal) || ContainsIdentifierToken(message, "humidity"),
            "humidity",
            "FLOAT");

        if (fields.Count == 0 && LooksLikeComputerPerformance(lowered))
        {
            fields.Add(new CopilotProvisionColumn("cpu_usage", "FLOAT", IsTag: false));
            fields.Add(new CopilotProvisionColumn("memory_usage", "FLOAT", IsTag: false));
            fields.Add(new CopilotProvisionColumn("cpu_temp_celsius", "FLOAT", IsTag: false));
        }

        return fields;
    }

    private static void AddFieldIf(List<CopilotProvisionColumn> fields, bool condition, string name, string type)
    {
        if (!condition)
            return;
        if (fields.Any(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase)))
            return;
        fields.Add(new CopilotProvisionColumn(name, type, IsTag: false));
    }

    private static bool ContainsUsageIntent(string lowered)
        => lowered.Contains("使用率", StringComparison.Ordinal)
            || lowered.Contains("占用率", StringComparison.Ordinal)
            || lowered.Contains("usage", StringComparison.Ordinal);

    private static bool ContainsTemperatureIntent(string lowered)
        => lowered.Contains("温度", StringComparison.Ordinal)
            || lowered.Contains("temp", StringComparison.Ordinal)
            || lowered.Contains("temperature", StringComparison.Ordinal);

    private static bool LooksLikeComputerPerformance(string lowered)
        => (lowered.Contains("电脑", StringComparison.Ordinal)
                || lowered.Contains("计算机", StringComparison.Ordinal)
                || lowered.Contains("主机", StringComparison.Ordinal)
                || lowered.Contains("系统", StringComparison.Ordinal)
                || lowered.Contains("host", StringComparison.Ordinal))
            && (lowered.Contains("性能", StringComparison.Ordinal)
                || lowered.Contains("perf", StringComparison.Ordinal)
                || lowered.Contains("监控", StringComparison.Ordinal)
                || lowered.Contains("指标", StringComparison.Ordinal)
                || lowered.Contains("usage", StringComparison.Ordinal));

    private static bool LooksLikeEnvironmentTelemetry(string lowered)
        => lowered.Contains("温度", StringComparison.Ordinal)
            || lowered.Contains("湿度", StringComparison.Ordinal)
            || lowered.Contains("humidity", StringComparison.Ordinal)
            || lowered.Contains("temperature", StringComparison.Ordinal);

    private static bool ShouldExecuteNow(string lowered)
    {
        if (lowered.Contains("sql", StringComparison.Ordinal)
            || lowered.Contains("语句", StringComparison.Ordinal)
            || lowered.Contains("怎么", StringComparison.Ordinal)
            || lowered.Contains("如何", StringComparison.Ordinal)
            || lowered.Contains("示例", StringComparison.Ordinal))
        {
            return false;
        }

        return lowered.Contains("帮我", StringComparison.Ordinal)
            || lowered.Contains("请", StringComparison.Ordinal)
            || lowered.Contains("直接", StringComparison.Ordinal)
            || lowered.Contains("立即", StringComparison.Ordinal)
            || lowered.Contains("现在", StringComparison.Ordinal)
            || lowered.Contains("新建", StringComparison.Ordinal)
            || lowered.Contains("创建", StringComparison.Ordinal);
    }

    private static string? TryMatchIdentifier(Regex regex, string text)
    {
        var match = regex.Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool ContainsIdentifierToken(string text, string token)
        => text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
}
