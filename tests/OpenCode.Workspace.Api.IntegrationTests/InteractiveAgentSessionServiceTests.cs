using OpenCode.Workspace.Api;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.LocalClient;
using OpenCode.Workspace.Mcp;
using Xunit;

namespace OpenCode.Workspace.Api.IntegrationTests;

public sealed class InteractiveAgentSessionServiceTests
{
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
    public async Task Recovery_Succeeds_After_Restart_With_New_Token_And_Same_AttachmentId()
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

        var recovered = await restarted.RecoverAsync(session.InteractiveAgentSessionId, attached.Attachment.AttachmentId, new RecoverInteractiveSessionAttachmentRequest
        {
            AttachmentRecoveryId = recoveryId,
            RecoverySecret = recoverySecret,
            HelperProcessId = 101,
            HelperStartedUtc = DateTimeOffset.UtcNow,
            ChildProcessId = 202,
            ChildStartedUtc = DateTimeOffset.UtcNow,
        });

        Assert.Equal(attached.Attachment.AttachmentId, recovered.Attachment.AttachmentId);
        Assert.NotEqual(token, recovered.AttachmentToken);
        var oldTokenError = await Assert.ThrowsAsync<OpenCodeWorkspaceMcpException>(() => restarted.HeartbeatAsync(session.InteractiveAgentSessionId, attached.Attachment.AttachmentId, new InteractiveSessionAttachmentHeartbeatRequest { AttachmentToken = token }));
        Assert.Equal("invalid_attachment_credential", oldTokenError.Code);
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

        var restarted = CreateService(new LocalHostStateStore(new DefaultLocalHostStatePathProvider(new LocalHostStateOptions { StateRoot = root })), CreateWorkspaceService());
        var error = await Assert.ThrowsAsync<OpenCodeWorkspaceMcpException>(() => restarted.RecoverAsync(session.InteractiveAgentSessionId, attached.Attachment.AttachmentId, new RecoverInteractiveSessionAttachmentRequest
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
}
