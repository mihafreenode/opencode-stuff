using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Avalonia.ViewModels;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using Xunit;

namespace OpenCode.Workspace.Avalonia.Tests;

public sealed class WorkspacePresentationStateResolverTests
{
    private readonly WorkspacePresentationStateResolver _resolver = new();

    public static IEnumerable<object[]> MatrixCases()
    {
        yield return ["new", WorkspaceReadinessStatus.Unavailable, WorkspaceRuntimeState.Stopped, true, true, false, WorkspacePresentationStatusKind.Provisioning, "Provisioning", WorkspacePresentedActionKind.OpenWorkspace, false, "Provisioning is in progress."];
        yield return ["provisioning", WorkspaceReadinessStatus.Preparing, WorkspaceRuntimeState.Stopped, false, true, false, WorkspacePresentationStatusKind.Provisioning, "Provisioning", WorkspacePresentedActionKind.OpenWorkspace, false, "Provisioning workspace..."];
        yield return ["provisioning-failed", WorkspaceReadinessStatus.ProvisioningFailed, WorkspaceRuntimeState.Stopped, false, true, false, WorkspacePresentationStatusKind.ProvisioningFailed, "Provisioning Failed", WorkspacePresentedActionKind.RetryProvisioning, true, string.Empty];
        yield return ["ready", WorkspaceReadinessStatus.Ready, WorkspaceRuntimeState.Running, false, false, false, WorkspacePresentationStatusKind.Ready, "Workspace Ready", WorkspacePresentedActionKind.OpenWorkspace, true, string.Empty];
        yield return ["stopped", WorkspaceReadinessStatus.Unavailable, WorkspaceRuntimeState.Stopped, false, false, false, WorkspacePresentationStatusKind.Stopped, "Stopped", WorkspacePresentedActionKind.OpenWorkspace, true, string.Empty];
        yield return ["needs-rebuild", WorkspaceReadinessStatus.NeedsRebuild, WorkspaceRuntimeState.Running, false, false, false, WorkspacePresentationStatusKind.NeedsRebuild, "Needs Rebuild", WorkspacePresentedActionKind.RebuildRuntime, true, string.Empty];
        yield return ["needs-recovery", WorkspaceReadinessStatus.Unavailable, WorkspaceRuntimeState.Stopped, false, true, false, WorkspacePresentationStatusKind.NeedsRecovery, "Needs Preparation", WorkspacePresentedActionKind.OpenWorkspace, true, string.Empty];
        yield return ["invalid", WorkspaceReadinessStatus.Unavailable, WorkspaceRuntimeState.Stopped, false, false, true, WorkspacePresentationStatusKind.Invalid, "Discovery Failed", WorkspacePresentedActionKind.RunDiagnostics, true, string.Empty];
    }

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public void Resolve_BuildsExpectedPrimaryState(
        string name,
        WorkspaceReadinessStatus readinessStatus,
        WorkspaceRuntimeState runtimeState,
        bool isFreshWorkspace,
        bool requiresPreparation,
        bool missingSnapshot,
        WorkspacePresentationStatusKind expectedStatus,
        string expectedLabel,
        WorkspacePresentedActionKind expectedPrimaryAction,
        bool expectedPrimaryEnabled,
        string expectedDisabledReason)
    {
        var context = CreateContext(name, readinessStatus, runtimeState, isFreshWorkspace, requiresPreparation, missingSnapshot, readinessStatus == WorkspaceReadinessStatus.Preparing, readinessStatus == WorkspaceReadinessStatus.Preparing ? "Provisioning workspace..." : string.Empty);

        var state = _resolver.Resolve(context);

        Assert.Equal(expectedStatus, state.Status);
        Assert.Equal(expectedLabel, state.StatusLabel);
        Assert.NotNull(state.PrimaryAction);
        Assert.Equal(expectedPrimaryAction, state.PrimaryAction!.Kind);
        Assert.Equal(expectedPrimaryEnabled, state.PrimaryAction.IsEnabled);
        Assert.Equal(expectedDisabledReason, state.PrimaryAction.DisabledReason);
    }

