using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using OpenCode.Workspace.Api;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.LocalClient;
using OpenCode.Workspace.Mcp;
using Xunit;

namespace OpenCode.Workspace.Api.IntegrationTests;

public sealed class WindowsConPtyTerminalRuntimeIntegrationTests
{
    [SkippableFact]
    [Trait("Category", "WindowsConPtyIntegration")]
    public async Task LocalHost_Restart_Normalizes_Old_ConPty_And_Starts_Fresh_Runtime()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows ConPTY integration requires a Windows host.");
        var root = Path.Combine(Path.GetTempPath(), $"local-host-conpty-restart-{Guid.NewGuid():N}");
        var child = GetChildPath();
        var firstAdapter = new DeterministicChildConPtyRuntime(child);
        var secondAdapter = new DeterministicChildConPtyRuntime(child);
        try
        {
            var first = await CreateServicesAsync(root, firstAdapter, createSession: true);
            await first.Sessions.RecordProviderSessionIdentityAsync(first.Session.InteractiveAgentSessionId, "provider-canonical", ProviderSessionIdentitySource.LaunchCorrelation);
            var oldRuntime = await first.Terminals.StartAsync(first.Session.InteractiveAgentSessionId, new StartInteractiveTerminalRequest(), CancellationToken.None);
            var oldAttachment = await first.Sessions.AttachAsync(first.Session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = first.Session.InteractiveAgentSessionId, CommandId = "attach-old", ClientInstanceId = "client-old" });
            var oldToken = ExtractLaunchArgument(oldAttachment, "--attachment-token");
            await first.Sessions.ActivateAsync(first.Session.InteractiveAgentSessionId, oldAttachment.Attachment.AttachmentId, new ActivateInteractiveSessionAttachmentRequest { AttachmentToken = oldToken, HelperProcessId = Environment.ProcessId });
            first.Terminals.SetActiveAttachment(first.Session.InteractiveAgentSessionId, oldAttachment.Attachment.AttachmentId);
            await WaitForOutputAsync(first.Terminals, first.Session.InteractiveAgentSessionId, "READY", TimeSpan.FromSeconds(5));

            var restarted = await CreateServicesAsync(root, secondAdapter, createSession: false);
            var normalizedSession = await restarted.Sessions.GetAsync(first.Session.InteractiveAgentSessionId);
            var normalizedRuntime = await restarted.Terminals.GetAsync(first.Session.InteractiveAgentSessionId, CancellationToken.None);
            Assert.Equal(InteractiveTerminalRuntimeStatus.Unavailable, normalizedRuntime.Status);
            Assert.Equal(oldRuntime.RuntimeId, normalizedRuntime.RuntimeId);
            Assert.Equal("provider-canonical", normalizedRuntime.ProviderSessionId);
            Assert.Equal(InteractiveAgentSessionStatus.Detached, normalizedSession.Status);
            Assert.Equal(string.Empty, normalizedSession.ActiveAttachmentId);
            Assert.Null(normalizedSession.ActiveLease);

