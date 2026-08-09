using OpenCode.Workspace.LocalClient;

namespace OpenCode.Workspace.Cli.Tests;

public sealed class InteractiveSessionAttachHelperTests
{
    private static readonly string[] Arguments = ["--session-id", "session-1", "--attachment-id", "attachment-1", "--attachment-token", "token-1", "--state-root", "state"];

    [Fact]
    public async Task Bridge_Preserves_Bytes_Forwards_Resize_And_Recovers_From_Gap()
    {
        var stdin = new byte[] { 0x00, 0xff, 0xc3, 0x1b, 0x5b, 0x33, 0x31, 0x6d, 0x0d, 0x0a };
        var stdout = new MemoryStream();
        var client = new FakeAttachClient
        {
            Outputs = new Queue<TerminalOutputReadResult>(
            [
                new() { GapDetected = true, EarliestSequence = 5, LatestSequence = 5, RequestedAfterSequence = 0, Chunks = [new TerminalOutputChunk { Sequence = 5, DataBase64 = Convert.ToBase64String(stdin) }] },
                new() { EarliestSequence = 5, LatestSequence = 5, RequestedAfterSequence = 4, Chunks = [new TerminalOutputChunk { Sequence = 5, DataBase64 = Convert.ToBase64String(stdin) }] },
            ]),
            Heartbeats = new Queue<InteractiveAttachmentControlAction>([InteractiveAttachmentControlAction.None, InteractiveAttachmentControlAction.Detach]),
        };
        var console = new FakeConsole(new MemoryStream(stdin), stdout, 132, 41);
        var helper = CreateHelper(client, console);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var result = await helper.RunAsync(Arguments, timeout.Token);

        Assert.Equal(0, result);
        Assert.Equal(stdin, client.Inputs.SelectMany(item => Convert.FromBase64String(item.DataBase64)).ToArray());
        Assert.Equal(stdin, stdout.ToArray());
        Assert.Contains(4, client.AfterSequences);
        Assert.Contains(client.Resizes, item => item is { AttachmentId: "attachment-1", AttachmentToken: "token-1", Columns: 132, Rows: 41 });
        Assert.True(client.HeartbeatCount >= 2);
        Assert.Equal(1, client.ProcessExitCount);
    }

    [Theory]
    [InlineData(InteractiveTerminalRuntimeStatus.Exited)]
    [InlineData(InteractiveTerminalRuntimeStatus.Failed)]
    [InlineData(InteractiveTerminalRuntimeStatus.Unavailable)]
    public async Task Bridge_Exits_When_Runtime_Is_Terminal(InteractiveTerminalRuntimeStatus status)
    {
        var client = new FakeAttachClient { RuntimeStatus = status };
        var helper = CreateHelper(client, new FakeConsole(new MemoryStream(), new MemoryStream(), 0, 0));
        Assert.Equal(0, await helper.RunAsync(Arguments, CancellationToken.None));
        Assert.Equal(1, client.ProcessExitCount);
    }

