using System.Text;
using OpenCode.Workspace.LocalClient;

namespace OpenCode.Workspace.Cli;

// Presentation bridge only: LocalHost owns the ConPTY and provider process.
internal sealed class InteractiveSessionAttachHelper
{
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly Func<string, CancellationToken, Task<IInteractiveSessionAttachClient>> _connect;
    private readonly IInteractiveSessionConsole _console;

    public InteractiveSessionAttachHelper(TextWriter output, TextWriter error)
        : this(output, error, ConnectAsync, new SystemInteractiveSessionConsole())
    {
    }

    internal InteractiveSessionAttachHelper(TextWriter output, TextWriter error, Func<string, CancellationToken, Task<IInteractiveSessionAttachClient>> connect, IInteractiveSessionConsole console)
    {
        _output = output;
        _error = error;
        _connect = connect;
        _console = console;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var sessionId = RequireOption(args, "--session-id");
        var attachmentId = RequireOption(args, "--attachment-id");
        var attachmentToken = RequireOption(args, "--attachment-token");
        var stateRoot = RequireOption(args, "--state-root");
        try
        {
            await using var client = await _connect(stateRoot, cancellationToken);
            var activation = await client.ActivateAsync(sessionId, attachmentId, new ActivateInteractiveSessionAttachmentRequest
            {
                AttachmentToken = attachmentToken,
                HelperProcessId = _console.ProcessId,
            }, cancellationToken);

            var lastSequence = activation.TerminalRuntime.LatestSequence;
            var heartbeatAt = DateTimeOffset.UtcNow;
            var dimensions = (Columns: 0, Rows: 0);
            var stdout = _console.StandardOutput;
            var stdin = _console.StandardInput;
            using var bridgeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var input = ForwardInputAsync(client, sessionId, attachmentId, attachmentToken, stdin, bridgeCancellation.Token);

            while (!bridgeCancellation.IsCancellationRequested)
            {
                dimensions = await ForwardResizeIfChangedAsync(client, sessionId, attachmentId, attachmentToken, dimensions, bridgeCancellation.Token);
                var output = await client.ReadOutputAsync(sessionId, lastSequence, bridgeCancellation.Token);
                if (output.GapDetected)
                {
                    lastSequence = output.EarliestSequence - 1;
                    continue;
                }
                foreach (var chunk in output.Chunks)
                {
                    var bytes = Convert.FromBase64String(chunk.DataBase64);
                    await stdout.WriteAsync(bytes, bridgeCancellation.Token);
                    await stdout.FlushAsync(bridgeCancellation.Token);
                    lastSequence = chunk.Sequence;
                }

                if (DateTimeOffset.UtcNow >= heartbeatAt)
                {
                    var heartbeat = await client.HeartbeatAsync(sessionId, attachmentId, new InteractiveSessionAttachmentHeartbeatRequest { AttachmentToken = attachmentToken }, bridgeCancellation.Token);
                    if (heartbeat.RequestedAction == InteractiveAttachmentControlAction.Detach)
                    {
                        break;
                    }
                    heartbeatAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, heartbeat.HeartbeatIntervalSeconds));
                }

                var runtime = await client.GetTerminalAsync(sessionId, bridgeCancellation.Token);
                if (runtime.Status is InteractiveTerminalRuntimeStatus.Exited or InteractiveTerminalRuntimeStatus.Failed or InteractiveTerminalRuntimeStatus.Unavailable)
                {
                    break;
                }
                await Task.Delay(50, bridgeCancellation.Token);
            }

            bridgeCancellation.Cancel();
            stdin.Dispose();
            await AwaitInputShutdownAsync(input);
            await client.ReportProcessExitAsync(sessionId, attachmentId, new InteractiveSessionAttachmentProcessExitRequest
            {
                AttachmentToken = attachmentToken,
                ChildProcessId = _console.ProcessId,
                Outcome = "presentation_closed",
            }, CancellationToken.None);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            await _error.WriteLineAsync(exception.Message);
            return 7;
        }
    }

    private static async Task ForwardInputAsync(IInteractiveSessionAttachClient client, string sessionId, string attachmentId, string attachmentToken, Stream stdin, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        while (!cancellationToken.IsCancellationRequested)
        {
            var count = await stdin.ReadAsync(buffer, cancellationToken);
            if (count == 0) return;
            await client.SendInputAsync(sessionId, new TerminalInputRequest
            {
                AttachmentId = attachmentId,
                AttachmentToken = attachmentToken,
                DataBase64 = Convert.ToBase64String(buffer, 0, count),
            }, cancellationToken);
        }
    }

    private async Task<(int Columns, int Rows)> ForwardResizeIfChangedAsync(IInteractiveSessionAttachClient client, string sessionId, string attachmentId, string attachmentToken, (int Columns, int Rows) previous, CancellationToken cancellationToken)
    {
        try
        {
            if (!_console.TryGetDimensions(out var columns, out var rows)) return previous;
            var current = (columns, rows);
            if (current.Item1 >= 20 && current.Item2 >= 5 && current != previous)
            {
                await client.ResizeAsync(sessionId, new TerminalResizeRequest { AttachmentId = attachmentId, AttachmentToken = attachmentToken, Columns = current.Item1, Rows = current.Item2 }, cancellationToken);
                return current;
            }
        }
        catch (IOException) { }
        catch (PlatformNotSupportedException) { }
        return previous;
    }

    private static async Task AwaitInputShutdownAsync(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
        catch (TimeoutException) { }
    }

    private static string RequireOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        }
        throw new ArgumentException($"Missing required option {name}.");
    }

    private static async Task<IInteractiveSessionAttachClient> ConnectAsync(string stateRoot, CancellationToken cancellationToken)
        => new LocalHostInteractiveSessionAttachClient(await LocalHostClient.ConnectAsync(new LocalHostClientOptions { StateRoot = stateRoot }, cancellationToken));
}

