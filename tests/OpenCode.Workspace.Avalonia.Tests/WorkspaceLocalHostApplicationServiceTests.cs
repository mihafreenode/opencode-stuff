using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.LocalClient;

namespace OpenCode.Workspace.Avalonia.Tests;

public sealed class WorkspaceLocalHostApplicationServiceTests
{
    [Fact]
    public async Task StartsDisconnected()
    {
        await using var service = new WorkspaceLocalHostApplicationService();

        Assert.Equal(LocalHostConnectionState.Disconnected, service.ConnectionState);
        Assert.Equal(string.Empty, service.StatusMessage);
        Assert.Equal(0, service.LastObservedSequence);
    }

    [Fact]
    public void ProjectionMapper_LoadResult_UsesCanonicalWorkspaceSnapshot()
    {
        var snapshot = new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord { Name = "alpha", RootPath = "/workspace/alpha", RepositoryPath = "/workspace/alpha", ConfigurationPath = "workspace.yaml" },
            Definition = new WorkspaceDefinition { Workspace = new WorkspaceMetadata { Id = "alpha", Name = "alpha", Image = "ubuntu:24.04" } },
            Paths = WorkspacePathBuilder.Build("/workspace/alpha", "workspace.yaml"),
            ConfigurationPath = "workspace.yaml",
            RuntimeState = WorkspaceRuntimeState.Running,
            Safety = new WorkspaceSafetySnapshot { OverallStatus = WorkspaceSafetyLevel.Protected, Headline = "Protected", Message = "Protected", LocalRecovery = new WorkspaceLocalRecoverySnapshot(), Backup = new WorkspaceBackupSnapshot(), IgnorePolicy = new WorkspaceIgnorePolicyReview(), AdvancedGit = new WorkspaceAdvancedGitSnapshot() },
            Session = new WorkspaceSessionSnapshot(),
            Health = new WorkspaceHealthSnapshot { OverallStatus = WorkspaceHealthStatus.Healthy, Summary = "Ready" },
            Readiness = new WorkspaceReadinessSnapshot { Summary = "Workspace ready." },
            AvailableServices = Array.Empty<WorkspaceServiceInfo>(),
        };
        var mapper = new DesktopWorkspaceProjectionMapper();

        var result = mapper.ToWorkspaceLoadResult(
        [
            new WorkspaceInstanceRecord
            {
                WorkspaceInstanceId = "workspace-alpha",
                WorkspaceId = "alpha",
                WorkspaceName = "alpha",
                RuntimeState = "Running",
                Status = "Healthy",
                RecoveryState = "Ready",
                Workspace = new OpenCode.Workspace.LocalClient.WorkspaceRecordModel { WorkspaceId = "alpha", Name = "alpha", WorkspaceRoot = "/workspace/alpha", Snapshot = snapshot },
            },
        ], []);

        var item = Assert.Single(result.Items);
        Assert.Equal("alpha", item.Record.Name);
        Assert.Equal(snapshot, item.Snapshot);
    }

    [Fact]
    public void ProjectionMapper_OperationResult_PreservesProgressMessage()
    {
        var snapshot = new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord { Name = "alpha", RootPath = "/workspace/alpha", RepositoryPath = "/workspace/alpha", ConfigurationPath = "workspace.yaml" },
            Definition = new WorkspaceDefinition { Workspace = new WorkspaceMetadata { Id = "alpha", Name = "alpha", Image = "ubuntu:24.04" } },
            Paths = WorkspacePathBuilder.Build("/workspace/alpha", "workspace.yaml"),
            ConfigurationPath = "workspace.yaml",
            RuntimeState = WorkspaceRuntimeState.Running,
            Safety = new WorkspaceSafetySnapshot { OverallStatus = WorkspaceSafetyLevel.Protected, Headline = "Protected", Message = "Protected", LocalRecovery = new WorkspaceLocalRecoverySnapshot(), Backup = new WorkspaceBackupSnapshot(), IgnorePolicy = new WorkspaceIgnorePolicyReview(), AdvancedGit = new WorkspaceAdvancedGitSnapshot() },
            Session = new WorkspaceSessionSnapshot(),
            Health = new WorkspaceHealthSnapshot { OverallStatus = WorkspaceHealthStatus.Healthy, Summary = "Ready" },
            Readiness = new WorkspaceReadinessSnapshot { Summary = "Workspace ready." },
            AvailableServices = Array.Empty<WorkspaceServiceInfo>(),
        };
        var mapper = new DesktopWorkspaceProjectionMapper();
        var transcript = new OperationTranscript { OperationName = "prepare_workspace", WorkspaceName = "alpha" };

        var result = mapper.ToWorkspaceOperationResult(
            new WorkspaceOperationRecord { OperationId = "op-1", CurrentPhase = "preparing", ProgressMessage = "Preparing workspace.", Status = WorkspaceOperationStatus.Succeeded },
            new WorkspaceInstanceRecord
            {
                WorkspaceInstanceId = "workspace-alpha",
                WorkspaceId = "alpha",
                WorkspaceName = "alpha",
                RuntimeState = "Running",
                Status = "Healthy",
                RecoveryState = "Ready",
                Workspace = new OpenCode.Workspace.LocalClient.WorkspaceRecordModel { WorkspaceId = "alpha", Name = "alpha", WorkspaceRoot = "/workspace/alpha", Snapshot = snapshot },
            },
            transcript);

        Assert.Equal("Preparing workspace.", result.Message);
        Assert.Equal(snapshot, result.Snapshot);
        Assert.Equal(transcript, result.Transcript);
    }
}
