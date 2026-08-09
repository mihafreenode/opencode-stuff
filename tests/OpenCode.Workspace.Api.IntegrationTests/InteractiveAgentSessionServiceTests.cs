using OpenCode.Workspace.Api;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.LocalClient;
using OpenCode.Workspace.Mcp;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace OpenCode.Workspace.Api.IntegrationTests;

public sealed class InteractiveAgentSessionServiceTests
{
    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task TerminalRuntime_Uses_Production_Service_For_Bytes_Reattach_Takeover_And_Stop()
    {
        var fixture = await TerminalFixture.CreateAsync();
        var started = await fixture.Application.StartInteractiveTerminalAsync(fixture.Session.InteractiveAgentSessionId, new StartInteractiveTerminalRequest());
        var attachmentA = await fixture.AttachAsync("client-a");
        fixture.Runtime.EmitOutput([0x00, 0xff, 0xc3, 0x1b, 0x0d, 0x0a]);

        await fixture.Application.SendInteractiveTerminalInputAsync(fixture.Session.InteractiveAgentSessionId, new TerminalInputRequest { AttachmentId = attachmentA.Attachment.Attachment.AttachmentId, AttachmentToken = attachmentA.Token, DataBase64 = Convert.ToBase64String(new byte[] { 0x00, 0xff, 0x1b, 0x0d, 0x0a }) });
        var output = await fixture.Application.GetInteractiveTerminalOutputAsync(fixture.Session.InteractiveAgentSessionId, 0);
        Assert.Equal(new byte[] { 0x00, 0xff, 0xc3, 0x1b, 0x0d, 0x0a }, Convert.FromBase64String(output.Chunks.Single().DataBase64));
        Assert.Equal(1, output.LatestSequence);

        await fixture.Application.DetachInteractiveSessionAsync(fixture.Session.InteractiveAgentSessionId, attachmentA.Attachment.Attachment.AttachmentId, new DetachInteractiveSessionAttachmentRequest { ClientInstanceId = "client-a", Reason = "user_detach" });
        await fixture.Application.ReportInteractiveSessionAttachmentProcessExitAsync(fixture.Session.InteractiveAgentSessionId, attachmentA.Attachment.Attachment.AttachmentId, new InteractiveSessionAttachmentProcessExitRequest { AttachmentToken = attachmentA.Token, Outcome = "detach_requested" });
        var detached = await fixture.Application.GetInteractiveTerminalAsync(fixture.Session.InteractiveAgentSessionId);
        Assert.Equal(InteractiveTerminalRuntimeStatus.Running, detached.Status);
        Assert.Equal(started.RuntimeId, detached.RuntimeId);
        Assert.Equal(4242, detached.ProcessId);
        Assert.Equal(0, fixture.Runtime.StopCount);

        fixture.Runtime.EmitOutput([0x0d, 0x0a]);
        var attachmentB = await fixture.AttachAsync("client-b");
        var reattached = await fixture.Application.GetInteractiveTerminalAsync(fixture.Session.InteractiveAgentSessionId);
        Assert.NotEqual(attachmentA.Attachment.Attachment.AttachmentId, attachmentB.Attachment.Attachment.AttachmentId);
        Assert.Equal(started.RuntimeId, reattached.RuntimeId);
        Assert.Equal(detached.ProcessId, reattached.ProcessId);
        Assert.Equal(2, (await fixture.Application.GetInteractiveTerminalOutputAsync(fixture.Session.InteractiveAgentSessionId, 1)).LatestSequence);

        await fixture.Application.StopInteractiveTerminalAsync(fixture.Session.InteractiveAgentSessionId);
        var stopped = await fixture.Application.GetInteractiveTerminalAsync(fixture.Session.InteractiveAgentSessionId);
        Assert.Equal(InteractiveTerminalRuntimeStatus.Exited, stopped.Status);
        Assert.Equal(1, fixture.Runtime.StopCount);
        var inputError = await Assert.ThrowsAsync<OpenCodeWorkspaceMcpException>(() => fixture.Application.SendInteractiveTerminalInputAsync(fixture.Session.InteractiveAgentSessionId, new TerminalInputRequest { AttachmentId = attachmentB.Attachment.Attachment.AttachmentId, AttachmentToken = attachmentB.Token, DataBase64 = "AA==" }));
        Assert.Equal("input_after_exit", inputError.Code);
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task TerminalRuntime_Rolls_Buffer_And_Reports_Exact_Gap()
    {
        var fixture = await TerminalFixture.CreateAsync();
        await fixture.Application.StartInteractiveTerminalAsync(fixture.Session.InteractiveAgentSessionId, new StartInteractiveTerminalRequest());
        fixture.Runtime.EmitOutput(new byte[1024 * 1024]);
        fixture.Runtime.EmitOutput([0xff]);

        var expired = await fixture.Application.GetInteractiveTerminalOutputAsync(fixture.Session.InteractiveAgentSessionId, 0);
        Assert.True(expired.GapDetected);
        Assert.Equal(0, expired.RequestedAfterSequence);
        Assert.Equal(2, expired.EarliestSequence);
        Assert.Equal(2, expired.LatestSequence);
        Assert.Equal(new byte[] { 0xff }, Convert.FromBase64String(expired.Chunks.Single().DataBase64));
        var recovered = await fixture.Application.GetInteractiveTerminalOutputAsync(fixture.Session.InteractiveAgentSessionId, expired.EarliestSequence - 1);
        Assert.False(recovered.GapDetected);
        Assert.Equal(expired.Chunks.Select(chunk => chunk.Sequence), recovered.Chunks.Select(chunk => chunk.Sequence));
    }
    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task TerminalRuntime_Correlates_Exactly_One_New_Provider_Session()
    {
        var discovery = new TestProviderSessionDiscovery([Set("old"), Set("old", "new")]);
        var fixture = await TerminalFixture.CreateAsync(discovery: discovery);
        var runtime = await fixture.Application.StartInteractiveTerminalAsync(fixture.Session.InteractiveAgentSessionId, new StartInteractiveTerminalRequest());
        var session = await fixture.Application.GetInteractiveAgentSessionAsync(fixture.Session.InteractiveAgentSessionId);
        Assert.Equal("new", runtime.ProviderSessionId);
        Assert.Equal("new", session.ProviderSessionId);
        Assert.Equal(ProviderSessionIdentitySource.LaunchCorrelation, session.ProviderSessionIdentitySource);
    }

    [Theory]
    [Trait("Category", "FastIntegration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalRuntime_Does_Not_Guess_Unresolved_Or_Ambiguous_Provider_Session(bool ambiguous)
    {
        var discovery = new TestProviderSessionDiscovery([Set("old"), ambiguous ? Set("old", "new-a", "new-b") : Set("old")]);
        var fixture = await TerminalFixture.CreateAsync(discovery: discovery, correlationTimeout: TimeSpan.FromMilliseconds(150));
        var runtime = await fixture.Application.StartInteractiveTerminalAsync(fixture.Session.InteractiveAgentSessionId, new StartInteractiveTerminalRequest());
        Assert.Equal(InteractiveTerminalRuntimeStatus.Running, runtime.Status);
        Assert.Null((await fixture.Application.GetInteractiveAgentSessionAsync(fixture.Session.InteractiveAgentSessionId)).ProviderSessionId);
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task TerminalRuntime_Resumes_Exact_Canonical_Provider_And_Rejects_Mismatch()
    {
        var discovery = new TestProviderSessionDiscovery([]);
        var fixture = await TerminalFixture.CreateAsync(discovery: discovery);
        await fixture.Sessions.RecordProviderSessionIdentityAsync(fixture.Session.InteractiveAgentSessionId, "canonical-a", ProviderSessionIdentitySource.LaunchCorrelation);
        var runtime = await fixture.Application.StartInteractiveTerminalAsync(fixture.Session.InteractiveAgentSessionId, new StartInteractiveTerminalRequest());
        var session = await fixture.Application.GetInteractiveAgentSessionAsync(fixture.Session.InteractiveAgentSessionId);
        Assert.Equal(0, discovery.CallCount);
        Assert.Equal(new[] { "--session", "canonical-a" }, fixture.Runtime.Arguments.TakeLast(2));
        Assert.Equal("canonical-a", runtime.ProviderSessionId);
        Assert.Equal(ProviderSessionIdentitySource.ExistingCanonicalIdentity, session.ProviderSessionIdentitySource);
        var mismatch = await Assert.ThrowsAsync<OpenCodeWorkspaceMcpException>(() => fixture.Sessions.RecordProviderSessionIdentityAsync(fixture.Session.InteractiveAgentSessionId, "canonical-b", ProviderSessionIdentitySource.LaunchCorrelation));
        Assert.Equal("provider_session_mismatch", mismatch.Code);
        Assert.Equal("canonical-a", (await fixture.Application.GetInteractiveAgentSessionAsync(fixture.Session.InteractiveAgentSessionId)).ProviderSessionId);
    }

    [Theory]
    [Trait("Category", "FastIntegration")]
    [InlineData(InteractiveTerminalRuntimeStatus.Starting, InteractiveTerminalRuntimeStatus.Unavailable)]
    [InlineData(InteractiveTerminalRuntimeStatus.Running, InteractiveTerminalRuntimeStatus.Unavailable)]
    [InlineData(InteractiveTerminalRuntimeStatus.Stopping, InteractiveTerminalRuntimeStatus.Exited)]
    [InlineData(InteractiveTerminalRuntimeStatus.Exited, InteractiveTerminalRuntimeStatus.Exited)]
    [InlineData(InteractiveTerminalRuntimeStatus.Failed, InteractiveTerminalRuntimeStatus.Failed)]
    public async Task TerminalRuntime_Restart_Normalizes_Persisted_Status(InteractiveTerminalRuntimeStatus persistedStatus, InteractiveTerminalRuntimeStatus expectedStatus)
    {
        var root = Path.Combine(Path.GetTempPath(), $"terminal-runtime-normalization-{Guid.NewGuid():N}");
        var store = new LocalHostStateStore(new DefaultLocalHostStatePathProvider(new LocalHostStateOptions { StateRoot = root }));
        var sessionId = $"interactive-{persistedStatus}";
        var now = DateTimeOffset.UtcNow;
        await store.WriteJsonAsync(Path.Combine(store.InteractiveSessionsRoot, sessionId, "terminal-runtime.json"), new PersistedTerminalRuntimeMetadata
        {
            TerminalRuntimeId = "runtime-old",
            InteractiveAgentSessionId = sessionId,
            WorkspaceId = "alpha",
            ProviderSessionId = "provider-1",
            Status = persistedStatus,
            ProcessId = Environment.ProcessId,
            ProcessStartedUtc = now.AddMinutes(-2),
            CreatedUtc = now.AddMinutes(-2),
            UpdatedUtc = now.AddMinutes(-1),
            LastActivityUtc = now.AddMinutes(-1),
            ExitCode = persistedStatus == InteractiveTerminalRuntimeStatus.Exited ? 0 : null,
            Columns = 132,
            Rows = 41,
            FailureSummary = persistedStatus == InteractiveTerminalRuntimeStatus.Failed ? "provider failed" : string.Empty,
        });
        var workspaceService = CreateWorkspaceService();
        var sessions = CreateService(store, workspaceService);
        var terminals = new InteractiveTerminalRuntimeService(sessions, workspaceService, new SystemClock(), store, runtimeFactory: () => new TestTerminalRuntime());

        var recovered = await terminals.GetAsync(sessionId, CancellationToken.None);

        Assert.Equal("runtime-old", recovered.RuntimeId);
        Assert.Equal("provider-1", recovered.ProviderSessionId);
        Assert.Equal(expectedStatus, recovered.Status);
        Assert.Equal(Environment.ProcessId, recovered.ProcessId);
        Assert.Equal(new InteractiveTerminalDimensions { Columns = 132, Rows = 41 }, recovered.Dimensions);
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task TerminalRuntime_Restart_Preserves_Conversation_But_Not_Pty_Attachment_Or_Output()
    {
        var discovery = new TestProviderSessionDiscovery([Set("old"), Set("old", "provider-1")]);
        var first = await TerminalFixture.CreateAsync(discovery: discovery);
        var oldRuntime = await first.Application.StartInteractiveTerminalAsync(first.Session.InteractiveAgentSessionId, new StartInteractiveTerminalRequest());
        var oldAttachment = await first.AttachAsync("client-a");
        await first.Application.ActivateInteractiveSessionAttachmentAsync(first.Session.InteractiveAgentSessionId, oldAttachment.Attachment.Attachment.AttachmentId, new ActivateInteractiveSessionAttachmentRequest { AttachmentToken = oldAttachment.Token, HelperProcessId = 10 });
        first.Runtime.EmitOutput([0x00, 0xff, 0x41]);
        Assert.Equal("provider-1", oldRuntime.ProviderSessionId);

        var restarted = await first.RestartAsync();
        var recoveredSession = await restarted.Application.GetInteractiveAgentSessionAsync(first.Session.InteractiveAgentSessionId);
        var recoveredAttachments = await restarted.Application.GetInteractiveAttachmentsAsync(first.Session.InteractiveAgentSessionId);
        var recoveredRuntime = await restarted.Application.GetInteractiveTerminalAsync(first.Session.InteractiveAgentSessionId);
        var recoveredOutput = await restarted.Application.GetInteractiveTerminalOutputAsync(first.Session.InteractiveAgentSessionId, 0);

        Assert.Equal(InteractiveAgentSessionStatus.Detached, recoveredSession.Status);
        Assert.Equal(string.Empty, recoveredSession.ActiveAttachmentId);
        Assert.Null(recoveredSession.ActiveLease);
        Assert.Contains(recoveredAttachments, item => item.AttachmentId == oldAttachment.Attachment.Attachment.AttachmentId && item.Status == InteractiveAttachmentStatus.Detached && item.DetachReason == "local_host_restarted");
        Assert.Equal("provider-1", recoveredSession.ProviderSessionId);
        Assert.Equal(oldRuntime.RuntimeId, recoveredRuntime.RuntimeId);
        Assert.Equal(InteractiveTerminalRuntimeStatus.Unavailable, recoveredRuntime.Status);
        Assert.Equal("provider-1", recoveredRuntime.ProviderSessionId);
        Assert.Empty(recoveredOutput.Chunks);
        Assert.Equal(0, recoveredOutput.LatestSequence);
        var oldAuthority = await Assert.ThrowsAsync<OpenCodeWorkspaceMcpException>(() => restarted.Sessions.ValidateTerminalInputAuthorityAsync(first.Session.InteractiveAgentSessionId, oldAttachment.Attachment.Attachment.AttachmentId, oldAttachment.Token));
        Assert.Equal("attachment_not_active", oldAuthority.Code);

        var newRuntime = await restarted.Application.StartInteractiveTerminalAsync(first.Session.InteractiveAgentSessionId, new StartInteractiveTerminalRequest());
        Assert.NotEqual(oldRuntime.RuntimeId, newRuntime.RuntimeId);
        Assert.Equal("provider-1", newRuntime.ProviderSessionId);
        Assert.Equal(new[] { "--session", "provider-1" }, restarted.Runtime.Arguments.TakeLast(2));

        var persistedJson = await File.ReadAllTextAsync(Path.Combine(first.Store.InteractiveSessionsRoot, first.Session.InteractiveAgentSessionId, "terminal-runtime.json"));
        Assert.DoesNotContain("DataBase64", persistedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("EarliestSequence", persistedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("LatestSequence", persistedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("AttachmentToken", persistedJson, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task TerminalRuntime_Takeover_Revokes_Old_Authority_Without_Restart()
    {
        var fixture = await TerminalFixture.CreateAsync();
        var started = await fixture.Application.StartInteractiveTerminalAsync(fixture.Session.InteractiveAgentSessionId, new StartInteractiveTerminalRequest());
        var a = await fixture.AttachAsync("client-a");
        await fixture.Application.ActivateInteractiveSessionAttachmentAsync(fixture.Session.InteractiveAgentSessionId, a.Attachment.Attachment.AttachmentId, new ActivateInteractiveSessionAttachmentRequest { AttachmentToken = a.Token, HelperProcessId = 10 });
        var takeover = fixture.Application.AttachInteractiveSessionAsync(fixture.Session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = fixture.Session.InteractiveAgentSessionId, CommandId = "takeover", ClientInstanceId = "client-b", RequestTransfer = true });
        await Task.Delay(50);
        var oldInput = await Assert.ThrowsAsync<OpenCodeWorkspaceMcpException>(() => fixture.Application.SendInteractiveTerminalInputAsync(fixture.Session.InteractiveAgentSessionId, new TerminalInputRequest { AttachmentId = a.Attachment.Attachment.AttachmentId, AttachmentToken = a.Token, DataBase64 = "AA==" }));
        var oldResize = await Assert.ThrowsAsync<OpenCodeWorkspaceMcpException>(() => fixture.Application.ResizeInteractiveTerminalAsync(fixture.Session.InteractiveAgentSessionId, new TerminalResizeRequest { AttachmentId = a.Attachment.Attachment.AttachmentId, AttachmentToken = a.Token, Columns = 140, Rows = 40 }));
        Assert.Equal("attachment_not_active", oldInput.Code);
        Assert.Equal("attachment_not_active", oldResize.Code);
        await fixture.Application.ReportInteractiveSessionAttachmentProcessExitAsync(fixture.Session.InteractiveAgentSessionId, a.Attachment.Attachment.AttachmentId, new InteractiveSessionAttachmentProcessExitRequest { AttachmentToken = a.Token, Outcome = "detach_requested" });
        var b = await takeover;
        var bToken = ExtractAttachmentToken(b);
        await fixture.Application.ActivateInteractiveSessionAttachmentAsync(fixture.Session.InteractiveAgentSessionId, b.Attachment.AttachmentId, new ActivateInteractiveSessionAttachmentRequest { AttachmentToken = bToken, HelperProcessId = 11 });
        await fixture.Application.SendInteractiveTerminalInputAsync(fixture.Session.InteractiveAgentSessionId, new TerminalInputRequest { AttachmentId = b.Attachment.AttachmentId, AttachmentToken = bToken, DataBase64 = "AP8=" });
        await fixture.Application.ResizeInteractiveTerminalAsync(fixture.Session.InteractiveAgentSessionId, new TerminalResizeRequest { AttachmentId = b.Attachment.AttachmentId, AttachmentToken = bToken, Columns = 150, Rows = 45 });
        var current = await fixture.Application.GetInteractiveTerminalAsync(fixture.Session.InteractiveAgentSessionId);
        Assert.Equal(started.RuntimeId, current.RuntimeId);
        Assert.Equal(started.ProcessId, current.ProcessId);
        Assert.Equal(0, fixture.Runtime.StopCount);
        Assert.Equal(new byte[] { 0x00, 0xff }, fixture.Runtime.ReceivedInput.Single());
        Assert.Equal(new InteractiveTerminalDimensions { Columns = 150, Rows = 45 }, fixture.Runtime.ResizeHistory.Single());
        var oldHeartbeat = await fixture.Application.HeartbeatInteractiveSessionAttachmentAsync(fixture.Session.InteractiveAgentSessionId, a.Attachment.Attachment.AttachmentId, new InteractiveSessionAttachmentHeartbeatRequest { AttachmentToken = a.Token });
        Assert.NotEqual(a.Attachment.Attachment.AttachmentId, oldHeartbeat.Session.ActiveAttachmentId);
    }

    private static IReadOnlySet<string> Set(params string[] values) => new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);

    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task Restart_Normalizes_Attached_Session_To_Detached_And_Preserves_Identity()
    {
        var root = Path.Combine(Path.GetTempPath(), $"interactive-session-state-{Guid.NewGuid():N}");
        var store = new LocalHostStateStore(new DefaultLocalHostStatePathProvider(new LocalHostStateOptions { StateRoot = root }));
        var session = new InteractiveAgentSessionRecord
        {
            InteractiveAgentSessionId = "interactive-alpha-1",
            WorkspaceId = "alpha",
            WorkspaceInstanceId = "workspace-alpha",
            ProviderSessionId = "provider-1",
            Title = "OpenCode session - alpha",
            Status = InteractiveAgentSessionStatus.Attached,
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            LastActivityUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            ActiveAttachmentId = "attachment-1",
            ActiveLease = new InteractiveAttachmentLease { InteractiveAgentSessionId = "interactive-alpha-1", AttachmentId = "attachment-1", HolderKind = "WindowsTerminal", HolderClientInstanceId = "client-1", AcquiredUtc = DateTimeOffset.UtcNow.AddMinutes(-1), LeaseExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(1), LastHeartbeatUtc = DateTimeOffset.UtcNow.AddMinutes(-1), Version = 1 },
        };
        await store.WriteJsonAsync(Path.Combine(store.InteractiveSessionsRoot, session.InteractiveAgentSessionId, "session.json"), session);

        var service = CreateService(store, new FakeApiService());

        var loaded = await service.GetAsync(session.InteractiveAgentSessionId);

        Assert.Equal(session.InteractiveAgentSessionId, loaded.InteractiveAgentSessionId);
        Assert.Equal("provider-1", loaded.ProviderSessionId);
        Assert.Equal(InteractiveAgentSessionStatus.Detached, loaded.Status);
        Assert.Equal(string.Empty, loaded.ActiveAttachmentId);
        Assert.Null(loaded.ActiveLease);
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    public void ArchitectureGuard_InteractiveSessionContract_And_Service_AreTransportNeutral()
    {
        var repoRoot = TestPaths.RepositoryRoot;
        var contracts = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.LocalClient", "LocalHostContracts.cs"));
        var services = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Api", "LocalHostServices.cs"));
        var contractBlock = contracts[contracts.IndexOf("public sealed record InteractiveAgentSessionRecord", StringComparison.Ordinal)..contracts.IndexOf("public sealed record WorkspaceEventEnvelope", StringComparison.Ordinal)];
        var serviceBlock = services[services.IndexOf("public sealed class InteractiveAgentSessionService", StringComparison.Ordinal)..services.IndexOf("public sealed class InteractiveSessionAttachmentService", StringComparison.Ordinal)];

        Assert.DoesNotContain("Avalonia", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("ControllerSessionRecord", contractBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowsTerminalAttachmentProvider", serviceBlock, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task Attach_Granted_Updates_Session_And_Produces_Approved_Launch_Descriptor()
    {
        var root = Path.Combine(Path.GetTempPath(), $"interactive-session-state-{Guid.NewGuid():N}");
        var store = new LocalHostStateStore(new DefaultLocalHostStatePathProvider(new LocalHostStateOptions { StateRoot = root }));
        var service = CreateService(store, CreateWorkspaceService());
        var session = await service.CreateAsync(new CreateInteractiveAgentSessionRequest { CommandId = "cmd-1", WorkspaceId = "alpha", Title = "OpenCode session - alpha" });

        var attached = await service.AttachAsync(session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = session.InteractiveAgentSessionId, CommandId = "attach-1", ClientInstanceId = "client-1" });

        Assert.Equal(InteractiveAgentSessionStatus.Starting, attached.Session.Status);
        Assert.Equal(attached.Attachment.AttachmentId, attached.Session.ActiveAttachmentId);
        Assert.Equal("wt.exe", attached.LaunchDescriptor.FileName);
        Assert.Equal(InteractiveAttachmentStatus.Pending, attached.Attachment.Status);
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task Concurrent_Attach_Produces_One_Winner_And_One_AlreadyAttached()
    {
        var root = Path.Combine(Path.GetTempPath(), $"interactive-session-state-{Guid.NewGuid():N}");
        var store = new LocalHostStateStore(new DefaultLocalHostStatePathProvider(new LocalHostStateOptions { StateRoot = root }));
        var service = CreateService(store, CreateWorkspaceService());
        var session = await service.CreateAsync(new CreateInteractiveAgentSessionRequest { CommandId = "cmd-1", WorkspaceId = "alpha", Title = "OpenCode session - alpha" });

        var first = service.AttachAsync(session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = session.InteractiveAgentSessionId, CommandId = "attach-1", ClientInstanceId = "client-1" });
        var second = service.AttachAsync(session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = session.InteractiveAgentSessionId, CommandId = "attach-2", ClientInstanceId = "client-2" });

        var results = await Task.WhenAll(Wrap(first), Wrap(second));

        Assert.Single(results, item => item.Success);
        Assert.Single(results, item => !item.Success && item.ErrorCode == "already_attached");
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task Detach_Preserves_Session_Identity_And_Clears_Active_Attachment()
    {
        var root = Path.Combine(Path.GetTempPath(), $"interactive-session-state-{Guid.NewGuid():N}");
        var store = new LocalHostStateStore(new DefaultLocalHostStatePathProvider(new LocalHostStateOptions { StateRoot = root }));
        var service = CreateService(store, CreateWorkspaceService());
        var session = await service.CreateAsync(new CreateInteractiveAgentSessionRequest { CommandId = "cmd-1", WorkspaceId = "alpha", Title = "OpenCode session - alpha" });
        var attached = await service.AttachAsync(session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = session.InteractiveAgentSessionId, CommandId = "attach-1", ClientInstanceId = "client-1" });
        var token = ExtractAttachmentToken(attached);

        _ = await service.RequestDetachAsync(session.InteractiveAgentSessionId, attached.Attachment.AttachmentId, new DetachInteractiveSessionAttachmentRequest { ClientInstanceId = "client-1", Reason = "user_detach" });
        var detached = await service.ReportProcessExitAsync(session.InteractiveAgentSessionId, attached.Attachment.AttachmentId, new InteractiveSessionAttachmentProcessExitRequest { AttachmentToken = token, Outcome = "detach_requested" });

        Assert.Equal(session.InteractiveAgentSessionId, detached.InteractiveAgentSessionId);
        Assert.Equal(InteractiveAgentSessionStatus.Detached, detached.Status);
        Assert.Equal(string.Empty, detached.ActiveAttachmentId);
        Assert.Null(detached.ActiveLease);
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task Output_Gap_Is_Explicit_And_Does_Not_Fabricate_Bytes()
    {
        var root = TestPaths.RepositoryRoot;
        var source = File.ReadAllText(Path.Combine(root, "src", "OpenCode.Workspace.Api", "InteractiveTerminalRuntimeService.cs"));

        Assert.Contains("GapDetected = gap", source, StringComparison.Ordinal);
        Assert.Contains("effectiveAfter = gap ? Record.EarliestSequence - 1", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    public void ArchitectureGuard_TerminalRuntime_Launches_LocalHost_Resolved_Provider_Not_Wrapper()
    {
        var source = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Api", "InteractiveTerminalRuntimeService.cs"));
        var runtime = source[..source.IndexOf("internal sealed class WindowsConPtyTerminalRuntime", StringComparison.Ordinal)];

        Assert.DoesNotContain("attach-workspace.ps1", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("AttachWrapperScriptPath", runtime, StringComparison.Ordinal);
        Assert.Contains("InteractiveProviderLaunchSpecification", runtime, StringComparison.Ordinal);
        Assert.Contains("docker.exe", runtime, StringComparison.Ordinal);
        Assert.Contains("--session", runtime, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task Lease_Expiry_Allows_New_Attach_And_Persists_Expired_History()
    {
        var root = Path.Combine(Path.GetTempPath(), $"interactive-session-state-{Guid.NewGuid():N}");
        var store = new LocalHostStateStore(new DefaultLocalHostStatePathProvider(new LocalHostStateOptions { StateRoot = root }));
        var service = CreateService(store, CreateWorkspaceService(), new InteractiveAttachmentLeasePolicy { StartupLeaseDuration = TimeSpan.FromMilliseconds(50), ActiveLeaseDuration = TimeSpan.FromMilliseconds(50), TransferTimeout = TimeSpan.FromMilliseconds(100), TransferPollInterval = TimeSpan.FromMilliseconds(10) });
        var session = await service.CreateAsync(new CreateInteractiveAgentSessionRequest { CommandId = "cmd-1", WorkspaceId = "alpha", Title = "OpenCode session - alpha" });
        var attached = await service.AttachAsync(session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = session.InteractiveAgentSessionId, CommandId = "attach-1", ClientInstanceId = "client-1" });
        await Task.Delay(80);

        var replacement = await service.AttachAsync(session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = session.InteractiveAgentSessionId, CommandId = "attach-2", ClientInstanceId = "client-2" });
        var history = await service.GetAttachmentsAsync(session.InteractiveAgentSessionId);

        Assert.NotEqual(attached.Attachment.AttachmentId, replacement.Attachment.AttachmentId);
        Assert.Contains(history, item => item.AttachmentId == attached.Attachment.AttachmentId && item.Status == InteractiveAttachmentStatus.Expired);
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task Resume_Reuses_ProviderSessionId_When_Present()
    {
        var root = Path.Combine(Path.GetTempPath(), $"interactive-session-state-{Guid.NewGuid():N}");
        var store = new LocalHostStateStore(new DefaultLocalHostStatePathProvider(new LocalHostStateOptions { StateRoot = root }));
        var persisted = new InteractiveAgentSessionRecord
        {
            InteractiveAgentSessionId = "interactive-alpha-1",
            WorkspaceId = "alpha",
            WorkspaceInstanceId = "workspace-alpha",
            ProviderSessionId = "provider-1",
            Title = "OpenCode session - alpha",
            Status = InteractiveAgentSessionStatus.Detached,
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            LastActivityUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        };
        await store.WriteJsonAsync(Path.Combine(store.InteractiveSessionsRoot, persisted.InteractiveAgentSessionId, "session.json"), persisted);
        var service = CreateService(store, CreateWorkspaceService());

        var attached = await service.AttachAsync(persisted.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = persisted.InteractiveAgentSessionId, CommandId = "attach-1", ClientInstanceId = "client-1" });

        Assert.Equal("provider-1", attached.Session.ProviderSessionId);
        Assert.Equal("provider-1", attached.Attachment.ProviderSessionId);
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task Transfer_Succeeds_After_Current_Owner_Detaches()
    {
        var root = Path.Combine(Path.GetTempPath(), $"interactive-session-state-{Guid.NewGuid():N}");
        var store = new LocalHostStateStore(new DefaultLocalHostStatePathProvider(new LocalHostStateOptions { StateRoot = root }));
        var service = CreateService(store, CreateWorkspaceService(), new InteractiveAttachmentLeasePolicy { StartupLeaseDuration = TimeSpan.FromSeconds(5), ActiveLeaseDuration = TimeSpan.FromSeconds(5), TransferTimeout = TimeSpan.FromSeconds(1), TransferPollInterval = TimeSpan.FromMilliseconds(20) });
        var session = await service.CreateAsync(new CreateInteractiveAgentSessionRequest { CommandId = "cmd-1", WorkspaceId = "alpha", Title = "OpenCode session - alpha" });
        var attached = await service.AttachAsync(session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = session.InteractiveAgentSessionId, CommandId = "attach-1", ClientInstanceId = "client-1" });
        var token = ExtractAttachmentToken(attached);

        var takeoverTask = service.AttachAsync(session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = session.InteractiveAgentSessionId, CommandId = "attach-2", ClientInstanceId = "client-2", RequestTransfer = true });
        await Task.Delay(100);
        await service.ReportProcessExitAsync(session.InteractiveAgentSessionId, attached.Attachment.AttachmentId, new InteractiveSessionAttachmentProcessExitRequest { AttachmentToken = token, Outcome = "detach_requested" });
        var takeover = await takeoverTask;

        Assert.Equal("client-2", takeover.Attachment.ClientInstanceId);
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task Transfer_Times_Out_With_Rejection_When_Current_Owner_Remains_Attached()
    {
        var root = Path.Combine(Path.GetTempPath(), $"interactive-session-state-{Guid.NewGuid():N}");
        var store = new LocalHostStateStore(new DefaultLocalHostStatePathProvider(new LocalHostStateOptions { StateRoot = root }));
        var service = CreateService(store, CreateWorkspaceService(), new InteractiveAttachmentLeasePolicy { StartupLeaseDuration = TimeSpan.FromSeconds(5), ActiveLeaseDuration = TimeSpan.FromSeconds(5), TransferTimeout = TimeSpan.FromMilliseconds(150), TransferPollInterval = TimeSpan.FromMilliseconds(20) });
        var session = await service.CreateAsync(new CreateInteractiveAgentSessionRequest { CommandId = "cmd-1", WorkspaceId = "alpha", Title = "OpenCode session - alpha" });
        _ = await service.AttachAsync(session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = session.InteractiveAgentSessionId, CommandId = "attach-1", ClientInstanceId = "client-1" });

        var error = await Assert.ThrowsAsync<OpenCodeWorkspaceMcpException>(() => service.AttachAsync(session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = session.InteractiveAgentSessionId, CommandId = "attach-2", ClientInstanceId = "client-2", RequestTransfer = true }));

        Assert.Equal("transfer_rejected", error.Code);
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task Restart_Invalidates_Attachment_And_Recovery_Credentials()
    {
        var root = Path.Combine(Path.GetTempPath(), $"interactive-session-state-{Guid.NewGuid():N}");
        var store = new LocalHostStateStore(new DefaultLocalHostStatePathProvider(new LocalHostStateOptions { StateRoot = root }));
        var service = CreateService(store, CreateWorkspaceService());
        var session = await service.CreateAsync(new CreateInteractiveAgentSessionRequest { CommandId = "cmd-1", WorkspaceId = "alpha", Title = "OpenCode session - alpha" });
        var attached = await service.AttachAsync(session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = session.InteractiveAgentSessionId, CommandId = "attach-1", ClientInstanceId = "client-1" });
        var token = ExtractAttachmentToken(attached);
        var recoveryId = ExtractLaunchArgument(attached, "--attachment-recovery-id");
        var recoverySecret = ExtractLaunchArgument(attached, "--recovery-secret");

        _ = await service.ActivateAsync(session.InteractiveAgentSessionId, attached.Attachment.AttachmentId, new ActivateInteractiveSessionAttachmentRequest { AttachmentToken = token, HelperProcessId = 101 });
        _ = await service.ReportProcessStartedAsync(session.InteractiveAgentSessionId, attached.Attachment.AttachmentId, new InteractiveSessionAttachmentProcessStartedRequest { AttachmentToken = token, ChildProcessId = 202 });

        File.WriteAllText(store.ShutdownMarkerPath, string.Empty);
        File.Delete(store.ShutdownMarkerPath);
        var restarted = CreateService(new LocalHostStateStore(new DefaultLocalHostStatePathProvider(new LocalHostStateOptions { StateRoot = root })), CreateWorkspaceService());

        var recoveryError = await Assert.ThrowsAsync<OpenCodeWorkspaceMcpException>(() => restarted.RecoverAsync(session.InteractiveAgentSessionId, attached.Attachment.AttachmentId, new RecoverInteractiveSessionAttachmentRequest
        {
            AttachmentRecoveryId = recoveryId,
            RecoverySecret = recoverySecret,
            HelperProcessId = 101,
            HelperStartedUtc = DateTimeOffset.UtcNow,
            ChildProcessId = 202,
            ChildStartedUtc = DateTimeOffset.UtcNow,
        }));

        Assert.Equal("recovery_not_allowed", recoveryError.Code);
        var oldTokenError = await Assert.ThrowsAsync<OpenCodeWorkspaceMcpException>(() => restarted.ValidateTerminalInputAuthorityAsync(session.InteractiveAgentSessionId, attached.Attachment.AttachmentId, token));
        Assert.Equal("attachment_not_active", oldTokenError.Code);
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task Recovery_Rejects_Invalid_Secret()
    {
        var root = Path.Combine(Path.GetTempPath(), $"interactive-session-state-{Guid.NewGuid():N}");
        var store = new LocalHostStateStore(new DefaultLocalHostStatePathProvider(new LocalHostStateOptions { StateRoot = root }));
        var service = CreateService(store, CreateWorkspaceService());
        var session = await service.CreateAsync(new CreateInteractiveAgentSessionRequest { CommandId = "cmd-1", WorkspaceId = "alpha", Title = "OpenCode session - alpha" });
        var attached = await service.AttachAsync(session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = session.InteractiveAgentSessionId, CommandId = "attach-1", ClientInstanceId = "client-1" });

        var error = await Assert.ThrowsAsync<OpenCodeWorkspaceMcpException>(() => service.RecoverAsync(session.InteractiveAgentSessionId, attached.Attachment.AttachmentId, new RecoverInteractiveSessionAttachmentRequest
        {
            AttachmentRecoveryId = ExtractLaunchArgument(attached, "--attachment-recovery-id"),
            RecoverySecret = "wrong",
            HelperProcessId = 1,
            HelperStartedUtc = DateTimeOffset.UtcNow,
        }));

        Assert.Equal("invalid_recovery_proof", error.Code);
    }

    private static InteractiveAgentSessionService CreateService(LocalHostStateStore store, FakeApiService apiService, InteractiveAttachmentLeasePolicy? policy = null)
        => new(store, apiService, policy ?? new InteractiveAttachmentLeasePolicy(), new InteractiveSessionLaunchDescriptorFactory(), new SystemClock());

    private static string ExtractAttachmentToken(InteractiveSessionAttachResult attached)
        => ExtractLaunchArgument(attached, "--attachment-token");

    private static string ExtractLaunchArgument(InteractiveSessionAttachResult attached, string name)
        => attached.LaunchDescriptor.Arguments[Array.IndexOf(attached.LaunchDescriptor.Arguments.ToArray(), name) + 1];

    private static FakeApiService CreateWorkspaceService()
        => new()
        {
            GetWorkspaceHandler = workspaceId => Task.FromResult(new OpenCode.Workspace.Mcp.WorkspaceRecordModel
            {
                WorkspaceId = workspaceId,
                Name = workspaceId,
                WorkspaceRoot = Path.Combine(Path.GetTempPath(), workspaceId),
                Snapshot = CreateSnapshot(workspaceId),
            }),
        };

    private static WorkspaceSnapshot CreateSnapshot(string workspaceId)
    {
        var root = Path.Combine(Path.GetTempPath(), workspaceId);
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

    private static async Task<(bool Success, string ErrorCode)> Wrap(Task<InteractiveSessionAttachResult> task)
    {
        try
        {
            await task;
            return (true, string.Empty);
        }
        catch (OpenCodeWorkspaceMcpException exception)
        {
            return (false, exception.Code);
        }
    }

    private sealed class TerminalFixture
    {
        public required LocalHostApplicationService Application { get; init; }
        public required InteractiveAgentSessionRecord Session { get; init; }
        public required TestTerminalRuntime Runtime { get; init; }
        public required InteractiveAgentSessionService Sessions { get; init; }
        public required LocalHostStateStore Store { get; init; }
        public required FakeApiService WorkspaceService { get; init; }

        public static async Task<TerminalFixture> CreateAsync(IProviderSessionDiscovery? discovery = null, TimeSpan? correlationTimeout = null)
        {
            var root = Path.Combine(Path.GetTempPath(), $"terminal-runtime-{Guid.NewGuid():N}");
            var store = new LocalHostStateStore(new DefaultLocalHostStatePathProvider(new LocalHostStateOptions { StateRoot = root }));
            var workspaceService = CreateWorkspaceService();
            var sessions = CreateService(store, workspaceService);
            var runtime = new TestTerminalRuntime();
            var terminals = new InteractiveTerminalRuntimeService(sessions, workspaceService, new SystemClock(), store, discovery, () => runtime, correlationTimeout);
            var operations = new TestWorkspaceOperations();
            var application = new LocalHostApplicationService(workspaceService, operations, new WorkspaceInstanceService(store, workspaceService, operations), new ControllerSessionService(store), sessions, new InteractiveSessionAttachmentService(sessions), terminals, new RuntimeResourcesLocalHostService(new OpenCodeWorkspaceMcpOptions { WorkspaceStateRoot = root }, new ProcessRunner()), new ConfigurationBuilder().Build());
            var session = await application.CreateInteractiveAgentSessionAsync(new CreateInteractiveAgentSessionRequest { CommandId = "terminal", WorkspaceId = "alpha", Title = "terminal" });
            return new TerminalFixture { Application = application, Session = session, Runtime = runtime, Sessions = sessions, Store = store, WorkspaceService = workspaceService };
        }

        public async Task<TerminalFixture> RestartAsync()
        {
            var store = new LocalHostStateStore(new DefaultLocalHostStatePathProvider(new LocalHostStateOptions { StateRoot = Store.StateRoot }));
            var sessions = CreateService(store, WorkspaceService);
            var runtime = new TestTerminalRuntime();
            var terminals = new InteractiveTerminalRuntimeService(sessions, WorkspaceService, new SystemClock(), store, runtimeFactory: () => runtime);
            var operations = new TestWorkspaceOperations();
            var application = new LocalHostApplicationService(WorkspaceService, operations, new WorkspaceInstanceService(store, WorkspaceService, operations), new ControllerSessionService(store), sessions, new InteractiveSessionAttachmentService(sessions), terminals, new RuntimeResourcesLocalHostService(new OpenCodeWorkspaceMcpOptions { WorkspaceStateRoot = Store.StateRoot }, new ProcessRunner()), new ConfigurationBuilder().Build());
            var session = await sessions.GetAsync(Session.InteractiveAgentSessionId);
            return new TerminalFixture { Application = application, Session = session, Runtime = runtime, Sessions = sessions, Store = store, WorkspaceService = WorkspaceService };
        }

        public async Task<(InteractiveSessionAttachResult Attachment, string Token)> AttachAsync(string clientId)
        {
            var attached = await Application.AttachInteractiveSessionAsync(Session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest { SessionId = Session.InteractiveAgentSessionId, CommandId = Guid.NewGuid().ToString("n"), ClientInstanceId = clientId });
            return (attached, ExtractAttachmentToken(attached));
        }
    }

    private sealed class TestTerminalRuntime : IInteractiveTerminalRuntime
    {
        private InteractiveTerminalRuntimeRecord _record = new();
        public int StopCount { get; private set; }
        public List<byte[]> ReceivedInput { get; } = [];
        public List<InteractiveTerminalDimensions> ResizeHistory { get; } = [];
        public InteractiveTerminalRuntimeRecord Record => _record;
        public event Action<byte[]>? Output;
        public event Action<InteractiveTerminalRuntimeRecord>? Changed;
        public IReadOnlyList<string> Arguments { get; private set; } = Array.Empty<string>();
        public Task<InteractiveTerminalRuntimeRecord> StartAsync(InteractiveAgentSessionRecord session, string fileName, IReadOnlyList<string> arguments, string workingDirectory, InteractiveTerminalDimensions dimensions, CancellationToken cancellationToken)
        {
            Arguments = arguments.ToArray();
            _record = new InteractiveTerminalRuntimeRecord { InteractiveAgentSessionId = session.InteractiveAgentSessionId, WorkspaceId = session.WorkspaceId, ProviderSessionId = session.ProviderSessionId, ProcessId = 4242, ProcessStartedUtc = DateTimeOffset.UtcNow, Status = InteractiveTerminalRuntimeStatus.Running, CreatedUtc = DateTimeOffset.UtcNow, LastActivityUtc = DateTimeOffset.UtcNow, Dimensions = dimensions };
            return Task.FromResult(_record);
        }
        public Task WriteAsync(byte[] data, CancellationToken cancellationToken) { ReceivedInput.Add(data.ToArray()); return Task.CompletedTask; }
        public Task ResizeAsync(InteractiveTerminalDimensions dimensions, CancellationToken cancellationToken) { ResizeHistory.Add(dimensions); _record = _record with { Dimensions = dimensions }; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) { StopCount++; _record = _record with { Status = InteractiveTerminalRuntimeStatus.Exited, ExitCode = 0 }; Changed?.Invoke(_record); return Task.CompletedTask; }
        public void EmitOutput(byte[] data) => Output?.Invoke(data);
    }

    private sealed class TestProviderSessionDiscovery(IEnumerable<IReadOnlySet<string>> results) : IProviderSessionDiscovery
    {
        private readonly Queue<IReadOnlySet<string>> _results = new(results);
        public int CallCount { get; private set; }
        public Task<IReadOnlySet<string>> ListWorkspaceSessionIdsAsync(string containerName, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_results.Count > 1 ? _results.Dequeue() : _results.Count == 1 ? _results.Peek() : Set());
        }
    }

    private sealed class TestWorkspaceOperations : IWorkspaceOperationService
    {
        public IReadOnlyList<WorkspaceOperationRecord> List() => Array.Empty<WorkspaceOperationRecord>();
        public WorkspaceOperationRecord Get(string operationId, long? afterSequence = null, int? maxEvents = null) => throw new NotSupportedException();
        public Task<WorkspaceOperationRecord> StartAsync(string operationKind, WorkspaceOperationScope scope, string workspaceId, string workspaceInstanceId, OperationInitiator initiatedBy, Func<WorkspaceOperationReporter, CancellationToken, Task<object>> work, string dedupeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceOperationRecord> CancelAsync(string operationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
