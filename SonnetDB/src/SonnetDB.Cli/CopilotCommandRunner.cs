using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SonnetDB.Cli;

/// <summary>
/// 处理 <c>sndb copilot ...</c> 子命令（PR #64）。
/// 当前仅支持 <c>copilot ingest</c>，通过 HTTP 调用服务端
/// <c>POST /v1/copilot/docs/ingest</c>。
/// </summary>
internal sealed class CopilotCommandRunner
{
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public CopilotCommandRunner(TextWriter output, TextWriter error)
    {
        _output = output;
        _error = error;
    }

    public int Run(IReadOnlyList<string> args)
    {
        if (args.Count < 2)
            throw new CliUsageException(BuildHelp());

        var sub = args[1].ToLowerInvariant();
        return sub switch
        {
            "ingest" => RunIngest(args),
            "skills" => RunSkills(args),
            "help" or "--help" or "-h" => Help(),
            _ => throw new CliUsageException($"未知 copilot 子命令 '{args[1]}'。\n{BuildHelp()}"),
        };
    }

    private int Help()
    {
        _output.WriteLine(BuildHelp());
        return ExitCodes.Success;
    }

    private int RunIngest(IReadOnlyList<string> args)
    {
        var endpoint = ReadEnv("SONNETDB_COPILOT_URL") ?? "http://127.0.0.1:5080";
        var token = ReadEnv("SONNETDB_COPILOT_TOKEN");
        var roots = new List<string>();
        var force = false;
        var dryRun = false;
        var timeoutSeconds = 600;

        for (var i = 2; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--root" or "-r":
                    roots.Add(RequireValue(args, ref i, "--root"));
                    break;
                case "--endpoint" or "--url":
                    endpoint = RequireValue(args, ref i, "--endpoint");
                    break;
                case "--token" or "-t":
                    token = RequireValue(args, ref i, "--token");
                    break;
                case "--force":
                    force = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--timeout":
                    if (!int.TryParse(RequireValue(args, ref i, "--timeout"), out timeoutSeconds) || timeoutSeconds <= 0)
                        throw new CliUsageException("--timeout 必须为正整数（秒）。");
                    break;
                case "--help" or "-h":
                    _output.WriteLine(BuildHelp());
                    return ExitCodes.Success;
                default:
                    throw new CliUsageException($"未知参数 '{args[i]}'。\n{BuildHelp()}");
            }
        }

        if (string.IsNullOrWhiteSpace(endpoint))
            throw new CliUsageException("必须通过 --endpoint <url> 或 SONNETDB_COPILOT_URL 环境变量指定服务端地址。");

        var request = new CliCopilotIngestRequest(
            Roots: roots.Count == 0 ? null : roots,
            Force: force,
            DryRun: dryRun);

