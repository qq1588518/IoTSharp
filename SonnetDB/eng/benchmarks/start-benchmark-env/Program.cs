using System.Diagnostics;

return await BenchmarkEnvironmentScript.RunAsync(args);

static class BenchmarkEnvironmentScript
{
    private static readonly string[] ContainerNames =
    [
        "sndb-bench-server",
        "sndb-bench-influxdb",
        "sndb-bench-tdengine",
        "sndb-bench-iotdb",
        "sndb-bench-timescaledb"
    ];

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = StartOptions.Parse(args);
            if (options.ShowHelp)
            {
                PrintUsage();
                return 0;
            }

            var repoRoot = FindRepoRoot();
            var composeFile = Path.Combine(repoRoot, "tests", "SonnetDB.Benchmarks", "docker", "docker-compose.yml");
            var compose = await ResolveDockerComposeAsync();

            if (options.Down)
            {
                var downArgs = new List<string> { "down", "--remove-orphans" };
                if (options.RemoveVolumes)
                {
                    downArgs.Add("-v");
                }

                await RunComposeAsync(compose, composeFile, repoRoot, downArgs);
                return 0;
            }

            if (options.ResetVolumes)
            {
                await RunComposeAsync(compose, composeFile, repoRoot, ["down", "-v", "--remove-orphans"]);
            }

            if (!options.SkipBuild)
            {
                await RunComposeAsync(compose, composeFile, repoRoot, ["build"]);
            }

            if (!options.SkipUp)
            {
                await RunComposeAsync(compose, composeFile, repoRoot, ["up", "-d"]);
            }

            await WaitForContainersAsync(compose, composeFile, repoRoot, options.TimeoutSeconds);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
        Usage:
          dotnet run --project eng/benchmarks/start-benchmark-env/start-benchmark-env.csproj -- [options]

        Options:
          --skip-build        Skip docker compose build.
          --skip-up           Skip docker compose up -d; only wait for existing containers.
          --reset-volumes     Run docker compose down -v before build/up.
          --timeout <sec>     Health wait timeout. Default: 240.
          --down              Stop compose services and exit.
          --remove-volumes    With --down, also remove compose volumes.
          -h, --help          Show this help.
        """);
    }

    private static async Task<ComposeCommand> ResolveDockerComposeAsync()
    {
        if (!await TryRunQuietAsync("docker", ["--version"]))
        {
            throw new InvalidOperationException("Docker CLI was not found. Install Docker Desktop or Docker Engine before running benchmarks.");
        }

        if (await TryRunQuietAsync("docker", ["compose", "version"]))
        {
            return new ComposeCommand("docker", ["compose"], "docker compose");
        }

        if (await TryRunQuietAsync("docker-compose", ["version"]))
        {
            return new ComposeCommand("docker-compose", [], "docker-compose");
        }

        throw new InvalidOperationException("Docker Compose was not found. Install Docker Compose before running benchmarks.");
    }

    private static async Task RunComposeAsync(
        ComposeCommand compose,
        string composeFile,
        string workingDirectory,
        IReadOnlyList<string> arguments)
    {
        var args = new List<string>(compose.Prefix)
        {
            "-f",
            composeFile
        };
        args.AddRange(arguments);
        await RunCommandAsync(compose.FileName, args, workingDirectory);
    }

    private static async Task WaitForContainersAsync(
        ComposeCommand compose,
        string composeFile,
        string workingDirectory,
        int timeoutSeconds)
    {
        Console.WriteLine();
        Console.WriteLine("==> Waiting for benchmark services");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);

        while (true)
        {
            var statuses = new List<ContainerStatus>();
            foreach (var containerName in ContainerNames)
            {
                statuses.Add(new ContainerStatus(containerName, await GetContainerStatusAsync(containerName, workingDirectory)));
            }

            var notReady = statuses
                .Where(status => status.Status is not ("healthy" or "running"))
                .ToArray();

            if (notReady.Length == 0)
            {
                Console.WriteLine("Services are ready: " + string.Join(", ", statuses.Select(status => $"{status.Name}={status.Status}")));
                return;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                await RunComposeAsync(compose, composeFile, workingDirectory, ["ps"]);
                throw new TimeoutException(
                    $"Timed out waiting for benchmark services after {timeoutSeconds} seconds: " +
                    string.Join(", ", statuses.Select(status => $"{status.Name}={status.Status}")));
            }

            Console.WriteLine("Waiting: " + string.Join(", ", notReady.Select(status => $"{status.Name}={status.Status}")));
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task<string> GetContainerStatusAsync(string containerName, string workingDirectory)
    {
        var result = await RunCaptureAsync(
            "docker",
            ["inspect", "--format={{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}", containerName],
            workingDirectory);

        if (result.ExitCode != 0)
        {
            return "missing";
        }

        var line = result.StdOut
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(line) ? "unknown" : line.Trim();
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SonnetDB.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the SonnetDB repository root. Run this script from inside the repository.");
    }

    private static async Task<bool> TryRunQuietAsync(string fileName, IReadOnlyList<string> arguments)
    {
        try
        {
            var result = await RunCaptureAsync(fileName, arguments, Directory.GetCurrentDirectory());
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task RunCommandAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        Console.WriteLine();
        Console.WriteLine("==> " + fileName + " " + string.Join(" ", arguments.Select(QuoteArgument)));

        using var process = Process.Start(new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        }.AddArguments(arguments)) ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} exited with code {process.ExitCode}.");
        }
    }

    private static async Task<CommandResult> RunCaptureAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory
        }.AddArguments(arguments)) ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new CommandResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string QuoteArgument(string value)
    {
        return value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
    }

    private sealed record StartOptions(
        bool SkipBuild,
        bool SkipUp,
        bool ResetVolumes,
        bool Down,
        bool RemoveVolumes,
        int TimeoutSeconds,
        bool ShowHelp)
    {
        public static StartOptions Parse(string[] args)
        {
            var skipBuild = false;
            var skipUp = false;
            var resetVolumes = false;
            var down = false;
            var removeVolumes = false;
            var timeoutSeconds = 240;
            var showHelp = false;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--skip-build":
                        skipBuild = true;
                        break;
                    case "--skip-up":
                        skipUp = true;
                        break;
                    case "--reset-volumes":
                        resetVolumes = true;
                        break;
                    case "--down":
                        down = true;
                        break;
                    case "--remove-volumes":
                        removeVolumes = true;
                        break;
                    case "--timeout":
                        timeoutSeconds = ReadPositiveInt(args, ref i, "--timeout");
                        break;
                    case "-h":
                    case "--help":
                        showHelp = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument '{args[i]}'. Use --help for usage.");
                }
            }

            return new StartOptions(skipBuild, skipUp, resetVolumes, down, removeVolumes, timeoutSeconds, showHelp);
        }

        private static int ReadPositiveInt(string[] args, ref int index, string optionName)
        {
            index++;
            if (index >= args.Length || !int.TryParse(args[index], out var value) || value <= 0)
            {
                throw new ArgumentException($"{optionName} expects a positive integer value.");
            }

            return value;
        }
    }

    private sealed record ComposeCommand(string FileName, string[] Prefix, string DisplayName);
    private sealed record ContainerStatus(string Name, string Status);
    private sealed record CommandResult(int ExitCode, string StdOut, string StdErr);
}

static class ProcessStartInfoExtensions
{
    public static ProcessStartInfo AddArguments(this ProcessStartInfo startInfo, IEnumerable<string> arguments)
    {
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
