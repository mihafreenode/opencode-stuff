namespace OpenCode.Workspace.Cli;

public static class Program
{
    public static Task<int> Main(string[] args)
        => new CliApplication(Console.Out, Console.Error).RunAsync(args);
}