            var freshRuntime = await restarted.Terminals.StartAsync(first.Session.InteractiveAgentSessionId, new StartInteractiveTerminalRequest(), CancellationToken.None);
            Assert.NotEqual(oldRuntime.RuntimeId, freshRuntime.RuntimeId);
            Assert.Equal("provider-canonical", freshRuntime.ProviderSessionId);
            await WaitForOutputAsync(restarted.Terminals, first.Session.InteractiveAgentSessionId, "READY", TimeSpan.FromSeconds(5));
            var freshAttachment = await restarted.Sessions.AttachAsync(first.Session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = first.Session.InteractiveAgentSessionId, CommandId = "attach-new", ClientInstanceId = "client-new" });
            var freshToken = ExtractLaunchArgument(freshAttachment, "--attachment-token");
            await restarted.Sessions.ActivateAsync(first.Session.InteractiveAgentSessionId, freshAttachment.Attachment.AttachmentId, new ActivateInteractiveSessionAttachmentRequest { AttachmentToken = freshToken, HelperProcessId = Environment.ProcessId });
            restarted.Terminals.SetActiveAttachment(first.Session.InteractiveAgentSessionId, freshAttachment.Attachment.AttachmentId);
            await restarted.Terminals.InputAsync(first.Session.InteractiveAgentSessionId, new TerminalInputRequest { AttachmentId = freshAttachment.Attachment.AttachmentId, AttachmentToken = freshToken, DataBase64 = Convert.ToBase64String(Encoding.ASCII.GetBytes("after-restart\r\n")) }, CancellationToken.None);
            await WaitForOutputAsync(restarted.Terminals, first.Session.InteractiveAgentSessionId, "ECHO:after-restart", TimeSpan.FromSeconds(5));
            await restarted.Terminals.StopAsync(first.Session.InteractiveAgentSessionId, CancellationToken.None);
            Assert.Equal(InteractiveTerminalRuntimeStatus.Exited, (await restarted.Terminals.GetAsync(first.Session.InteractiveAgentSessionId, CancellationToken.None)).Status);
            Assert.True(secondAdapter.NativeResourcesClosed);
        }
        finally
        {
            if (!firstAdapter.NativeResourcesClosed) await firstAdapter.StopAsync(CancellationToken.None);
            if (!secondAdapter.NativeResourcesClosed) await secondAdapter.StopAsync(CancellationToken.None);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [SkippableFact]
    [Trait("Category", "WindowsConPtyIntegration")]
    public async Task Detach_Reattach_And_Bounded_Stop_Preserve_The_Owned_Child()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows ConPTY integration requires a Windows host.");
        var runtime = new WindowsConPtyTerminalRuntime();
        var bytes = new ConcurrentQueue<byte>();
        void Capture(byte[] chunk) { foreach (var value in chunk) bytes.Enqueue(value); }
        runtime.Output += Capture;
        var session = new InteractiveAgentSessionRecord { InteractiveAgentSessionId = "native-conpty", WorkspaceId = "test" };
        var child = GetChildPath();
        Assert.True(File.Exists(child), $"ConPTY test child was not built: {child}");

        InteractiveTerminalRuntimeRecord? started = null;
        try
        {
            started = await runtime.StartAsync(session, child, [], AppContext.BaseDirectory, new InteractiveTerminalDimensions { Columns = 120, Rows = 30 }, CancellationToken.None);
            Assert.Equal(InteractiveTerminalRuntimeStatus.Running, started.Status);
            Assert.False(string.IsNullOrWhiteSpace(started.RuntimeId));
            Assert.True(started.ProcessId > 0);
            await WaitForTextAsync(runtime, bytes, "READY", TimeSpan.FromSeconds(5));
            await runtime.WriteAsync(Encoding.ASCII.GetBytes("first\r\n"), CancellationToken.None);
            await WaitForTextAsync(runtime, bytes, "ECHO:first", TimeSpan.FromSeconds(5));
        await runtime.ResizeAsync(new InteractiveTerminalDimensions { Columns = 140, Rows = 40 }, CancellationToken.None);

        runtime.Output -= Capture;
        await Task.Delay(100);
        Assert.Equal(InteractiveTerminalRuntimeStatus.Running, runtime.Record.Status);
        Assert.Equal(started.RuntimeId, runtime.Record.RuntimeId);
        Assert.Equal(started.ProcessId, runtime.Record.ProcessId);
        Assert.False(Process.GetProcessById(started.ProcessId!.Value).HasExited);

        runtime.Output += Capture;
        await runtime.WriteAsync(Encoding.ASCII.GetBytes("second\r\n"), CancellationToken.None);
            await WaitForTextAsync(runtime, bytes, "ECHO:second", TimeSpan.FromSeconds(5));
            await runtime.StopAsync(CancellationToken.None);

            Assert.Equal(InteractiveTerminalRuntimeStatus.Exited, runtime.Record.Status);
            Assert.True(runtime.NativeResourcesClosed);
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(started.ProcessId.Value));
        }
        finally
        {
            if (!runtime.NativeResourcesClosed) await runtime.StopAsync(CancellationToken.None);
        }
    }

    private static async Task WaitForTextAsync(WindowsConPtyTerminalRuntime runtime, ConcurrentQueue<byte> bytes, string expected, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (Encoding.UTF8.GetString(bytes.ToArray()).Contains(expected, StringComparison.Ordinal)) return;
            await Task.Delay(25);
        }
        Assert.Fail($"ConPTY output did not contain '{expected}'. Status={runtime.Record.Status}; exitCode={runtime.Record.ExitCode}; outputFailure={runtime.OutputFailure}; bytes={Convert.ToHexString(bytes.ToArray())}");
    }

    private static async Task<(InteractiveAgentSessionService Sessions, InteractiveTerminalRuntimeService Terminals, InteractiveAgentSessionRecord Session)> CreateServicesAsync(string root, IInteractiveTerminalRuntime runtime, bool createSession)
    {
        var store = new LocalHostStateStore(new DefaultLocalHostStatePathProvider(new LocalHostStateOptions { StateRoot = root }));
        var workspace = new FakeApiService
        {
            GetWorkspaceHandler = workspaceId => Task.FromResult(new OpenCode.Workspace.Mcp.WorkspaceRecordModel
            {
                WorkspaceId = workspaceId,
                Name = workspaceId,
                WorkspaceRoot = root,
                Snapshot = CreateSnapshot(workspaceId, root),
            }),
        };
        var sessions = new InteractiveAgentSessionService(store, workspace, new InteractiveAttachmentLeasePolicy(), new InteractiveSessionLaunchDescriptorFactory(), new SystemClock());
        var terminals = new InteractiveTerminalRuntimeService(sessions, workspace, new SystemClock(), store, runtimeFactory: () => runtime);
        var session = createSession
            ? await sessions.CreateAsync(new CreateInteractiveAgentSessionRequest { CommandId = "restart-test", WorkspaceId = "alpha", Title = "restart" })
            : (await sessions.ListAsync()).Single();
        return (sessions, terminals, session);
    }

    private static WorkspaceSnapshot CreateSnapshot(string workspaceId, string root)
    {
        var paths = WorkspacePathBuilder.Build(root, Path.Combine(root, "workspace.yaml"));
        return new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord { Name = workspaceId, RootPath = root, RepositoryPath = root, ConfigurationPath = paths.WorkspaceYamlPath },
            Definition = new WorkspaceDefinition { Workspace = new WorkspaceMetadata { Id = workspaceId, Name = workspaceId, Image = "ubuntu:24.04" } },
            Paths = paths,
            ConfigurationPath = paths.WorkspaceYamlPath,
            RuntimeState = WorkspaceRuntimeState.Stopped,
            Safety = new WorkspaceSafetySnapshot { OverallStatus = WorkspaceSafetyLevel.NeedsReview, Headline = workspaceId, Message = workspaceId, LocalRecovery = new WorkspaceLocalRecoverySnapshot(), Backup = new WorkspaceBackupSnapshot(), IgnorePolicy = new WorkspaceIgnorePolicyReview(), AdvancedGit = new WorkspaceAdvancedGitSnapshot() },
            Session = new WorkspaceSessionSnapshot(),
            Synchronization = new WorkspaceSynchronizationSnapshot(),
            Assistant = new WorkspaceApexAssistantSnapshot(),
            Health = new WorkspaceHealthSnapshot(),
            Readiness = new WorkspaceReadinessSnapshot(),
        };
    }

    private static string ExtractLaunchArgument(InteractiveSessionAttachResult attached, string name)
        => attached.LaunchDescriptor.Arguments[Array.IndexOf(attached.LaunchDescriptor.Arguments.ToArray(), name) + 1];

    private static async Task WaitForOutputAsync(InteractiveTerminalRuntimeService terminals, string sessionId, string expected, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var output = await terminals.ReadOutputAsync(sessionId, 0, CancellationToken.None);
            var text = Encoding.UTF8.GetString(output.Chunks.SelectMany(chunk => Convert.FromBase64String(chunk.DataBase64)).ToArray());
            if (text.Contains(expected, StringComparison.Ordinal)) return;
            await Task.Delay(25);
        }
        Assert.Fail($"Terminal output did not contain '{expected}'.");
    }

    private static string GetChildPath()
    {
        var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
        var child = Path.Combine(TestPaths.RepositoryRoot, "tests", "OpenCode.Workspace.ConPtyTestChild", "bin", configuration, "net10.0", "OpenCode.Workspace.ConPtyTestChild.exe");
        Assert.True(File.Exists(child), $"ConPTY test child was not built: {child}");
        return child;
    }

    private sealed class DeterministicChildConPtyRuntime(string childPath) : IInteractiveTerminalRuntime
    {
        private readonly WindowsConPtyTerminalRuntime _inner = new();
        public InteractiveTerminalRuntimeRecord Record => _inner.Record;
        public bool NativeResourcesClosed => _inner.NativeResourcesClosed;
        public event Action<byte[]>? Output { add => _inner.Output += value; remove => _inner.Output -= value; }
        public event Action<InteractiveTerminalRuntimeRecord>? Changed { add => _inner.Changed += value; remove => _inner.Changed -= value; }
        public Task<InteractiveTerminalRuntimeRecord> StartAsync(InteractiveAgentSessionRecord session, string fileName, IReadOnlyList<string> arguments, string workingDirectory, InteractiveTerminalDimensions dimensions, CancellationToken cancellationToken)
            => _inner.StartAsync(session, childPath, [], AppContext.BaseDirectory, dimensions, cancellationToken);
        public Task WriteAsync(byte[] data, CancellationToken cancellationToken) => _inner.WriteAsync(data, cancellationToken);
        public Task ResizeAsync(InteractiveTerminalDimensions dimensions, CancellationToken cancellationToken) => _inner.ResizeAsync(dimensions, cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken) => _inner.StopAsync(cancellationToken);
    }
}