internal interface IInteractiveSessionAttachClient : IAsyncDisposable
{
    Task<InteractiveSessionAttachmentActivationResult> ActivateAsync(string sessionId, string attachmentId, ActivateInteractiveSessionAttachmentRequest request, CancellationToken cancellationToken);
    Task<TerminalOutputReadResult> ReadOutputAsync(string sessionId, long afterSequence, CancellationToken cancellationToken);
    Task<InteractiveTerminalRuntimeRecord> SendInputAsync(string sessionId, TerminalInputRequest request, CancellationToken cancellationToken);
    Task<InteractiveTerminalRuntimeRecord> ResizeAsync(string sessionId, TerminalResizeRequest request, CancellationToken cancellationToken);
    Task<InteractiveSessionAttachmentHeartbeatResult> HeartbeatAsync(string sessionId, string attachmentId, InteractiveSessionAttachmentHeartbeatRequest request, CancellationToken cancellationToken);
    Task<InteractiveTerminalRuntimeRecord> GetTerminalAsync(string sessionId, CancellationToken cancellationToken);
    Task<InteractiveAgentSessionRecord> ReportProcessExitAsync(string sessionId, string attachmentId, InteractiveSessionAttachmentProcessExitRequest request, CancellationToken cancellationToken);
}

internal interface IInteractiveSessionConsole
{
    Stream StandardInput { get; }
    Stream StandardOutput { get; }
    int ProcessId { get; }
    bool TryGetDimensions(out int columns, out int rows);
}

internal sealed class LocalHostInteractiveSessionAttachClient(LocalHostClient client) : IInteractiveSessionAttachClient
{
    public Task<InteractiveSessionAttachmentActivationResult> ActivateAsync(string sessionId, string attachmentId, ActivateInteractiveSessionAttachmentRequest request, CancellationToken cancellationToken) => client.ActivateInteractiveSessionAttachmentAsync(sessionId, attachmentId, request, cancellationToken);
    public Task<TerminalOutputReadResult> ReadOutputAsync(string sessionId, long afterSequence, CancellationToken cancellationToken) => client.GetInteractiveTerminalOutputAsync(sessionId, afterSequence, cancellationToken);
    public Task<InteractiveTerminalRuntimeRecord> SendInputAsync(string sessionId, TerminalInputRequest request, CancellationToken cancellationToken) => client.SendInteractiveTerminalInputAsync(sessionId, request, cancellationToken);
    public Task<InteractiveTerminalRuntimeRecord> ResizeAsync(string sessionId, TerminalResizeRequest request, CancellationToken cancellationToken) => client.ResizeInteractiveTerminalAsync(sessionId, request, cancellationToken);
    public Task<InteractiveSessionAttachmentHeartbeatResult> HeartbeatAsync(string sessionId, string attachmentId, InteractiveSessionAttachmentHeartbeatRequest request, CancellationToken cancellationToken) => client.HeartbeatInteractiveSessionAttachmentAsync(sessionId, attachmentId, request, cancellationToken);
    public Task<InteractiveTerminalRuntimeRecord> GetTerminalAsync(string sessionId, CancellationToken cancellationToken) => client.GetInteractiveTerminalAsync(sessionId, cancellationToken);
    public Task<InteractiveAgentSessionRecord> ReportProcessExitAsync(string sessionId, string attachmentId, InteractiveSessionAttachmentProcessExitRequest request, CancellationToken cancellationToken) => client.ReportInteractiveSessionAttachmentProcessExitAsync(sessionId, attachmentId, request, cancellationToken);
    public ValueTask DisposeAsync() => client.DisposeAsync();
}

internal sealed class SystemInteractiveSessionConsole : IInteractiveSessionConsole
{
    public Stream StandardInput { get; } = Console.OpenStandardInput();
    public Stream StandardOutput { get; } = Console.OpenStandardOutput();
    public int ProcessId => Environment.ProcessId;
    public bool TryGetDimensions(out int columns, out int rows)
    {
        try { columns = Console.WindowWidth; rows = Console.WindowHeight; return true; }
        catch (IOException) { columns = rows = 0; return false; }
        catch (PlatformNotSupportedException) { columns = rows = 0; return false; }
    }
}