    [Fact]
    public void Resolve_NeedsRebuild_ExposesExecutableRebuildAction()
    {
        var state = _resolver.Resolve(CreateContext("rebuild", WorkspaceReadinessStatus.NeedsRebuild, WorkspaceRuntimeState.Running, false, false, false, false, string.Empty));

        Assert.Equal(WorkspacePresentationStatusKind.NeedsRebuild, state.Status);
        Assert.Equal(WorkspacePresentedActionKind.RebuildRuntime, state.PrimaryAction?.Kind);
        Assert.True(state.PrimaryAction?.IsEnabled);
        Assert.Contains(state.AdvancedActions, action => action.Kind == WorkspacePresentedActionKind.RebuildRuntime && action.IsEnabled);
    }

    [Fact]
    public void Resolve_ProvisioningFailed_ExposesExecutableRetryProvisioningAction()
    {
        var state = _resolver.Resolve(CreateContext("failed", WorkspaceReadinessStatus.ProvisioningFailed, WorkspaceRuntimeState.Stopped, false, true, false, false, string.Empty));

        Assert.Equal(WorkspacePresentationStatusKind.ProvisioningFailed, state.Status);
        Assert.Equal(WorkspacePresentedActionKind.RetryProvisioning, state.PrimaryAction?.Kind);
        Assert.True(state.PrimaryAction?.IsEnabled);
        Assert.Contains(state.AdvancedActions, action => action.Kind == WorkspacePresentedActionKind.RetryProvisioning && action.IsEnabled);
    }

    [Fact]
    public void Resolve_ActiveOperation_DisablesActionsWithExplicitReason()
    {
        var state = _resolver.Resolve(CreateContext("busy", WorkspaceReadinessStatus.Ready, WorkspaceRuntimeState.Running, false, false, false, true, "Workspace validation is running."));

        Assert.False(state.PrimaryAction?.IsEnabled);
        Assert.Equal("Workspace validation is running.", state.PrimaryAction?.DisabledReason);
        Assert.All(state.AvailableServices, service => Assert.Equal("Workspace validation is running.", service.UnavailableReason));
    }

    [Fact]
    public void Resolve_OperationComplete_ReEnablesNormalAction()
    {
        var state = _resolver.Resolve(CreateContext("ready", WorkspaceReadinessStatus.Ready, WorkspaceRuntimeState.Running, false, false, false, false, string.Empty));

        Assert.Equal(WorkspacePresentationStatusKind.Ready, state.Status);
        Assert.True(state.PrimaryAction?.IsEnabled);
        Assert.Equal(WorkspacePresentedActionKind.OpenWorkspace, state.PrimaryAction?.Kind);
    }

    [Fact]
    public void Resolve_ServiceReason_AgreesWithNeedsRebuildState()
    {
        var state = _resolver.Resolve(CreateContext("rebuild", WorkspaceReadinessStatus.NeedsRebuild, WorkspaceRuntimeState.Running, false, false, false, false, string.Empty));

        Assert.All(state.AvailableServices, service =>
        {
            Assert.False(service.IsAvailable);
            Assert.Equal("Rebuild Runtime before opening services.", service.UnavailableReason);
        });
    }

    [Fact]
    public void Resolve_Ready_DoesNotPromoteRebuild()
    {
        var state = _resolver.Resolve(CreateContext("ready", WorkspaceReadinessStatus.Ready, WorkspaceRuntimeState.Running, false, false, false, false, string.Empty));

        Assert.Equal(WorkspacePresentedActionKind.OpenWorkspace, state.PrimaryAction?.Kind);
        Assert.DoesNotContain(state.AdvancedActions, action => action.Kind == WorkspacePresentedActionKind.RebuildRuntime && action.IsPrimary);
    }

    [Fact]
    public void Resolve_FreshWorkspace_DoesNotShowNeedsRebuild()
    {
        var state = _resolver.Resolve(CreateContext("fresh", WorkspaceReadinessStatus.Unavailable, WorkspaceRuntimeState.Stopped, true, true, false, true, "Provisioning is in progress."));

        Assert.Equal(WorkspacePresentationStatusKind.Provisioning, state.Status);
        Assert.NotEqual(WorkspacePresentedActionKind.RebuildRuntime, state.PrimaryAction?.Kind);
        Assert.DoesNotContain(state.AdvancedActions, action => action.Kind == WorkspacePresentedActionKind.RebuildRuntime && action.IsEnabled);
    }

    [Fact]
    public void Architecture_FormatterFile_IsRemoved()
    {
        Assert.False(File.Exists(Path.Combine(GetRepoRoot(), "src", "OpenCode.Workspace.Avalonia", "ViewModels", "WorkspaceReadinessPresentationFormatter.cs")));
    }

