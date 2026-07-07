using System.Diagnostics;

return await BenchmarkRunScript.RunAsync(args);

static class BenchmarkRunScript
{
    public static async Task<int> RunAsync(string[] args)
    {
        RunOptions? options = null;
        string? repoRoot = null;
        string? startProject = null;
        var exitCode = 0;

        try
        {
            options = RunOptions.Parse(args);
            if (options.ShowHelp)
            {
                PrintUsage();
                return 0;
            }

            repoRoot = FindRepoRoot();
            startProject = Path.Combine(repoRoot, "eng", "benchmarks", "start-benchmark-env", "start-benchmark-env.csproj");
            var benchmarkProject = Path.Combine(repoRoot, "tests", "SonnetDB.Benchmarks", "SonnetDB.Benchmarks.csproj");

            if (!options.SkipEnvironment)
            {
                var startArgs = new List<string>
                {
                    "run",
                    "--project",
                    startProject,
                    "--"
                };

                if (options.SkipBuild)
                {
                    startArgs.Add("--skip-build");
                }

                if (options.ResetVolumes)
                {
                    startArgs.Add("--reset-volumes");
                }

                startArgs.Add("--timeout");
                startArgs.Add(options.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));

                await RunCommandAsync("dotnet", startArgs, repoRoot);
            }

            var benchmarkArgs = new List<string>
            {
                "run",
                "-c",
                "Release",
                "--project",
                benchmarkProject,
                "--",
                "--filter",
                options.Filter
            };
            benchmarkArgs.AddRange(options.ExtraBenchmarkArgs);

            var environment = new Dictionary<string, string?>();
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SONNETDB_BENCH_TOKEN")))
            {
                environment["SONNETDB_BENCH_TOKEN"] = "bench-admin-token";
            }

            await RunCommandAsync("dotnet", benchmarkArgs, repoRoot, environment);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            exitCode = 1;
        }
        finally
        {
            if (options is not null &&
                repoRoot is not null &&
                startProject is not null &&
                (options.DownAfterRun || options.RemoveVolumes))
            {
                var downArgs = new List<string>
                {
                    "run",
                    "--project",
                    startProject,
                    "--",
                    "--down"
                };

                if (options.RemoveVolumes)
                {
                    downArgs.Add("--remove-volumes");
                }

                try
                {
                    await RunCommandAsync("dotnet", downArgs, repoRoot);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    exitCode = 1;
                }
            }
        }

        return exitCode;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
        Usage:
          dotnet run --project eng/benchmarks/run-benchmarks/run-benchmarks.csproj -- [options] [-- <BenchmarkDotNet args>]

        Options:
          --filter <pattern>   BenchmarkDotNet filter. Default: *.
          --skip-env           Do not build/start/wait for docker compose services.
          --skip-build         Start compose services without docker compose build.
          --reset-volumes      Reset compose volumes before starting services.
          --timeout <sec>      Health wait timeout. Default: 240.
          --down-after-run     Stop compose services after benchmarks finish.
          --remove-volumes     Stop services and remove volumes after benchmarks finish.
          -h, --help           Show this help.

        Examples:
          dotnet run --project eng/benchmarks/run-benchmarks/run-benchmarks.csproj -- --filter *
          dotnet run --project eng/benchmarks/run-benchmarks/run-benchmarks.csproj -- --filter *Insert* --reset-volumes
          dotnet run --project eng/benchmarks/run-benchmarks/run-benchmarks.csproj -- --filter *Query* --skip-env
        """);
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

    private static async Task RunCommandAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        Console.WriteLine();
        Console.WriteLine("==> " + fileName + " " + string.Join(" ", arguments.Select(QuoteArgument)));

        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var item in environment)
            {
                startInfo.Environment[item.Key] = item.Value;
            }
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} exited with code {process.ExitCode}.");
        }
    }

    private static string QuoteArgument(string value)
    {
        return value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
    }

    private sealed record RunOptions(
        string Filter,
        bool SkipEnvironment,
        bool SkipBuild,
        bool ResetVolumes,
        bool DownAfterRun,
        bool RemoveVolumes,
        int TimeoutSeconds,
        IReadOnlyList<string> ExtraBenchmarkArgs,
        bool ShowHelp)
    {
        public static RunOptions Parse(string[] args)
        {
            var filter = "*";
            var skipEnvironment = false;
            var skipBuild = false;
            var resetVolumes = false;
            var downAfterRun = false;
            var removeVolumes = false;
            var timeoutSeconds = 240;
            var extraBenchmarkArgs = new List<string>();
            var showHelp = false;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--filter":
                        filter = ReadString(args, ref i, "--filter");
                        break;
                    case "--skip-env":
                        skipEnvironment = true;
                        break;
                    case "--skip-build":
                        skipBuild = true;
                        break;
                    case "--reset-volumes":
                        resetVolumes = true;
                        break;
                    case "--down-after-run":
                        downAfterRun = true;
                        break;
                    case "--remove-volumes":
                        removeVolumes = true;
                        downAfterRun = true;
                        break;
                    case "--timeout":
                        timeoutSeconds = ReadPositiveInt(args, ref i, "--timeout");
                        break;
                    case "-h":
                    case "--help":
                        showHelp = true;
                        break;
                    case "--":
                        extraBenchmarkArgs.AddRange(args.Skip(i + 1));
                        i = args.Length;
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument '{args[i]}'. Use --help for usage.");
                }
            }

            return new RunOptions(
                filter,
                skipEnvironment,
                skipBuild,
                resetVolumes,
                downAfterRun,
                removeVolumes,
                timeoutSeconds,
                extraBenchmarkArgs,
                showHelp);
        }

        private static string ReadString(string[] args, ref int index, string optionName)
        {
            index++;
            if (index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            {
                throw new ArgumentException($"{optionName} expects a value.");
            }

            return args[index];
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
}
