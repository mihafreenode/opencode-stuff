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
    private readonly WorkspaceRepository _workspaceRepository;
    private readonly WorkspaceResolver _workspaceResolver;
    private readonly ComposeGenerator _composeGenerator;
    private readonly EnvironmentFileGenerator _environmentFileGenerator;
    private readonly ProvisioningScriptGenerator _provisioningScriptGenerator;
    private readonly TerminalArtifactsGenerator _terminalArtifactsGenerator;
    private readonly AttachArtifactsGenerator _attachArtifactsGenerator;
    private readonly WorkspaceAppliedStateService _workspaceAppliedStateService;
    private readonly DockerService _dockerService;
    private readonly ITerminalLauncher _terminalLauncher;

    public WorkspaceOrchestrator(
        WorkspaceYamlService workspaceYamlService,
        WorkspaceRepository workspaceRepository,
        WorkspaceResolver workspaceResolver,
        ComposeGenerator composeGenerator,
        EnvironmentFileGenerator environmentFileGenerator,
        ProvisioningScriptGenerator provisioningScriptGenerator,
        TerminalArtifactsGenerator terminalArtifactsGenerator,
        AttachArtifactsGenerator attachArtifactsGenerator,
        WorkspaceAppliedStateService workspaceAppliedStateService,
        DockerService dockerService,
        ITerminalLauncher terminalLauncher)
    {
        _workspaceYamlService = workspaceYamlService;
        _workspaceRepository = workspaceRepository;
        _workspaceResolver = workspaceResolver;
        _composeGenerator = composeGenerator;
        _environmentFileGenerator = environmentFileGenerator;
        _provisioningScriptGenerator = provisioningScriptGenerator;
        _terminalArtifactsGenerator = terminalArtifactsGenerator;
        _attachArtifactsGenerator = attachArtifactsGenerator;
        _workspaceAppliedStateService = workspaceAppliedStateService;
        _dockerService = dockerService;
        _terminalLauncher = terminalLauncher;
    }

    public IReadOnlyList<WorkspaceRecord> LoadWorkspaceRecords() => _workspaceRepository.LoadAll();

    public WorkspaceSnapshot LoadSnapshot(string rootPath)
    {
        var paths = WorkspacePathBuilder.Build(rootPath);
        var definition = _workspaceYamlService.Read(paths.WorkspaceYamlPath);
        var record = _workspaceRepository.LoadAll().FirstOrDefault(item => string.Equals(item.RootPath, rootPath, StringComparison.OrdinalIgnoreCase))
            ?? new WorkspaceRecord
            {
                Name = definition.Workspace.Name,
                RootPath = rootPath,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            };

        var generatedArtifacts = GenerateArtifacts(definition, paths);
        var appliedState = _workspaceAppliedStateService.Read(paths.AppliedStatePath);
        var updateRequired = IsUpdateRequired(paths, generatedArtifacts, appliedState);

        var state = File.Exists(paths.ComposePath) && Directory.Exists(paths.RootPath)
            ? WorkspaceRuntimeState.Unknown
            : WorkspaceRuntimeState.Stopped;

        return new WorkspaceSnapshot
        {
            Record = record,
            Definition = definition,
            Paths = paths,
            RuntimeState = state,
            AppliedState = appliedState,
            UpdateRequired = updateRequired,
        };
    }

    public async Task<WorkspaceSnapshot> LoadSnapshotAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        var snapshot = LoadSnapshot(rootPath);
        var runtimeState = await GetRuntimeStateAsync(snapshot, cancellationToken);
        return new WorkspaceSnapshot
        {
            Record = snapshot.Record,
            Definition = snapshot.Definition,
            Paths = snapshot.Paths,
            RuntimeState = runtimeState,
            AppliedState = snapshot.AppliedState,
            UpdateRequired = snapshot.UpdateRequired,
        };
    }

    public WorkspaceSnapshot CreateWorkspace(string rootPath, WorkspaceDefinition definition)
    {
        var paths = WorkspacePathBuilder.Build(rootPath);
        CreateFolderStructure(paths);
        var generatedArtifacts = WriteGeneratedFiles(paths, definition);

        var now = DateTimeOffset.UtcNow;
        var record = new WorkspaceRecord
        {
            Name = definition.Workspace.Name,
            RootPath = rootPath,
            CreatedUtc = now,
            LastOpenedUtc = now,
            LastOperationName = "Create Workspace",
            LastOperationResult = "Workspace created.",
            LastOperationSucceeded = true,
            LastOperationUtc = now,
        };

        _workspaceRepository.Save(record);

        return new WorkspaceSnapshot
        {
            Record = record,
            Definition = definition,
            Paths = paths,
            RuntimeState = WorkspaceRuntimeState.Stopped,
            AppliedState = null,
            UpdateRequired = IsUpdateRequired(paths, generatedArtifacts, null),
        };
    }

    public async Task RegenerateAsync(WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        WriteGeneratedFiles(snapshot.Paths, snapshot.Definition);
        await Task.CompletedTask;
    }

    public async Task StartAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        WriteGeneratedFiles(snapshot.Paths, snapshot.Definition);
        Log(log, "app", $"Starting workspace '{snapshot.Definition.Workspace.Name}'.");
        var result = await _dockerService.StartAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken);
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
        WriteGeneratedFiles(snapshot.Paths, snapshot.Definition);
        Log(log, "app", $"Removing Docker resources for workspace '{snapshot.Definition.Workspace.Name}'.");
        var result = await _dockerService.RemoveAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken);
        EnsureSuccess(result, "Workspace removal failed while cleaning up Docker resources.");
    }

    public async Task ProvisionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var generatedArtifacts = WriteGeneratedFiles(snapshot.Paths, snapshot.Definition);
        Log(log, "app", $"Preparing workspace '{snapshot.Definition.Workspace.Name}'.");
        var startResult = await _dockerService.StartAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken);
        EnsureSuccess(startResult, "Workspace start failed before provisioning.");
        await ValidateWorkspaceRunningAsync(snapshot, log, cancellationToken);

        Log(log, "app", "Running provisioning script inside the workspace container.");
        var provisionResult = await _dockerService.RunProvisionScriptAsync(snapshot.Definition, snapshot.Paths, log, cancellationToken);
        EnsureSuccess(provisionResult, "Workspace provisioning failed.");
        await EnsureOpencodeUserDirectoriesAsync(snapshot, log, cancellationToken);
        await ValidateProvisionedWorkspaceAsync(snapshot, log, cancellationToken);
        _workspaceAppliedStateService.Write(snapshot.Paths.AppliedStatePath, _workspaceAppliedStateService.CreateState(generatedArtifacts));
    }

    public async Task AttachAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        Log(log, "app", $"Ensuring workspace '{snapshot.Definition.Workspace.Name}' is running before attach.");
        var startResult = await _dockerService.StartAsync(snapshot.Paths, snapshot.Definition, log, cancellationToken);
        EnsureSuccess(startResult, "Workspace start failed before attach.");
        await ValidateWorkspaceRunningAsync(snapshot, log, cancellationToken);
        await EnsureOpencodeUserDirectoriesAsync(snapshot, log, cancellationToken);
        await ValidateProvisionedWorkspaceAsync(snapshot, log, cancellationToken);

        await _terminalLauncher.LaunchAttachSessionAsync(snapshot, log, cancellationToken);
    }

    public async Task LaunchAttachForRunningWorkspaceAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        await ValidateWorkspaceRunningAsync(snapshot, log, cancellationToken);
        await EnsureOpencodeUserDirectoriesAsync(snapshot, log, cancellationToken);
        await ValidateProvisionedWorkspaceAsync(snapshot, log, cancellationToken);
        await _terminalLauncher.LaunchAttachSessionAsync(snapshot, log, cancellationToken);
    }

    public void SaveRecord(WorkspaceRecord record) => _workspaceRepository.Save(record);

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

        File.WriteAllText(paths.WorkspaceYamlPath, generatedArtifacts.WorkspaceYaml);
        File.WriteAllText(paths.ComposePath, generatedArtifacts.ComposeYaml);
        File.WriteAllText(paths.EnvironmentFilePath, generatedArtifacts.EnvironmentFile);
        File.WriteAllText(paths.StarshipConfigPath, generatedArtifacts.StarshipConfig.Replace("\r\n", "\n", StringComparison.Ordinal));
        File.WriteAllText(paths.ShellInitScriptPath, generatedArtifacts.ShellInitScript.Replace("\r\n", "\n", StringComparison.Ordinal));
        File.WriteAllText(paths.OpencodeWorkspaceShellPath, generatedArtifacts.OpencodeWorkspaceShellScript.Replace("\r\n", "\n", StringComparison.Ordinal));
        File.WriteAllText(paths.ScreenConfigPath, generatedArtifacts.ScreenConfig.Replace("\r\n", "\n", StringComparison.Ordinal));
        File.WriteAllText(paths.AttachWrapperScriptPath, generatedArtifacts.AttachWrapperScript.Replace("\r\n", "\n", StringComparison.Ordinal));
        File.WriteAllText(paths.TerminalDiagnosticsScriptPath, generatedArtifacts.TerminalDiagnosticsScript.Replace("\r\n", "\n", StringComparison.Ordinal));

        // The provisioning script runs inside Linux containers, so it must use LF
        // line endings even when the desktop app generated it on Windows.
        File.WriteAllText(paths.ProvisionScriptPath, generatedArtifacts.ProvisionScript.Replace("\r\n", "\n", StringComparison.Ordinal));
        return generatedArtifacts;
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
        var opencodeWorkspaceShellScript = _terminalArtifactsGenerator.GenerateOpencodeWorkspaceShellScript();
        var screenConfig = _terminalArtifactsGenerator.GenerateScreenConfiguration();
        var attachWrapper = _attachArtifactsGenerator.GenerateWindowsTerminalWrapper(definition);
        var diagnosticsWrapper = _attachArtifactsGenerator.GenerateTerminalDiagnosticsWrapper(definition);
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
            diagnosticsWrapper);

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
        var containerName = DockerService.GetWorkspaceContainerName(snapshot.Definition);
        var toolCheck = await _dockerService.RunSimpleDockerCommandAsync(
            new[] { "exec", containerName, "bash", "-lc", "command -v opencode && command -v screen && command -v node && command -v npm && getent passwd opencode" },
            log,
            cancellationToken);
        EnsureSuccess(toolCheck, "Workspace tool validation failed after provisioning.");
    }

    private async Task EnsureOpencodeUserDirectoriesAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        Log(log, "app", "Checking OpenCode user directories.");
        var result = await _dockerService.EnsureOpencodeUserDirectoriesAsync(snapshot.Definition, log, cancellationToken);
        EnsureSuccess(result, "OpenCode user directory initialization failed.");
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

    private static void EnsureSuccess(ProcessResult result, string failureMessage)
    {
        if (result.IsSuccess)
        {
            return;
        }

        var details = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
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
}
