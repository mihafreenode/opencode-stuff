using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Workspaces;

/// <summary>
/// Coordinates the end-to-end workspace flow. The orchestrator is intentionally
/// concrete and use-case oriented so contributors can trace create, start,
/// provision, and attach behavior from one readable entry point.
/// </summary>
public sealed class WorkspaceOrchestrator
{
    private readonly WorkspaceYamlService _workspaceYamlService;
    private readonly WorkspaceDiscoveryService _workspaceDiscoveryService;
    private readonly WorkspaceRepository _workspaceRepository;
    private readonly WorkspaceResolver _workspaceResolver;
    private readonly ComposeGenerator _composeGenerator;
    private readonly EnvironmentFileGenerator _environmentFileGenerator;
    private readonly ProvisioningScriptGenerator _provisioningScriptGenerator;
    private readonly TerminalArtifactsGenerator _terminalArtifactsGenerator;
    private readonly AttachArtifactsGenerator _attachArtifactsGenerator;
    private readonly WorkspaceContentGenerator _workspaceContentGenerator;
    private readonly WorkspaceAppliedStateService _workspaceAppliedStateService;
    private readonly WorkspaceCheckpointService _workspaceCheckpointService;
    private readonly WorkspaceTimelineService _workspaceTimelineService;
    private readonly WorkspaceSafetyService _workspaceSafetyService;
    private readonly WorkspaceIgnorePolicyService _workspaceIgnorePolicyService;
    private readonly IWorkspaceProvider _workspaceProvider;
    private readonly DockerService _dockerService;
    private readonly ITerminalLauncher _terminalLauncher;
    private readonly OpenCodeSessionService _openCodeSessionService = new();

    public WorkspaceOrchestrator(
        WorkspaceYamlService workspaceYamlService,
        WorkspaceDiscoveryService workspaceDiscoveryService,
        WorkspaceRepository workspaceRepository,
        WorkspaceResolver workspaceResolver,
        ComposeGenerator composeGenerator,
        EnvironmentFileGenerator environmentFileGenerator,
        ProvisioningScriptGenerator provisioningScriptGenerator,
        TerminalArtifactsGenerator terminalArtifactsGenerator,
        AttachArtifactsGenerator attachArtifactsGenerator,
        WorkspaceContentGenerator workspaceContentGenerator,
        WorkspaceAppliedStateService workspaceAppliedStateService,
        WorkspaceCheckpointService workspaceCheckpointService,
        WorkspaceTimelineService workspaceTimelineService,
        WorkspaceSafetyService workspaceSafetyService,
        WorkspaceIgnorePolicyService workspaceIgnorePolicyService,
        IWorkspaceProvider workspaceProvider,
        DockerService dockerService,
        ITerminalLauncher terminalLauncher)
    {
        _workspaceYamlService = workspaceYamlService;
        _workspaceDiscoveryService = workspaceDiscoveryService;
        _workspaceRepository = workspaceRepository;
        _workspaceResolver = workspaceResolver;
        _composeGenerator = composeGenerator;
        _environmentFileGenerator = environmentFileGenerator;
        _provisioningScriptGenerator = provisioningScriptGenerator;
        _terminalArtifactsGenerator = terminalArtifactsGenerator;
        _attachArtifactsGenerator = attachArtifactsGenerator;
        _workspaceContentGenerator = workspaceContentGenerator;
        _workspaceAppliedStateService = workspaceAppliedStateService;
        _workspaceCheckpointService = workspaceCheckpointService;
        _workspaceTimelineService = workspaceTimelineService;
        _workspaceSafetyService = workspaceSafetyService;
        _workspaceIgnorePolicyService = workspaceIgnorePolicyService;
        _workspaceProvider = workspaceProvider;
        _dockerService = dockerService;
        _terminalLauncher = terminalLauncher;
    }

    public IReadOnlyList<WorkspaceRecord> LoadWorkspaceRecords() => _workspaceRepository.LoadAll();

    public WorkspaceSnapshot LoadSnapshot(string rootPath)
        => Task.Run(() => LoadSnapshotAsync(rootPath)).GetAwaiter().GetResult();

    public async Task<WorkspaceSnapshot> LoadSnapshotAsync(string rootPath, CancellationToken cancellationToken = default, bool includeRuntimeInspection = true)
    {
        var record = _workspaceRepository.LoadAll().FirstOrDefault(item => string.Equals(item.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));
        var configurationPath = ResolveConfigurationPath(rootPath, record?.ConfigurationPath);
        var paths = WorkspacePathBuilder.Build(rootPath, configurationPath);
        var definition = _workspaceYamlService.Read(paths.WorkspaceYamlPath);
        record ??= new WorkspaceRecord
            {
                Name = definition.Workspace.Name,
                RootPath = rootPath,
                RepositoryPath = rootPath,
                ConfigurationPath = paths.WorkspaceYamlRelativePath,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            };

        var generatedArtifacts = GenerateArtifacts(definition, paths);
        var appliedState = _workspaceAppliedStateService.Read(paths.AppliedStatePath);
        var updateRequired = IsUpdateRequired(paths, generatedArtifacts, appliedState);
        var latestCheckpoint = _workspaceCheckpointService.GetLatest(paths.CheckpointIndexPath);
        var lastSuccessfulPublishUtc = _workspaceTimelineService.GetLastPublishUtc(paths.TimelinePath);
        var gitState = await _workspaceProvider.GetGitStateAsync(paths, definition, cancellationToken);
        var ignorePolicyReview = _workspaceIgnorePolicyService.ReviewWorkspace(paths.RootPath);
        var safety = _workspaceSafetyService.Build(gitState, latestCheckpoint, lastSuccessfulPublishUtc, ignorePolicyReview);

        var snapshot = new WorkspaceSnapshot
        {
            Record = record,
            Definition = definition,
            Paths = paths,
            ConfigurationPath = paths.WorkspaceYamlRelativePath,
            RuntimeState = File.Exists(paths.ComposePath) && Directory.Exists(paths.RootPath)
                ? WorkspaceRuntimeState.Unknown
                : WorkspaceRuntimeState.Stopped,
            Safety = safety,
            Session = new WorkspaceSessionSnapshot
            {
                SessionName = definition.Workspace.Id,
                State = WorkspaceSessionState.Unknown,
            },
            AppliedState = appliedState,
            UpdateRequired = updateRequired,
        };

        if (!includeRuntimeInspection)
        {
            return new WorkspaceSnapshot
            {
                Record = snapshot.Record,
                Definition = snapshot.Definition,
                Paths = snapshot.Paths,
                ConfigurationPath = snapshot.ConfigurationPath,
                RuntimeState = WorkspaceRuntimeState.Unknown,
                Safety = snapshot.Safety,
                Session = new WorkspaceSessionSnapshot
                {
                    SessionName = definition.Workspace.Id,
                    State = WorkspaceSessionState.Unknown,
                },
                AppliedState = snapshot.AppliedState,
                UpdateRequired = snapshot.UpdateRequired,
            };
        }

        var runtimeState = await GetRuntimeStateAsync(snapshot, cancellationToken);
        var sessionState = runtimeState == WorkspaceRuntimeState.Running
            ? await GetSessionStateAsync(definition, cancellationToken)
            : WorkspaceSessionState.NotRunning;
        return new WorkspaceSnapshot
        {
            Record = snapshot.Record,
            Definition = snapshot.Definition,
            Paths = snapshot.Paths,
            ConfigurationPath = snapshot.ConfigurationPath,
            RuntimeState = runtimeState,
            Safety = snapshot.Safety,
            Session = new WorkspaceSessionSnapshot
            {
                SessionName = definition.Workspace.Id,
                State = sessionState,
            },
            AppliedState = snapshot.AppliedState,
            UpdateRequired = snapshot.UpdateRequired,
        };
    }

