using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Runtime;

/// <summary>
/// Keeps Docker as the first concrete container runtime while orchestration moves
/// to runtime abstractions that can support additional engines later.
/// </summary>
public sealed class DockerContainerRuntime : IContainerRuntime
{
    private readonly DockerService _dockerService;

    public DockerContainerRuntime(DockerService dockerService)
    {
        _dockerService = dockerService;
    }

    public string RuntimeId => "docker";

    public string GetWorkspaceContainerName(WorkspaceDefinition definition) => DockerService.GetWorkspaceContainerName(definition);

    public IReadOnlyList<string> CreatePermissionRepairArguments(string workspaceRootPath) => DockerService.CreatePermissionRepairArguments(workspaceRootPath);

    public Task<ProcessResult> StartAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null)
        => _dockerService.StartAsync(paths, definition, log, cancellationToken, repairComposeAsync);

    public Task<ProcessResult> ValidateAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null)
        => _dockerService.ValidateAsync(paths, definition, log, cancellationToken, repairComposeAsync);

    public Task<ProcessResult> StopAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => _dockerService.StopAsync(paths, definition, log, cancellationToken);

    public Task<ProcessResult> RemoveAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null)
        => _dockerService.RemoveAsync(paths, definition, log, cancellationToken, repairComposeAsync);

    public Task<ProcessResult> ResetAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null)
        => _dockerService.ResetAsync(paths, definition, log, cancellationToken, repairComposeAsync);

    public Task<ProcessResult?> ValidateVolatileEnvironmentAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => _dockerService.ValidateVolatileEnvironmentAsync(paths, definition, log, cancellationToken);

    public Task<ProcessResult> GetPsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => _dockerService.GetPsAsync(paths, definition, log, cancellationToken);

    public Task<ProcessResult> GetComposePsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => _dockerService.GetComposePsAsync(paths, definition, log, cancellationToken);

    public Task<ProcessResult> GetServiceLogsAsync(WorkspacePaths paths, WorkspaceDefinition definition, string serviceName, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => _dockerService.GetServiceLogsAsync(paths, definition, serviceName, log, cancellationToken);

    public Task<ProcessResult> RunProvisionScriptAsync(WorkspaceDefinition definition, WorkspacePaths paths, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => _dockerService.RunProvisionScriptAsync(definition, paths, log, cancellationToken);

    public Task<ProcessResult> InspectContainerImageAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => _dockerService.InspectContainerImageAsync(definition, log, cancellationToken);

    public Task<ProcessResult> InspectImageRepoTagsAsync(string imageId, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => _dockerService.InspectImageRepoTagsAsync(imageId, log, cancellationToken);

    public Task<ProcessResult> GetNodeToolDiagnosticsAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => _dockerService.GetNodeToolDiagnosticsAsync(definition, log, cancellationToken);

    public Task<ProcessResult> GetNodeAptPolicyAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => _dockerService.GetNodeAptPolicyAsync(definition, log, cancellationToken);

    public Task<ProcessResult> GetOsReleaseAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => _dockerService.GetOsReleaseAsync(definition, log, cancellationToken);

    public Task<ProcessResult> CheckOpencodeUserAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => _dockerService.CheckOpencodeUserAsync(definition, log, cancellationToken);

    public Task<ProcessResult> EnsureOpencodeUserDirectoriesAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => _dockerService.EnsureOpencodeUserDirectoriesAsync(definition, log, cancellationToken);

    public Task<ProcessResult> NormalizeWorkspaceFilePermissionsAsync(string workspaceRootPath, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => _dockerService.NormalizeWorkspaceFilePermissionsAsync(workspaceRootPath, log, cancellationToken);

    public Task<ProcessResult> RunSimpleDockerCommandAsync(IEnumerable<string> arguments, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => _dockerService.RunSimpleDockerCommandAsync(arguments, log, cancellationToken);

    public Task<ProcessResult> ListOpenCodeSessionsAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => _dockerService.ListOpenCodeSessionsAsync(definition, log, cancellationToken);

    public Task<ProcessResult> ExportOpenCodeSessionAsync(WorkspaceDefinition definition, string sessionId, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => _dockerService.ExportOpenCodeSessionAsync(definition, sessionId, log, cancellationToken);
}