    [Fact]
    public void Architecture_Aggregator_DoesNotReturnPresentationActions()
    {
        var source = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "OpenCode.Workspace.Avalonia", "ViewModels", "WorkspaceHealthAggregator.cs"));

        Assert.DoesNotContain("WorkspacePresentedAction", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionItemViewModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PrimaryActionLabel", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Architecture_ViewModel_DoesNotBuildPresentedActionsDirectly()
    {
        var source = File.ReadAllText(Path.Combine(GetRepoRoot(), "src", "OpenCode.Workspace.Avalonia", "ViewModels", "WorkspacesPageViewModel.cs"));

        Assert.DoesNotContain("CreatePresentedAction(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolvePresentedActionCommand(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Architecture_SourcePresentationActions_OnlyComeFromResolver()
    {
        var viewModelDirectory = Path.Combine(GetRepoRoot(), "src", "OpenCode.Workspace.Avalonia", "ViewModels");
        var resolverSource = File.ReadAllText(Path.Combine(viewModelDirectory, "WorkspacePresentationStateResolver.cs"));
        var viewModelSource = File.ReadAllText(Path.Combine(viewModelDirectory, "WorkspacesPageViewModel.cs"));

        Assert.Contains("CreateAction(", resolverSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAction(", viewModelSource, StringComparison.Ordinal);
    }

    private WorkspacePresentationResolutionContext CreateContext(
        string name,
        WorkspaceReadinessStatus readinessStatus,
        WorkspaceRuntimeState runtimeState,
        bool isFreshWorkspace,
        bool requiresPreparation,
        bool missingSnapshot,
        bool isOperationRunning,
        string operationStatus)
    {
        var workspace = CreateWorkspace(name, readinessStatus, runtimeState, missingSnapshot);
        var readiness = missingSnapshot ? null : workspace.Snapshot!.Readiness;

        return new WorkspacePresentationResolutionContext(
            workspace,
            new WorkspaceAggregatedState
            {
                Summary = "summary",
                CurrentActivity = isOperationRunning ? "Provisioning" : "None",
                ActivitySummary = isOperationRunning ? operationStatus : "No active workspace operation.",
            },
            readiness,
            isOperationRunning,
            operationStatus,
            isOperationRunning,
            isOperationRunning ? "Prepare" : string.Empty,
            operationStatus,
            HasInteractionService: true,
            HasClipboardService: true,
            SupportsApexAssistant: false,
            SupportsSynchronization: false,
            IsOracleApexWorkspace: false,
            IsOracleApexMediaMissing: false,
            isFreshWorkspace,
            requiresPreparation,
            RetryOperationName: null,
            RepairabilityClassification: readinessStatus == WorkspaceReadinessStatus.NeedsRebuild ? WorkspaceRepairability.CleanupRepair : null,
            [new WorkspacePresentedServiceCandidate("development-shell", "Development Shell", "Development", "Shell", string.Empty, string.Empty, string.Empty, string.Empty, true)]);
    }

    private static WorkspaceSummaryViewModel CreateWorkspace(string name, WorkspaceReadinessStatus readinessStatus, WorkspaceRuntimeState runtimeState, bool missingSnapshot)
    {
        if (missingSnapshot)
        {
            return new WorkspaceSummaryViewModel(new WorkspaceShellItem
            {
                Record = new WorkspaceRecord { Name = name, RootPath = "/tmp/" + name, RepositoryPath = "/tmp/" + name, CreatedUtc = DateTimeOffset.UtcNow, LastOpenedUtc = DateTimeOffset.UtcNow },
                ErrorMessage = "failed",
            });
        }

        var root = Path.Combine(Path.GetTempPath(), $"resolver-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var snapshot = new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord { Name = name, RootPath = root, RepositoryPath = root, CreatedUtc = DateTimeOffset.UtcNow, LastOpenedUtc = DateTimeOffset.UtcNow },
            Definition = new WorkspaceDefinition { Workspace = new WorkspaceMetadata { Name = name, Image = "ubuntu:24.04" }, Features = ["core"], Services = [] },
            Paths = new WorkspacePaths
            {
                RootPath = root,
                GitIgnorePath = Path.Combine(root, ".gitignore"),
                OpencodePath = Path.Combine(root, ".opencode"),
                OpencodeLocalPath = Path.Combine(root, ".opencode", "local"),
                WorkspaceYamlRelativePath = "workspace.yaml",
                WorkspaceYamlPath = Path.Combine(root, "workspace.yaml"),
                ComposePath = Path.Combine(root, "compose.yaml"),
                EnvironmentFilePath = Path.Combine(root, ".env"),
                MountsRootPath = Path.Combine(root, "mounts"),
                InboxPath = Path.Combine(root, "mounts", "inbox"),
                WorkspacePath = Path.Combine(root, "mounts", "workspace"),
                UserPath = Path.Combine(root, "mounts", "user"),
                HomePath = Path.Combine(root, "mounts", "home"),
                ConfigPath = Path.Combine(root, "mounts", "config"),
                ProvisionScriptPath = Path.Combine(root, "mounts", "config", "provision.sh"),
                StarshipConfigPath = Path.Combine(root, "mounts", "config", "starship.toml"),
                ShellInitScriptPath = Path.Combine(root, "mounts", "config", "opencode-shell-init.sh"),
                OpencodeWorkspaceShellPath = Path.Combine(root, "mounts", "config", "opencode-workspace-shell.sh"),
                ScreenConfigPath = Path.Combine(root, "mounts", "config", "screenrc"),
                AttachWrapperScriptPath = Path.Combine(root, "mounts", "config", "attach.ps1"),
                AttachDiagnosticsLogPath = Path.Combine(root, "mounts", "config", "attach.log"),
                TerminalDiagnosticsScriptPath = Path.Combine(root, "mounts", "config", "terminal-diagnostics.ps1"),
                RuntimeStatePath = Path.Combine(root, ".opencode", "local", "runtime-state.yaml"),
                AppliedStatePath = Path.Combine(root, "mounts", "config", "applied-state.yaml"),
                HistoryPath = Path.Combine(root, "history"),
                CheckpointsPath = Path.Combine(root, "history", "checkpoints"),
                CheckpointIndexPath = Path.Combine(root, "history", "checkpoints", "index.yaml"),
                TimelinePath = Path.Combine(root, "history", "timeline.yaml"),
                RuntimesPath = Path.Combine(root, "runtimes"),
                DefaultRuntimePath = Path.Combine(root, "runtimes", "default.yaml"),
                ArtifactsPath = Path.Combine(root, "artifacts"),
                ArtifactRunsPath = Path.Combine(root, "artifacts", "runs"),
                ArtifactIndexPath = Path.Combine(root, "artifacts", "index.json"),
            },
            ConfigurationPath = Path.Combine(root, "workspace.yaml"),
            RuntimeState = runtimeState,
            Safety = new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.Protected,
                Headline = "Protected",
                Message = "Protected",
                LocalRecovery = new WorkspaceLocalRecoverySnapshot(),
                Backup = new WorkspaceBackupSnapshot(),
                IgnorePolicy = new WorkspaceIgnorePolicyReview(),
                AdvancedGit = new WorkspaceAdvancedGitSnapshot(),
            },
            Session = new WorkspaceSessionSnapshot(),
            AppliedState = readinessStatus is WorkspaceReadinessStatus.Ready or WorkspaceReadinessStatus.NeedsRebuild or WorkspaceReadinessStatus.Unavailable ? new WorkspaceAppliedState() : null,
            LocalRuntimeState = readinessStatus is WorkspaceReadinessStatus.Ready or WorkspaceReadinessStatus.NeedsRebuild or WorkspaceReadinessStatus.Unavailable ? new WorkspaceRuntimeStateRecord() : null,
            ResolvedRuntimePlan = new ResolvedRuntimePlan { Runtime = "docker", TargetPlatform = "linux-x64", IsAvailable = true },
            UpdateRequired = false,
            Health = new WorkspaceHealthSnapshot(),
            Readiness = new WorkspaceReadinessSnapshot { Status = readinessStatus, PrimaryAction = readinessStatus == WorkspaceReadinessStatus.NeedsRebuild ? WorkspacePrimaryAction.RebuildRuntime : readinessStatus == WorkspaceReadinessStatus.ProvisioningFailed ? WorkspacePrimaryAction.RetryProvisioning : readinessStatus == WorkspaceReadinessStatus.Preparing ? WorkspacePrimaryAction.ViewProgress : WorkspacePrimaryAction.OpenWorkspace, IsOperationInProgress = readinessStatus == WorkspaceReadinessStatus.Preparing },
        };

        return new WorkspaceSummaryViewModel(new WorkspaceShellItem { Record = snapshot.Record, Snapshot = snapshot });
    }

    private static string GetRepoRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