    public WorkspaceSnapshot CreateWorkspace(string rootPath, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null)
        => CreateWorkspaceAsync(rootPath, definition, log).GetAwaiter().GetResult();

    public async Task<WorkspaceSnapshot> CreateWorkspaceAsync(string rootPath, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, bool includeRuntimeInspection = true)
    {
        var paths = WorkspacePathBuilder.Build(rootPath);
        Log(log, "app", $"[create] Preparing folder structure at '{rootPath}'.");
        CreateFolderStructure(paths);
        Log(log, "app", "[create] Folder structure prepared.");
        Log(log, "app", "[create] Ensuring workspace scaffolding files.");
        EnsureWorkspaceScaffolding(paths, definition);
        Log(log, "app", "[create] Workspace scaffolding ensured.");
        Log(log, "app", "[create] Writing generated workspace files.");
        WriteGeneratedFiles(paths, definition);
        Log(log, "app", "[create] Generated workspace files written.");
        Log(log, "app", "[create] Initializing workspace repository.");
        await _workspaceProvider.InitializeWorkspaceAsync(paths, definition, createInitialSavePoint: false, log, cancellationToken);
        Log(log, "app", "[create] Workspace repository initialized without an automatic initial Save Point.");
        _workspaceTimelineService.Append(paths.TimelinePath, "workspace-created", "Created workspace", "Initialized the workspace repository without blocking the UI on an automatic Save Point.");

        var now = DateTimeOffset.UtcNow;
        var record = new WorkspaceRecord
        {
            Name = definition.Workspace.Name,
            RootPath = rootPath,
            RepositoryPath = rootPath,
            ConfigurationPath = paths.WorkspaceYamlRelativePath,
            SourceType = WorkspaceSourceType.NewWorkspace,
            CreatedUtc = now,
            LastOpenedUtc = now,
            LastOperationName = "Create Workspace",
            LastOperationResult = "Workspace created.",
            LastOperationSucceeded = true,
            LastOperationUtc = now,
        };

        Log(log, "app", $"[create] Saving workspace record for '{definition.Workspace.Name}'.");
        await _workspaceRepository.SaveAsync(record, cancellationToken);
        Log(log, "app", "[create] Workspace record saved.");

        Log(log, "app", "[create] Loading workspace snapshot after registration.");
        return await LoadSnapshotAsync(rootPath, cancellationToken, includeRuntimeInspection);
    }

    public WorkspaceSnapshot OpenFolderAsWorkspace(string rootPath, string? workspaceName = null, Action<CommandLogEntry>? log = null)
    {
        var discovery = _workspaceDiscoveryService.Discover(rootPath);
        if (discovery.Status == WorkspaceDiscoveryStatus.Invalid)
        {
            var configurationPath = discovery.ConfigurationPath ?? "workspace configuration";
            throw new InvalidOperationException($"Invalid workspace configuration found at '{configurationPath}'. {discovery.ErrorMessage}".Trim());
        }

        if (discovery.Status == WorkspaceDiscoveryStatus.Found)
        {
            return LoadSnapshot(rootPath);
        }

        var paths = WorkspacePathBuilder.Build(rootPath);

        var folderName = string.IsNullOrWhiteSpace(workspaceName) ? Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : workspaceName.Trim();
        var definition = new WorkspaceDefinition
        {
            Workspace = new WorkspaceMetadata
            {
                Id = WorkspacePathBuilder.Slugify(folderName),
                Name = folderName,
                Image = "ubuntu:24.04",
            },
            Provider = new WorkspaceProviderDefinition
            {
                Type = _workspaceProvider.Type,
            },
            Runtime = new WorkspaceRuntimeDefinition
            {
                Default = "default",
                Node = WorkspaceRuntimeDefinition.DefaultNodeMajorVersion,
            },
            Features = new List<string> { "core" },
            Services = new List<string>(),
            Skills = new List<string>(),
            Mcp = new List<string>(),
        };

        return CreateWorkspace(rootPath, definition, log);
    }

