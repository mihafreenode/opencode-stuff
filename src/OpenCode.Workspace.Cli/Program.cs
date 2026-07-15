namespace OpenCode.Workspace.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellationSource = new CancellationTokenSource();
        var cancelCount = 0;
        ConsoleCancelEventHandler? handler = null;
        handler = (_, eventArgs) =>
        {
            cancelCount++;
            if (cancelCount == 1)
            {
                eventArgs.Cancel = true;
                cancellationSource.Cancel();
                return;
            }

            eventArgs.Cancel = false;
        };

        Console.CancelKeyPress += handler;
        try
        {
            return await new CliApplication(Console.Out, Console.Error).RunAsync(args, cancellationSource.Token);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }
}