    [Fact]
    public async Task Bridge_Exits_Safely_On_Takeover_Authority_Loss()
    {
        var client = new FakeAttachClient { ReadError = new LocalHostClientException("attachment_not_active", "authority lost", "reattach") };
        var error = new StringWriter();
        var helper = new InteractiveSessionAttachHelper(TextWriter.Null, error, (_, _) => Task.FromResult<IInteractiveSessionAttachClient>(client), new FakeConsole(new MemoryStream(), new MemoryStream(), 0, 0));
        Assert.Equal(7, await helper.RunAsync(Arguments, CancellationToken.None));
        Assert.Contains("authority lost", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bridge_Exits_Safely_When_LocalHost_Is_Unavailable()
    {
        var error = new StringWriter();
        var helper = new InteractiveSessionAttachHelper(TextWriter.Null, error, (_, _) => throw new HttpRequestException("LocalHost unavailable"), new FakeConsole(new MemoryStream(), new MemoryStream(), 0, 0));
        Assert.Equal(7, await helper.RunAsync(Arguments, CancellationToken.None));
        Assert.Contains("LocalHost unavailable", error.ToString(), StringComparison.Ordinal);
    }

    private static InteractiveSessionAttachHelper CreateHelper(FakeAttachClient client, FakeConsole console)
        => new(TextWriter.Null, TextWriter.Null, (_, _) => Task.FromResult<IInteractiveSessionAttachClient>(client), console);

    private sealed class FakeConsole(Stream input, Stream output, int columns, int rows) : IInteractiveSessionConsole
    {
        public Stream StandardInput => input;
        public Stream StandardOutput => output;
        public int ProcessId => 123;
        public bool TryGetDimensions(out int currentColumns, out int currentRows) { currentColumns = columns; currentRows = rows; return columns > 0 && rows > 0; }
    }

    private sealed class FakeAttachClient : IInteractiveSessionAttachClient
    {
        public Queue<TerminalOutputReadResult> Outputs { get; init; } = new();
        public Queue<InteractiveAttachmentControlAction> Heartbeats { get; init; } = new([InteractiveAttachmentControlAction.Detach]);
        public List<TerminalInputRequest> Inputs { get; } = [];
        public List<TerminalResizeRequest> Resizes { get; } = [];
        public List<long> AfterSequences { get; } = [];
        public int HeartbeatCount { get; private set; }
        public int ProcessExitCount { get; private set; }
        public InteractiveTerminalRuntimeStatus RuntimeStatus { get; init; } = InteractiveTerminalRuntimeStatus.Running;
        public Exception? ReadError { get; init; }
        public Task<InteractiveSessionAttachmentActivationResult> ActivateAsync(string sessionId, string attachmentId, ActivateInteractiveSessionAttachmentRequest request, CancellationToken cancellationToken) => Task.FromResult(new InteractiveSessionAttachmentActivationResult { TerminalRuntime = new InteractiveTerminalRuntimeRecord { Status = InteractiveTerminalRuntimeStatus.Running } });
        public Task<TerminalOutputReadResult> ReadOutputAsync(string sessionId, long afterSequence, CancellationToken cancellationToken)
        {
            if (ReadError is not null) throw ReadError;
            AfterSequences.Add(afterSequence);
            return Task.FromResult(Outputs.Count > 0 ? Outputs.Dequeue() : new TerminalOutputReadResult { RequestedAfterSequence = afterSequence });
        }
        public Task<InteractiveTerminalRuntimeRecord> SendInputAsync(string sessionId, TerminalInputRequest request, CancellationToken cancellationToken) { Inputs.Add(request); return Task.FromResult(new InteractiveTerminalRuntimeRecord { Status = InteractiveTerminalRuntimeStatus.Running }); }
        public Task<InteractiveTerminalRuntimeRecord> ResizeAsync(string sessionId, TerminalResizeRequest request, CancellationToken cancellationToken) { Resizes.Add(request); return Task.FromResult(new InteractiveTerminalRuntimeRecord { Status = InteractiveTerminalRuntimeStatus.Running }); }
        public Task<InteractiveSessionAttachmentHeartbeatResult> HeartbeatAsync(string sessionId, string attachmentId, InteractiveSessionAttachmentHeartbeatRequest request, CancellationToken cancellationToken)
        {
            HeartbeatCount++;
            return Task.FromResult(new InteractiveSessionAttachmentHeartbeatResult { RequestedAction = Heartbeats.Count > 0 ? Heartbeats.Dequeue() : InteractiveAttachmentControlAction.Detach, HeartbeatIntervalSeconds = Heartbeats.Count > 0 ? 1 : 0 });
        }
        public Task<InteractiveTerminalRuntimeRecord> GetTerminalAsync(string sessionId, CancellationToken cancellationToken) => Task.FromResult(new InteractiveTerminalRuntimeRecord { Status = RuntimeStatus });
        public Task<InteractiveAgentSessionRecord> ReportProcessExitAsync(string sessionId, string attachmentId, InteractiveSessionAttachmentProcessExitRequest request, CancellationToken cancellationToken) { ProcessExitCount++; return Task.FromResult(new InteractiveAgentSessionRecord()); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