    public async Task<ExistingGitCheckoutPlan> InspectExistingGitCheckoutAsync(string repositoryRoot, string? workspaceName = null, CancellationToken cancellationToken = default)
    {
        var gitProvider = GetGitWorkspaceProvider();
        var inspection = await gitProvider.RepositoryService.InspectAsync(repositoryRoot, cancellationToken);
        if (!inspection.IsRepository)
        {
            throw new InvalidOperationException("The selected folder is not a Git checkout.");
        }

        var folderName = string.IsNullOrWhiteSpace(workspaceName)
            ? Path.GetFileName(repositoryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : workspaceName.Trim();
        var discovery = _workspaceDiscoveryService.Discover(repositoryRoot);
        WorkspaceDefinition? loadedDefinition = null;
        if (discovery.Status == WorkspaceDiscoveryStatus.Found && !string.IsNullOrWhiteSpace(discovery.ConfigurationPath))
        {
            try
            {
                loadedDefinition = _workspaceYamlService.Read(Path.Combine(repositoryRoot, discovery.ConfigurationPath.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception exception)
            {
                discovery = new WorkspaceDiscoveryResult
                {
                    Status = WorkspaceDiscoveryStatus.Invalid,
                    ConfigurationPath = discovery.ConfigurationPath,
                    ErrorMessage = exception.Message,
                };
            }
        }

        return new ExistingGitCheckoutPlan
        {
            RepositoryPath = repositoryRoot,
            WorkspaceName = folderName,
            Repository = inspection,
            DiscoveryResult = discovery,
            LoadedDefinition = loadedDefinition,
        };
    }

    public async Task<WorkspaceSnapshot> ImportExistingGitCheckoutAsync(ExistingGitCheckoutImportRequest request, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var gitProvider = GetGitWorkspaceProvider();
        var repositoryService = gitProvider.RepositoryService;
        var inspection = await repositoryService.InspectAsync(request.RepositoryPath, cancellationToken);
        if (!inspection.IsRepository)
        {
            throw new InvalidOperationException("The selected folder is not a Git checkout.");
        }

        var selectedBranch = inspection.CurrentBranch;
        switch (request.BranchMode)
        {
            case ExistingGitCheckoutBranchMode.CreateTemporaryWorkspaceBranch:
                selectedBranch = await repositoryService.CreateUniqueWorkspaceBranchNameAsync(request.RepositoryPath, request.WorkspaceName, DateTimeOffset.UtcNow, cancellationToken);
                await repositoryService.CreateBranchAsync(request.RepositoryPath, selectedBranch, log, cancellationToken);
                break;
            case ExistingGitCheckoutBranchMode.CreateNamedFeatureBranch:
                var branchName = request.NamedBranch.Trim();
                var validation = await repositoryService.ValidateBranchNameAsync(request.RepositoryPath, branchName, cancellationToken);
                if (!validation.IsValid)
                {
                    throw new InvalidOperationException(validation.Message);
                }

                selectedBranch = branchName;
                if (validation.BranchExists)
                {
                    if (!request.ReuseExistingNamedBranch)
                    {
                        throw new InvalidOperationException("That branch already exists. Choose a different name or confirm that you want to use the existing branch.");
                    }

                    await repositoryService.CheckoutBranchAsync(request.RepositoryPath, selectedBranch, log, cancellationToken);
                }
                else
                {
                    await repositoryService.CreateBranchAsync(request.RepositoryPath, selectedBranch, log, cancellationToken);
                }

                break;
            case ExistingGitCheckoutBranchMode.UseCurrentBranch:
            default:
                break;
        }

        var discovery = _workspaceDiscoveryService.Discover(request.RepositoryPath);
        if (discovery.Status == WorkspaceDiscoveryStatus.Invalid)
        {
            var invalidConfigurationPath = discovery.ConfigurationPath ?? "workspace configuration";
            throw new InvalidOperationException($"Invalid workspace configuration found at '{invalidConfigurationPath}'. {discovery.ErrorMessage}".Trim());
        }

        var activeConfigurationPath = discovery.ConfigurationPath ?? "workspace.yaml";
        var paths = WorkspacePathBuilder.Build(request.RepositoryPath, activeConfigurationPath);
        var definition = discovery.Status == WorkspaceDiscoveryStatus.Found
            ? _workspaceYamlService.Read(paths.WorkspaceYamlPath)
            : request.InitialDefinition is not null
                ? new WorkspaceDefinition
                {
                    Workspace = new WorkspaceMetadata
                    {
                        Id = string.IsNullOrWhiteSpace(request.InitialDefinition.Workspace.Id)
                            ? WorkspacePathBuilder.Slugify(request.WorkspaceName)
                            : request.InitialDefinition.Workspace.Id,
                        Name = string.IsNullOrWhiteSpace(request.InitialDefinition.Workspace.Name)
                            ? request.WorkspaceName.Trim()
                            : request.InitialDefinition.Workspace.Name,
                        Image = request.InitialDefinition.Workspace.Image,
                    },
                    Provider = new WorkspaceProviderDefinition
                    {
                        Type = string.IsNullOrWhiteSpace(request.InitialDefinition.Provider.Type) ? gitProvider.Type : request.InitialDefinition.Provider.Type,
                        Url = string.IsNullOrWhiteSpace(request.InitialDefinition.Provider.Url) ? inspection.RemoteUrl : request.InitialDefinition.Provider.Url,
                    },
                    Runtime = request.InitialDefinition.Runtime,
                    Features = request.InitialDefinition.Features,
                    Services = request.InitialDefinition.Services,
                    Skills = request.InitialDefinition.Skills,
                    Mcp = request.InitialDefinition.Mcp,
                    Terminal = request.InitialDefinition.Terminal,
                    Agent = request.InitialDefinition.Agent,
                }
                : new WorkspaceDefinition
                {
                    Workspace = new WorkspaceMetadata
                    {
                        Id = WorkspacePathBuilder.Slugify(request.WorkspaceName),
                        Name = request.WorkspaceName.Trim(),
                        Image = "ubuntu:24.04",
                    },
                    Provider = new WorkspaceProviderDefinition
                    {
                        Type = gitProvider.Type,
                        Url = inspection.RemoteUrl,
                    },
                    Runtime = new WorkspaceRuntimeDefinition
                    {
                        Default = "default",
                        Node = WorkspaceRuntimeDefinition.DefaultNodeMajorVersion,
                    },
                    Features = new List<string> { "core" },
                    Services = new List<string>(),
                    Skills = new List<string>(),
                    Mcp = new List<string>(),
                };

        CreateFolderStructure(paths);
        EnsureWorkspaceScaffolding(paths, definition);
        WriteGeneratedFiles(paths, definition);

        var now = DateTimeOffset.UtcNow;
        _workspaceRepository.Save(new WorkspaceRecord
        {
            Name = definition.Workspace.Name,
            RootPath = request.RepositoryPath,
            RepositoryPath = request.RepositoryPath,
            ConfigurationPath = paths.WorkspaceYamlRelativePath,
            SourceType = WorkspaceSourceType.ExistingGitCheckout,
            ImportedFromExistingCheckout = true,
            OriginalDefaultBranch = inspection.DefaultBranch,
            SelectedWorkspaceBranch = selectedBranch,
            RemoteOriginUrl = inspection.RemoteUrl,
            CreatedUtc = now,
            LastOpenedUtc = now,
            LastOperationName = "Import Existing Git Checkout",
            LastOperationResult = "Imported existing Git checkout.",
            LastOperationSucceeded = true,
            LastOperationUtc = now,
        });

        _workspaceTimelineService.Append(paths.TimelinePath, "imported-git-checkout", "Imported existing Git checkout", $"Imported checkout at '{request.RepositoryPath}' on branch '{selectedBranch}'.");
        return await LoadSnapshotAsync(request.RepositoryPath, cancellationToken);
    }

    public async Task RegenerateAsync(WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        WriteGeneratedFiles(snapshot.Paths, snapshot.Definition);
        await Task.CompletedTask;
    }

    public async Task RecoverAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        await EnsureManagedComposeCurrentAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken);
        Log(log, "app", $"Validating regenerated compose.yaml for workspace '{snapshot.Definition.Workspace.Name}'.");
        var result = await _dockerService.ValidateAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken, repairComposeAsync: token => EnsureManagedComposeCurrentAsync(snapshot.Paths, snapshot.Definition, log, token));
        EnsureSuccess(result, "Workspace recovery failed.");
    }

