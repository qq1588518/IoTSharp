namespace SonnetDB.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        var app = new CliApplication(Console.In, Console.Out, Console.Error);
        return app.Run(args);
    }
}