        var url = endpoint.TrimEnd('/') + "/v1/copilot/docs/ingest";
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var content = JsonContent.Create(request, CliJsonContext.Default.CliCopilotIngestRequest);
        HttpResponseMessage response;
        try
        {
            response = client.PostAsync(url, content).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _error.WriteLine($"调用 {url} 失败: {ex.Message}");
            return ExitCodes.ExecutionFailed;
        }

        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                var err = JsonSerializer.Deserialize(body, CliJsonContext.Default.CliCopilotErrorResponse);
                _error.WriteLine($"服务端返回 {(int)response.StatusCode}: {err?.Error} {err?.Message}");
            }
            catch
            {
                _error.WriteLine($"服务端返回 {(int)response.StatusCode}: {body}");
            }

            return ExitCodes.ExecutionFailed;
        }

        var resp = JsonSerializer.Deserialize(body, CliJsonContext.Default.CliCopilotIngestResponse);
        if (resp is null)
        {
            _error.WriteLine("服务端响应为空。");
            return ExitCodes.ExecutionFailed;
        }

        _output.WriteLine($"扫描文件: {resp.ScannedFiles}");
        _output.WriteLine($"重新索引: {resp.IndexedFiles}");
        _output.WriteLine($"跳过未变: {resp.SkippedFiles}");
        _output.WriteLine($"清理失效: {resp.DeletedFiles}");
        _output.WriteLine($"写入分块: {resp.WrittenChunks}");
        _output.WriteLine($"DryRun  : {resp.DryRun}");
        _output.WriteLine($"耗时    : {resp.ElapsedMilliseconds:F1} ms");
        return ExitCodes.Success;
    }

    private static string RequireValue(IReadOnlyList<string> args, ref int index, string flag)
    {
        if (index + 1 >= args.Count)
            throw new CliUsageException($"{flag} 缺少参数值。");
        return args[++index];
    }

    private static string? ReadEnv(string key)
        => Environment.GetEnvironmentVariable(key);

    private int RunSkills(IReadOnlyList<string> args)
    {
        if (args.Count < 3)
            throw new CliUsageException(BuildHelp());

        var sub = args[2].ToLowerInvariant();
        return sub switch
        {
            "reload" => RunSkillsReload(args),
            "list" => RunSkillsList(args),
            "show" => RunSkillsShow(args),
            "help" or "--help" or "-h" => Help(),
            _ => throw new CliUsageException($"未知 copilot skills 子命令 '{args[2]}'。\n{BuildHelp()}"),
        };
    }

    private int RunSkillsReload(IReadOnlyList<string> args)
    {
        var endpoint = ReadEnv("SONNETDB_COPILOT_URL") ?? "http://127.0.0.1:5080";
        var token = ReadEnv("SONNETDB_COPILOT_TOKEN");
        string? root = null;
        var force = false;
        var dryRun = false;
        var timeoutSeconds = 600;

        for (var i = 3; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--root" or "-r":
                    root = RequireValue(args, ref i, "--root");
                    break;
                case "--endpoint" or "--url":
                    endpoint = RequireValue(args, ref i, "--endpoint");
                    break;
                case "--token" or "-t":
                    token = RequireValue(args, ref i, "--token");
                    break;
                case "--force":
                    force = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--timeout":
                    if (!int.TryParse(RequireValue(args, ref i, "--timeout"), out timeoutSeconds) || timeoutSeconds <= 0)
                        throw new CliUsageException("--timeout 必须为正整数（秒）。");
                    break;
                case "--help" or "-h":
                    _output.WriteLine(BuildHelp());
                    return ExitCodes.Success;
                default:
                    throw new CliUsageException($"未知参数 '{args[i]}'。\n{BuildHelp()}");
            }
        }

        var request = new CliCopilotSkillsReloadRequest(root, force, dryRun);
        var url = endpoint.TrimEnd('/') + "/v1/copilot/skills/reload";
        using var client = CreateClient(timeoutSeconds, token);
        var content = JsonContent.Create(request, CliJsonContext.Default.CliCopilotSkillsReloadRequest);

        HttpResponseMessage response;
        try
        {
            response = client.PostAsync(url, content).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _error.WriteLine($"调用 {url} 失败: {ex.Message}");
            return ExitCodes.ExecutionFailed;
        }

        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            return WriteHttpError(response, body);

        var resp = JsonSerializer.Deserialize(body, CliJsonContext.Default.CliCopilotSkillsReloadResponse);
        if (resp is null)
        {
            _error.WriteLine("服务端响应为空。");
            return ExitCodes.ExecutionFailed;
        }

        _output.WriteLine($"扫描技能: {resp.ScannedSkills}");
        _output.WriteLine($"重新索引: {resp.IndexedSkills}");
        _output.WriteLine($"跳过未变: {resp.SkippedSkills}");
        _output.WriteLine($"清理失效: {resp.DeletedSkills}");
        _output.WriteLine($"DryRun  : {resp.DryRun}");
        _output.WriteLine($"耗时    : {resp.ElapsedMilliseconds:F1} ms");
        return ExitCodes.Success;
    }

    private int RunSkillsList(IReadOnlyList<string> args)
    {
        var endpoint = ReadEnv("SONNETDB_COPILOT_URL") ?? "http://127.0.0.1:5080";
        var token = ReadEnv("SONNETDB_COPILOT_TOKEN");
        var timeoutSeconds = 60;

        for (var i = 3; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--endpoint" or "--url":
                    endpoint = RequireValue(args, ref i, "--endpoint");
                    break;
                case "--token" or "-t":
                    token = RequireValue(args, ref i, "--token");
                    break;
                case "--help" or "-h":
                    _output.WriteLine(BuildHelp());
                    return ExitCodes.Success;
                default:
                    throw new CliUsageException($"未知参数 '{args[i]}'。\n{BuildHelp()}");
            }
        }

        var url = endpoint.TrimEnd('/') + "/v1/copilot/skills/list";
        using var client = CreateClient(timeoutSeconds, token);

        HttpResponseMessage response;
        try
        {
            response = client.GetAsync(url).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _error.WriteLine($"调用 {url} 失败: {ex.Message}");
            return ExitCodes.ExecutionFailed;
        }

        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            return WriteHttpError(response, body);

        var resp = JsonSerializer.Deserialize(body, CliJsonContext.Default.CliCopilotSkillsListResponse);
        if (resp is null || resp.Skills.Count == 0)
        {
            _output.WriteLine("（暂无已注册技能。可以先运行 sndb copilot skills reload。）");
            return ExitCodes.Success;
        }

        foreach (var skill in resp.Skills)
        {
            _output.WriteLine($"- {skill.Name}");
            if (!string.IsNullOrWhiteSpace(skill.Description))
                _output.WriteLine($"    {skill.Description}");
            if (skill.Triggers.Count > 0)
                _output.WriteLine($"    triggers: {string.Join(", ", skill.Triggers)}");
        }

        return ExitCodes.Success;
    }

    private int RunSkillsShow(IReadOnlyList<string> args)
    {
        if (args.Count < 4)
            throw new CliUsageException("sndb copilot skills show 需要 <name> 参数。");

        var name = args[3];
        var endpoint = ReadEnv("SONNETDB_COPILOT_URL") ?? "http://127.0.0.1:5080";
        var token = ReadEnv("SONNETDB_COPILOT_TOKEN");
        var timeoutSeconds = 60;

        for (var i = 4; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--endpoint" or "--url":
                    endpoint = RequireValue(args, ref i, "--endpoint");
                    break;
                case "--token" or "-t":
                    token = RequireValue(args, ref i, "--token");
                    break;
                case "--help" or "-h":
                    _output.WriteLine(BuildHelp());
                    return ExitCodes.Success;
                default:
                    throw new CliUsageException($"未知参数 '{args[i]}'。\n{BuildHelp()}");
            }
        }

        var url = endpoint.TrimEnd('/') + "/v1/copilot/skills/" + Uri.EscapeDataString(name);
        using var client = CreateClient(timeoutSeconds, token);

        HttpResponseMessage response;
        try
        {
            response = client.GetAsync(url).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _error.WriteLine($"调用 {url} 失败: {ex.Message}");
            return ExitCodes.ExecutionFailed;
        }

        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            return WriteHttpError(response, body);

        var resp = JsonSerializer.Deserialize(body, CliJsonContext.Default.CliCopilotSkillLoadResponse);
        if (resp is null)
        {
            _error.WriteLine("服务端响应为空。");
            return ExitCodes.ExecutionFailed;
        }

        _output.WriteLine($"# {resp.Name}");
        if (!string.IsNullOrWhiteSpace(resp.Description))
            _output.WriteLine(resp.Description);
        if (resp.Triggers.Count > 0)
            _output.WriteLine($"triggers: {string.Join(", ", resp.Triggers)}");
        if (resp.RequiresTools.Count > 0)
            _output.WriteLine($"requires_tools: {string.Join(", ", resp.RequiresTools)}");
        if (!string.IsNullOrWhiteSpace(resp.Source))
            _output.WriteLine($"source: {resp.Source}");
        _output.WriteLine();
        _output.WriteLine(resp.Body);
        return ExitCodes.Success;
    }

    private static HttpClient CreateClient(int timeoutSeconds, string? token)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private int WriteHttpError(HttpResponseMessage response, string body)
    {
        try
        {
            var err = JsonSerializer.Deserialize(body, CliJsonContext.Default.CliCopilotErrorResponse);
            _error.WriteLine($"服务端返回 {(int)response.StatusCode}: {err?.Error} {err?.Message}");
        }
        catch
        {
            _error.WriteLine($"服务端返回 {(int)response.StatusCode}: {body}");
        }
        return ExitCodes.ExecutionFailed;
    }

    private static string BuildHelp()
        => """
sndb copilot — Copilot 知识库管理（PR #64 / PR #65）

用法:
  sndb copilot ingest [--root <dir>]... [--endpoint <url>] [--token <bearer>] [--force] [--dry-run] [--timeout <sec>]
  sndb copilot skills reload [--root <dir>] [--endpoint <url>] [--token <bearer>] [--force] [--dry-run]
  sndb copilot skills list   [--endpoint <url>]
  sndb copilot skills show <name> [--endpoint <url>]

参数:
  --root, -r        指定根目录（ingest 可重复多次；skills 仅取一个）。为空时使用服务端配置。
  --endpoint, --url 服务端地址（默认 http://127.0.0.1:5080，或环境变量 SONNETDB_COPILOT_URL）。
  --token, -t       Bearer token（admin），可通过 SONNETDB_COPILOT_TOKEN 提供。
  --force           忽略 mtime/fingerprint，强制重新嵌入。
  --dry-run         仅扫描和切片，不写入向量库。
  --timeout         HTTP 超时秒数，默认 600。

示例:
  sndb copilot ingest --root ./docs
  sndb copilot skills reload --force
  sndb copilot skills list
  sndb copilot skills show query-aggregation
""";
}