    public async Task StartAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        await EnsureManagedComposeCurrentAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken);
        Log(log, "app", $"Starting workspace '{snapshot.Definition.Workspace.Name}'.");
        var result = await _dockerService.StartAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken, repairComposeAsync: token => EnsureManagedComposeCurrentAsync(snapshot.Paths, snapshot.Definition, log, token));
        EnsureSuccess(result, "Workspace start failed.");
        await ValidateWorkspaceRunningAsync(snapshot, log, cancellationToken);
    }

    public async Task StopAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        Log(log, "app", $"Stopping workspace '{snapshot.Definition.Workspace.Name}'.");
        var result = await _dockerService.StopAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken);
        EnsureSuccess(result, "Workspace stop failed.");
    }

    public async Task RemoveDockerResourcesAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        await EnsureManagedComposeCurrentAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken);
        Log(log, "app", $"Removing Docker resources for workspace '{snapshot.Definition.Workspace.Name}'.");
        var result = await _dockerService.RemoveAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken, repairComposeAsync: token => EnsureManagedComposeCurrentAsync(snapshot.Paths, snapshot.Definition, log, token));
        EnsureSuccess(result, "Workspace removal failed while cleaning up Docker resources.");
    }

    public async Task ResetRuntimeAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        await EnsureManagedComposeCurrentAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken);
        Log(log, "app", $"Resetting runtime resources for workspace '{snapshot.Definition.Workspace.Name}'.");
        var result = await _dockerService.ResetAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken, repairComposeAsync: token => EnsureManagedComposeCurrentAsync(snapshot.Paths, snapshot.Definition, log, token));
        EnsureSuccess(result, "Workspace reset failed while removing runtime resources.");
    }

    public async Task ProvisionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var generatedArtifacts = await EnsureManagedGeneratedFilesCurrentAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken);
        Log(log, "app", $"Preparing workspace '{snapshot.Definition.Workspace.Name}'.");
        var startResult = await _dockerService.StartAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken, repairComposeAsync: token => EnsureManagedComposeCurrentAsync(snapshot.Paths, snapshot.Definition, log, token));
        EnsureSuccess(startResult, "Workspace start failed before provisioning.");
        await ValidateWorkspaceRunningAsync(snapshot, log, cancellationToken);

        await ProvisionRunningWorkspaceAsync(snapshot, log, cancellationToken);
        _workspaceAppliedStateService.Write(snapshot.Paths.AppliedStatePath, _workspaceAppliedStateService.CreateState(generatedArtifacts.Artifacts));
    }

    public async Task AttachAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        Log(log, "app", $"Ensuring workspace '{snapshot.Definition.Workspace.Name}' is running before attach.");
        LogAttach(log, snapshot, $"Selected workspace '{snapshot.Definition.Workspace.Name}'.");
        await EnsureManagedComposeCurrentAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken);
        var startResult = await _dockerService.StartAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken, repairComposeAsync: token => EnsureManagedComposeCurrentAsync(snapshot.Paths, snapshot.Definition, log, token));
        EnsureSuccess(startResult, "Workspace start failed before attach.");
        await ValidateWorkspaceRunningAsync(snapshot, log, cancellationToken);
        LogAttach(log, snapshot, "Container status: running.");
        await EnsureProvisionedForAttachAsync(snapshot, log, cancellationToken);
        LogAttach(log, snapshot, "Handing off to terminal attach wrapper.");

        await _terminalLauncher.LaunchAttachSessionAsync(snapshot, log, cancellationToken);
    }

    public async Task<bool> CreateSavePointAsync(WorkspaceSnapshot snapshot, string message, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var saved = await _workspaceProvider.CreateSavePointAsync(snapshot.Paths, snapshot.Definition, message, log, cancellationToken);
        if (saved)
        {
            _workspaceTimelineService.Append(snapshot.Paths.TimelinePath, "save-point", "Created Save Point", message);
        }

        return saved;
    }

    public async Task<WorkspaceCheckpointRecord> CreateCheckpointAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var gitProvider = _workspaceProvider as GitWorkspaceProvider ?? throw new InvalidOperationException("Checkpoint creation currently requires the Git workspace provider.");
        var gitState = await _workspaceProvider.GetGitStateAsync(snapshot.Paths, snapshot.Definition, cancellationToken);
        var checkpointId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var checkpointPath = Path.Combine(snapshot.Paths.CheckpointsPath, checkpointId);
        Directory.CreateDirectory(checkpointPath);

        var patch = await gitProvider.GetTrackedChangesPatchAsync(snapshot.Paths.RootPath, cancellationToken);
        File.WriteAllText(Path.Combine(checkpointPath, "tracked.patch"), patch.Replace("\r\n", "\n", StringComparison.Ordinal));

        // Checkpoints complement Save Points by preserving local state that Git may
        // not yet describe safely enough for recovery, especially untracked files.
        var untrackedFiles = await gitProvider.GetUntrackedFilesAsync(snapshot.Paths.RootPath, cancellationToken);
        var copiedFiles = new List<string>();
        if (untrackedFiles.Count > 0)
        {
            var untrackedRoot = Path.Combine(checkpointPath, "untracked");
            Directory.CreateDirectory(untrackedRoot);
            foreach (var relativePath in untrackedFiles)
            {
                var sourcePath = Path.Combine(snapshot.Paths.RootPath, relativePath);
                if (!File.Exists(sourcePath))
                {
                    continue;
                }

                var destinationPath = Path.Combine(untrackedRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath, overwrite: true);
                copiedFiles.Add(relativePath);
            }
        }

        if (File.Exists(snapshot.Paths.WorkspaceYamlPath))
        {
            File.Copy(snapshot.Paths.WorkspaceYamlPath, Path.Combine(checkpointPath, Path.GetFileName(snapshot.Paths.WorkspaceYamlPath)), overwrite: true);
        }

        if (File.Exists(snapshot.Paths.ArtifactIndexPath))
        {
            File.Copy(snapshot.Paths.ArtifactIndexPath, Path.Combine(checkpointPath, "artifact-index.json"), overwrite: true);
        }

        var record = new WorkspaceCheckpointRecord
        {
            Id = checkpointId,
            CreatedUtc = DateTimeOffset.UtcNow,
            CurrentBranch = gitState.CurrentBranch,
            CurrentCommitSha = gitState.LatestCommitSha,
            CapturedUntrackedFiles = copiedFiles.Count == untrackedFiles.Count,
            UntrackedFiles = copiedFiles,
        };

        _workspaceCheckpointService.SaveMetadata(Path.Combine(checkpointPath, "checkpoint.yaml"), record);
        _workspaceCheckpointService.AddCheckpoint(snapshot.Paths.CheckpointIndexPath, record);
        _workspaceTimelineService.Append(snapshot.Paths.TimelinePath, "checkpoint", "Created checkpoint", $"Checkpoint {checkpointId} captured {copiedFiles.Count} untracked file(s).");
        log?.Invoke(new CommandLogEntry { Source = "app", Message = $"Created checkpoint '{checkpointId}'." });
        return record;
    }

    public async Task<WorkspacePublishReview> PublishAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        _workspaceTimelineService.Append(snapshot.Paths.TimelinePath, "publish-attempted", "Publish attempted", $"Attempted to publish Working Copy '{snapshot.Safety.WorkingCopyName}'.");
        var review = await _workspaceProvider.PublishAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken);
        _workspaceTimelineService.Append(
            snapshot.Paths.TimelinePath,
            review.IsBlocked ? "publish-blocked" : "publish-succeeded",
            review.IsBlocked ? "Publish needs review" : "Published workspace",
            review.Message);
        return review;
    }

    public async Task<WorkspacePublishReview> UpdateWorkingCopyAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var review = await _workspaceProvider.UpdateWorkingCopyAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken);
        _workspaceTimelineService.Append(
            snapshot.Paths.TimelinePath,
            review.SafeUpdateApplied ? "working-copy-updated" : "publish-blocked",
            review.SafeUpdateApplied ? "Working Copy updated" : "Update needs review",
            review.Message);
        return review;
    }

    public async Task<WorkspacePublishReview> PublishToReviewWorkingCopyAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var review = await _workspaceProvider.PublishToReviewWorkingCopyAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken);
        _workspaceTimelineService.Append(
            snapshot.Paths.TimelinePath,
            review.IsBlocked ? "publish-blocked" : "publish-succeeded",
            review.IsBlocked ? "Publish to review Working Copy blocked" : "Published review Working Copy",
            review.IsBlocked ? review.Message : $"{review.Message} Review Working Copy: {review.ReviewWorkingCopyBranch}");
        return review;
    }

    public async Task<string> ExportPatchAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var patchFileName = $"{WorkspacePathBuilder.Slugify(snapshot.Definition.Workspace.Name)}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.patch";
        var outputPath = Path.Combine(snapshot.Paths.HistoryPath, patchFileName);
        Directory.CreateDirectory(snapshot.Paths.HistoryPath);
        return await _workspaceProvider.ExportPatchAsync(snapshot.Paths, snapshot.Definition, outputPath, log, cancellationToken);
    }

    public async Task LaunchAttachForRunningWorkspaceAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        LogAttach(log, snapshot, $"Selected workspace '{snapshot.Definition.Workspace.Name}'.");
        await ValidateWorkspaceRunningAsync(snapshot, log, cancellationToken);
        LogAttach(log, snapshot, "Container status: running.");
        await EnsureProvisionedForAttachAsync(snapshot, log, cancellationToken);
        LogAttach(log, snapshot, "Handing off to terminal attach wrapper.");
        await _terminalLauncher.LaunchAttachSessionAsync(snapshot, log, cancellationToken);
    }

    public void SaveRecord(WorkspaceRecord record) => _workspaceRepository.Save(record);

    public Task SaveRecordAsync(WorkspaceRecord record, CancellationToken cancellationToken = default)
        => _workspaceRepository.SaveAsync(record, cancellationToken);

    public void DeleteWorkspaceRegistration(string rootPath) => _workspaceRepository.Delete(rootPath);

    public async Task RepairWorkspaceFilePermissionsAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        Log(log, "app", $"Repairing Docker-owned file permissions for '{snapshot.Definition.Workspace.Name}'.");
        var result = await _dockerService.NormalizeWorkspaceFilePermissionsAsync(snapshot.Paths.RootPath, log, cancellationToken);
        EnsureSuccess(result, "Workspace file permission repair failed.");
    }

    public void WriteAppliedState(WorkspaceSnapshot snapshot)
    {
        var generatedArtifacts = GenerateArtifacts(snapshot.Definition, snapshot.Paths);
        _workspaceAppliedStateService.Write(snapshot.Paths.AppliedStatePath, _workspaceAppliedStateService.CreateState(generatedArtifacts));
    }

    private GeneratedWorkspaceArtifacts WriteGeneratedFiles(WorkspacePaths paths, WorkspaceDefinition definition)
    {
        var generatedArtifacts = GenerateArtifacts(definition, paths);

        _workspaceYamlService.WriteToFile(paths.WorkspaceYamlPath, definition);
        File.WriteAllText(paths.ComposePath, generatedArtifacts.ComposeYaml);
        File.WriteAllText(paths.EnvironmentFilePath, generatedArtifacts.EnvironmentFile);
        File.WriteAllText(paths.StarshipConfigPath, generatedArtifacts.StarshipConfig.Replace("\r\n", "\n", StringComparison.Ordinal));
        File.WriteAllText(paths.ShellInitScriptPath, generatedArtifacts.ShellInitScript.Replace("\r\n", "\n", StringComparison.Ordinal));
        File.WriteAllText(paths.OpencodeWorkspaceShellPath, generatedArtifacts.OpencodeWorkspaceShellScript.Replace("\r\n", "\n", StringComparison.Ordinal));
        EnsureGeneratedScriptPermissions(paths.OpencodeWorkspaceShellPath);
        File.WriteAllText(paths.ScreenConfigPath, generatedArtifacts.ScreenConfig.Replace("\r\n", "\n", StringComparison.Ordinal));
        File.WriteAllText(paths.AttachWrapperScriptPath, generatedArtifacts.AttachWrapperScript.Replace("\r\n", "\n", StringComparison.Ordinal));
        EnsureGeneratedScriptPermissions(paths.AttachWrapperScriptPath);
        File.WriteAllText(paths.TerminalDiagnosticsScriptPath, generatedArtifacts.TerminalDiagnosticsScript.Replace("\r\n", "\n", StringComparison.Ordinal));
        EnsureGeneratedScriptPermissions(paths.TerminalDiagnosticsScriptPath);

        foreach (var additionalFile in generatedArtifacts.AdditionalFiles)
        {
            var fullPath = Path.Combine(paths.RootPath, additionalFile.Key);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (string.Equals(additionalFile.Key, "AGENTS.md", StringComparison.OrdinalIgnoreCase))
            {
                var existingContent = File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
                var resolved = _workspaceResolver.Resolve(definition);
                var mergedContent = _workspaceContentGenerator.BuildAgentsDocument(resolved, existingContent);
                File.WriteAllText(fullPath, mergedContent.Replace("\r\n", "\n", StringComparison.Ordinal));
                continue;
            }

            File.WriteAllText(fullPath, additionalFile.Value.Replace("\r\n", "\n", StringComparison.Ordinal));
            EnsureGeneratedScriptPermissions(fullPath);
        }

        foreach (var binaryFile in generatedArtifacts.AdditionalBinaryFiles)
        {
            var fullPath = Path.Combine(paths.RootPath, binaryFile.Key);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(fullPath, binaryFile.Value);
        }

        // The provisioning script runs inside Linux containers, so it must use LF
        // line endings even when the desktop app generated it on Windows.
        File.WriteAllText(paths.ProvisionScriptPath, generatedArtifacts.ProvisionScript.Replace("\r\n", "\n", StringComparison.Ordinal));
        EnsureGeneratedScriptPermissions(paths.ProvisionScriptPath);
        return generatedArtifacts;
    }

    private async Task<bool> EnsureManagedComposeCurrentAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        var result = await EnsureManagedGeneratedFilesCurrentAsync(paths, definition, log, cancellationToken);
        return result.ComposeWasUpdated;
    }

    private Task<GeneratedFilesUpdateResult> EnsureManagedGeneratedFilesCurrentAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var previousCompose = File.Exists(paths.ComposePath)
            ? File.ReadAllText(paths.ComposePath)
            : null;
        var generatedArtifacts = WriteGeneratedFiles(paths, definition);
        var composeWasUpdated = !string.Equals(previousCompose, generatedArtifacts.ComposeYaml, StringComparison.Ordinal);

        if (composeWasUpdated)
        {
            Log(log, "app", "Stale compose detected for this managed workspace.");
            Log(log, "app", "Compose regenerated/repaired.");
            Log(log, "app", "Regenerated stale compose.yaml before Docker operation.");
        }

        return Task.FromResult(new GeneratedFilesUpdateResult(generatedArtifacts, composeWasUpdated));
    }

    private static void EnsureGeneratedScriptPermissions(string fullPath)
    {
        if (!fullPath.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                fullPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (PlatformNotSupportedException)
        {
        }
    }

    private GeneratedWorkspaceArtifacts GenerateArtifacts(WorkspaceDefinition definition, WorkspacePaths paths)
    {
        var resolved = _workspaceResolver.Resolve(definition);
        var workspaceYaml = _workspaceYamlService.Write(definition);
        var composeYaml = _composeGenerator.Generate(resolved, paths);
        var environmentFile = _environmentFileGenerator.Generate(definition);
        var provisionScript = _provisioningScriptGenerator.Generate(resolved);
        var starshipConfig = _terminalArtifactsGenerator.GenerateStarshipConfig(definition);
        var shellInitScript = _terminalArtifactsGenerator.GenerateShellInitScript(definition);
        var opencodeWorkspaceShellScript = _terminalArtifactsGenerator.GenerateOpencodeWorkspaceShellScript(definition);
        var screenConfig = _terminalArtifactsGenerator.GenerateScreenConfiguration();
        var attachWrapper = _attachArtifactsGenerator.GenerateWindowsTerminalWrapper(definition, paths);
        var diagnosticsWrapper = _attachArtifactsGenerator.GenerateTerminalDiagnosticsWrapper(definition);
        var additionalFiles = _workspaceContentGenerator.Generate(resolved);
        var additionalBinaryFiles = _workspaceContentGenerator.GenerateBinaryFiles(resolved);
        var workspaceDefinitionHash = WorkspaceAppliedStateService.ComputeHash(workspaceYaml);
        var desiredStateHash = WorkspaceAppliedStateService.ComputeHash(
            workspaceYaml,
            composeYaml,
            environmentFile,
            provisionScript,
            starshipConfig,
            shellInitScript,
            opencodeWorkspaceShellScript,
            screenConfig,
            attachWrapper,
            diagnosticsWrapper,
            string.Join("\n", additionalFiles.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select(item => item.Key + "\n" + item.Value)),
            string.Join("\n", additionalBinaryFiles.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select(item => item.Key + "\n" + Convert.ToHexString(item.Value))));

        return new GeneratedWorkspaceArtifacts
        {
            WorkspaceYaml = workspaceYaml,
            ComposeYaml = composeYaml,
            EnvironmentFile = environmentFile,
            ProvisionScript = provisionScript,
            StarshipConfig = starshipConfig,
            ShellInitScript = shellInitScript,
            OpencodeWorkspaceShellScript = opencodeWorkspaceShellScript,
            ScreenConfig = screenConfig,
            AttachWrapperScript = attachWrapper,
            TerminalDiagnosticsScript = diagnosticsWrapper,
            WorkspaceDefinitionHash = workspaceDefinitionHash,
            DesiredStateHash = desiredStateHash,
            AdditionalFiles = additionalFiles,
            AdditionalBinaryFiles = additionalBinaryFiles,
        };
    }

    private static bool IsUpdateRequired(WorkspacePaths paths, GeneratedWorkspaceArtifacts artifacts, WorkspaceAppliedState? appliedState)
    {
        if (!File.Exists(paths.ComposePath)
            || !File.Exists(paths.ProvisionScriptPath)
            || !File.Exists(paths.AttachWrapperScriptPath)
            || !File.Exists(paths.WorkspaceYamlPath)
            || appliedState is null)
        {
            return true;
        }

        return !string.Equals(appliedState.DesiredStateHash, artifacts.DesiredStateHash, StringComparison.Ordinal)
            || !string.Equals(appliedState.WorkspaceDefinitionHash, artifacts.WorkspaceDefinitionHash, StringComparison.Ordinal);
    }

    private static void CreateFolderStructure(WorkspacePaths paths)
    {
        Directory.CreateDirectory(paths.RootPath);
        Directory.CreateDirectory(paths.MountsRootPath);
        Directory.CreateDirectory(paths.InboxPath);
        Directory.CreateDirectory(paths.WorkspacePath);
        Directory.CreateDirectory(paths.UserPath);
        Directory.CreateDirectory(paths.HomePath);
        Directory.CreateDirectory(paths.ConfigPath);
        Directory.CreateDirectory(paths.HistoryPath);
        Directory.CreateDirectory(paths.CheckpointsPath);
        Directory.CreateDirectory(paths.RuntimesPath);
        Directory.CreateDirectory(paths.ArtifactsPath);
        Directory.CreateDirectory(paths.ArtifactRunsPath);
    }

    private void EnsureWorkspaceScaffolding(WorkspacePaths paths, WorkspaceDefinition definition)
    {
        _workspaceCheckpointService.EnsureCreated(paths.CheckpointIndexPath);
        _workspaceTimelineService.EnsureCreated(paths.TimelinePath);

        if (!File.Exists(paths.ArtifactIndexPath))
        {
            File.WriteAllText(paths.ArtifactIndexPath, "{\n  \"runs\": []\n}");
        }

        if (!File.Exists(paths.GitIgnorePath))
        {
            var gitIgnore = string.Join("\n",
                "mounts/home/",
                "mounts/user/",
                "mounts/inbox/",
                "history/checkpoints/",
                "artifacts/runs/",
                string.Empty);
            File.WriteAllText(paths.GitIgnorePath, gitIgnore.Replace("\r\n", "\n", StringComparison.Ordinal));
        }

        if (!File.Exists(paths.DefaultRuntimePath))
        {
            var content = string.Join("\n",
                "id: default",
                "name: Default Runtime",
                $"image: {definition.Workspace.Image}",
                "mounts:",
                "  - workspace",
                string.Empty);
            File.WriteAllText(paths.DefaultRuntimePath, content.Replace("\r\n", "\n", StringComparison.Ordinal));
        }
    }

    private async Task ValidateWorkspaceRunningAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        Log(log, "app", "Validating Docker Compose service status.");
        var composePsResult = await _dockerService.GetPsAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken);
        EnsureSuccess(composePsResult, "Docker Compose status check failed.");

        var expectedServiceNames = new[] { "workspace" }.Concat(snapshot.Definition.Services).ToList();
        foreach (var serviceName in expectedServiceNames)
        {
            if (composePsResult.StandardOutputLines.All(line => !string.Equals(line.Trim(), serviceName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Workspace validation failed. Service '{serviceName}' is not reported as running by Docker Compose.");
            }
        }

        Log(log, "app", "Checking for the expected workspace container in docker ps output.");
        var containerName = DockerService.GetWorkspaceContainerName(snapshot.Definition);
        var dockerPsResult = await _dockerService.RunSimpleDockerCommandAsync(
            new[] { "ps", "--filter", $"name={containerName}", "--format", "{{.Names}}" },
            log,
            cancellationToken);
        EnsureSuccess(dockerPsResult, "Workspace container lookup failed.");

        if (dockerPsResult.StandardOutputLines.All(line => !string.Equals(line.Trim(), containerName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Workspace validation failed. Container '{containerName}' is not running.");
        }
    }

    private async Task ValidateProvisionedWorkspaceAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        Log(log, "app", "Validating provisioned workspace tools.");
        await ValidateOpencodeUserExistsAsync(snapshot, log, cancellationToken);
        await LogWorkspaceRuntimeDiagnosticsAsync(snapshot, log, cancellationToken);
        var containerName = DockerService.GetWorkspaceContainerName(snapshot.Definition);
        var toolCheck = await _dockerService.RunSimpleDockerCommandAsync(
            new[] { "exec", containerName, "bash", "-lc", "command -v opencode && command -v screen && command -v node && command -v npm && getent passwd opencode" },
            log,
            cancellationToken);
        EnsureSuccess(toolCheck, "Workspace tool validation failed after provisioning.");

        var nodeCheck = await _dockerService.GetNodeToolDiagnosticsAsync(snapshot.Definition, log, cancellationToken);
        EnsureSuccess(nodeCheck, "Workspace Node.js validation failed after provisioning.");

        var actualNodeVersion = nodeCheck.StandardOutputLines.FirstOrDefault(line => line.TrimStart().StartsWith("v", StringComparison.Ordinal));
        var actualNodeMajorVersion = ParseNodeMajorVersion(actualNodeVersion);
        var expectedNodeMajorVersion = snapshot.Definition.Runtime.GetEffectiveNodeMajorVersion();
        if (actualNodeMajorVersion != expectedNodeMajorVersion)
        {
            throw new InvalidOperationException($"Workspace runtime validation failed. Expected Node.js {expectedNodeMajorVersion} but container reports {actualNodeVersion ?? "unknown"}.".Trim());
        }
    }

    private async Task EnsureOpencodeUserDirectoriesAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        Log(log, "app", "Checking OpenCode user directories.");
        await ValidateOpencodeUserExistsAsync(snapshot, log, cancellationToken);
        var result = await _dockerService.EnsureOpencodeUserDirectoriesAsync(snapshot.Definition, log, cancellationToken);
        EnsureSuccess(result, "OpenCode user directory initialization failed.");
    }

    private async Task EnsureProvisionedForAttachAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        var userCheck = await _dockerService.CheckOpencodeUserAsync(snapshot.Definition, log, cancellationToken);
        var requiresProvisioning = snapshot.AppliedState is null || snapshot.UpdateRequired || !userCheck.IsSuccess;

        if (requiresProvisioning)
        {
            Log(log, "app", "Workspace container is running but not provisioned. Running provisioning before attach.");
            LogAttach(log, snapshot, "Provisioning status: running provisioning before attach.");
            await ProvisionRunningWorkspaceAsync(snapshot, log, cancellationToken);
            LogAttach(log, snapshot, "Provisioning status: completed.");
            return;
        }

        LogAttach(log, snapshot, "Provisioning status: already provisioned.");
        await EnsureOpencodeUserDirectoriesAsync(snapshot, log, cancellationToken);
        await ValidateProvisionedWorkspaceAsync(snapshot, log, cancellationToken);
    }

    private async Task ProvisionRunningWorkspaceAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        await LogWorkspaceRuntimeDiagnosticsAsync(snapshot, log, cancellationToken);
        Log(log, "app", "Running provisioning script inside the workspace container.");
        var provisionResult = await _dockerService.RunProvisionScriptAsync(snapshot.Definition, snapshot.Paths, log, cancellationToken);
        EnsureSuccess(provisionResult, "Workspace provisioning failed.");
        await ValidateOpencodeUserExistsAsync(snapshot, log, cancellationToken);
        await EnsureOpencodeUserDirectoriesAsync(snapshot, log, cancellationToken);
        await ValidateProvisionedWorkspaceAsync(snapshot, log, cancellationToken);
    }

    private async Task LogWorkspaceRuntimeDiagnosticsAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        var imageResult = await _dockerService.InspectContainerImageAsync(snapshot.Definition, log, cancellationToken);
        if (imageResult.IsSuccess)
        {
            var imageId = imageResult.StandardOutputLines.FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(imageId))
            {
                Log(log, "runtime", $"Container image id: {imageId}");
                var repoTagsResult = await _dockerService.InspectImageRepoTagsAsync(imageId, log, cancellationToken);
                if (repoTagsResult.IsSuccess)
                {
                    Log(log, "runtime", $"Container image tags: {repoTagsResult.StandardOutput.Trim()}");
                }
            }
        }

        var nodeToolResult = await _dockerService.GetNodeToolDiagnosticsAsync(snapshot.Definition, log, cancellationToken);
        if (nodeToolResult.IsSuccess)
        {
            foreach (var line in nodeToolResult.StandardOutputLines.Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                Log(log, "runtime", line.Trim());
            }
        }

        var aptPolicyResult = await _dockerService.GetNodeAptPolicyAsync(snapshot.Definition, log, cancellationToken);
        if (aptPolicyResult.IsSuccess)
        {
            foreach (var line in aptPolicyResult.StandardOutputLines.Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                Log(log, "runtime", line.Trim());
            }
        }

        var osReleaseResult = await _dockerService.GetOsReleaseAsync(snapshot.Definition, log, cancellationToken);
        if (osReleaseResult.IsSuccess)
        {
            foreach (var line in osReleaseResult.StandardOutputLines.Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                Log(log, "runtime", line.Trim());
            }
        }
    }

    private static int ParseNodeMajorVersion(string? versionText)
    {
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return 0;
        }

        var trimmed = versionText.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[1..];
        }

        var firstSegment = trimmed.Split('.', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return int.TryParse(firstSegment, out var major) ? major : 0;
    }

    private async Task ValidateOpencodeUserExistsAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        var userCheck = await _dockerService.CheckOpencodeUserAsync(snapshot.Definition, log, cancellationToken);
        if (userCheck.IsSuccess)
        {
            return;
        }

        var details = string.IsNullOrWhiteSpace(userCheck.StandardError) ? userCheck.StandardOutput : userCheck.StandardError;
        throw new InvalidOperationException($"Workspace container is running but not provisioned. Run provisioning/recover workspace.{Environment.NewLine}{details}".Trim());
    }

    private static void LogAttach(Action<CommandLogEntry>? log, WorkspaceSnapshot snapshot, string message)
    {
        log?.Invoke(new CommandLogEntry
        {
            Source = "attach",
            Message = $"[attach:{snapshot.Definition.Workspace.Name}] {message}",
        });
    }

    private async Task<WorkspaceRuntimeState> GetRuntimeStateAsync(WorkspaceSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!File.Exists(snapshot.Paths.ComposePath))
        {
            return WorkspaceRuntimeState.Stopped;
        }

        var containerName = DockerService.GetWorkspaceContainerName(snapshot.Definition);
        try
        {
            var result = await _dockerService.RunSimpleDockerCommandAsync(
                ["ps", "--filter", $"name={containerName}", "--format", "{{.Names}}"],
                cancellationToken: cancellationToken);

            return result.IsSuccess && result.StandardOutputLines.Any(line => string.Equals(line.Trim(), containerName, StringComparison.OrdinalIgnoreCase))
                ? WorkspaceRuntimeState.Running
                : WorkspaceRuntimeState.Stopped;
        }
        catch
        {
            return WorkspaceRuntimeState.Unknown;
        }
    }

    private async Task<WorkspaceSessionState> GetSessionStateAsync(WorkspaceDefinition definition, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _dockerService.ListOpenCodeSessionsAsync(definition, cancellationToken: cancellationToken);
            if (!result.IsSuccess)
            {
                return WorkspaceSessionState.Unknown;
            }

            var sessionId = await _openCodeSessionService.SelectLatestSessionForWorkspaceAsync(
                result.StandardOutput,
                async session =>
                {
                    try
                    {
                        var export = await _dockerService.ExportOpenCodeSessionAsync(definition, session, cancellationToken: cancellationToken);
                        return _openCodeSessionService.TryGetSessionDirectory(export.StandardOutput);
                    }
                    catch
                    {
                        return null;
                    }
                },
                "/workspace");

            return string.IsNullOrWhiteSpace(sessionId)
                ? WorkspaceSessionState.NotRunning
                : WorkspaceSessionState.Resumable;
        }
        catch
        {
            return WorkspaceSessionState.Unknown;
        }
    }

    private static void EnsureSuccess(ProcessResult result, string failureMessage)
    {
        if (result.IsSuccess)
        {
            return;
        }

        var details = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;

        if (result.FailureClassification == WorkspaceFailureClassification.EnvironmentPortConflict)
        {
            throw new WorkspaceEnvironmentConflictException(details.Trim());
        }

        throw new InvalidOperationException($"{failureMessage}{Environment.NewLine}Command: {result.Command}{Environment.NewLine}Exit code: {result.ExitCode}{Environment.NewLine}{details}".Trim());
    }

    private static void Log(Action<CommandLogEntry>? log, string source, string message)
    {
        log?.Invoke(new CommandLogEntry
        {
            Source = source,
            Message = message,
        });
    }

    private GitWorkspaceProvider GetGitWorkspaceProvider()
    {
        if (_workspaceProvider is GitWorkspaceProvider gitProvider)
        {
            return gitProvider;
        }

        throw new InvalidOperationException("The configured workspace provider does not support existing Git checkout import.");
    }

    private string ResolveConfigurationPath(string rootPath, string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return WorkspacePathBuilder.NormalizeConfigurationRelativePath(configuredPath);
        }

        var discovery = _workspaceDiscoveryService.Discover(rootPath);
        if (discovery.Status == WorkspaceDiscoveryStatus.Found && !string.IsNullOrWhiteSpace(discovery.ConfigurationPath))
        {
            return discovery.ConfigurationPath;
        }

        return "workspace.yaml";
    }

    private sealed record GeneratedFilesUpdateResult(GeneratedWorkspaceArtifacts Artifacts, bool ComposeWasUpdated);
}
