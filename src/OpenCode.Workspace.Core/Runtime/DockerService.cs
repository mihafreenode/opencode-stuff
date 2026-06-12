using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Runtime;

/// <summary>
/// Wraps Docker CLI usage behind readable methods so application code can express
/// intent in terms of workspace operations instead of shell command assembly.
/// </summary>
public sealed class DockerService
{
    private readonly ProcessRunner _processRunner;
    private const string DockerUnavailableMessage = "Docker is not reachable from this environment. Check Docker Desktop / WSL integration.";

    public DockerService(ProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public Task<ProcessResult> StartAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => StartWithCleanupOnFailureAsync(paths, definition, log, cancellationToken);

    public Task<ProcessResult> StopAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunComposeAsync(paths, definition, new[] { "stop" }, log, cancellationToken);

    public Task<ProcessResult> RemoveAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunComposeAsync(paths, definition, new[] { "down", "--remove-orphans" }, log, cancellationToken);

    public Task<ProcessResult> GetPsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunComposeAsync(paths, definition, new[] { "ps", "--status", "running", "--services" }, log, cancellationToken);

    public Task<ProcessResult> RunProvisionScriptAsync(WorkspaceDefinition definition, WorkspacePaths paths, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var containerName = GetWorkspaceContainerName(definition);
        return RunDockerCommandAsync(
            new[] { "exec", containerName, "bash", "/opt/opencode-workspace/config/provision.sh" },
            paths.RootPath,
            log,
            cancellationToken);
    }

    public Task<ProcessResult> EnsureOpencodeUserDirectoriesAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var containerName = GetWorkspaceContainerName(definition);
        const string command = "repair_needed=0; if [ ! -d /home/opencode/.local/share/opencode/log ] || ! su -s /bin/bash -c 'test -w /home/opencode/.local/share/opencode/log' opencode >/dev/null 2>&1; then repair_needed=1; fi; if [ \"$repair_needed\" -eq 1 ]; then printf '[attach] Initializing OpenCode user directories.\\n'; fi; mkdir -p /home/opencode/.local/share/opencode/log /home/opencode/.config/opencode /home/opencode/.cache/opencode; chown -R opencode:opencode /home/opencode/.local /home/opencode/.config /home/opencode/.cache; test -d /home/opencode/.local/share/opencode/log; su -s /bin/bash -c 'test -w /home/opencode/.local/share/opencode/log' opencode";
        return RunDockerCommandAsync(new[] { "exec", containerName, "bash", "-lc", command }, null, log, cancellationToken);
    }

    public Task<ProcessResult> NormalizeWorkspaceFilePermissionsAsync(string workspaceRootPath, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunDockerCommandAsync(CreatePermissionRepairArguments(workspaceRootPath), null, log, cancellationToken);

    public Task<ProcessResult> RunSimpleDockerCommandAsync(IEnumerable<string> arguments, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunDockerCommandAsync(arguments, null, log, cancellationToken);

    public static string GetWorkspaceContainerName(WorkspaceDefinition definition) => $"{WorkspacePathBuilder.Slugify(definition.Workspace.Name)}-workspace";

    public static IReadOnlyList<string> CreatePermissionRepairArguments(string workspaceRootPath)
    {
        const string command = "chmod -R u+rwX,go+rwX /target || true; find /target -type d -exec chmod a+rwx {} + || true";
        return new[]
        {
            "run",
            "--rm",
            "-v",
            $"{workspaceRootPath}:/target",
            "ubuntu:24.04",
            "bash",
            "-lc",
            command,
        };
    }

    private Task<ProcessResult> RunComposeAsync(WorkspacePaths paths, WorkspaceDefinition definition, IEnumerable<string> composeArguments, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        var projectName = WorkspacePathBuilder.Slugify(definition.Workspace.Name);
        var arguments = new List<string>
        {
            "compose",
            "--project-name",
            projectName,
            "--file",
            paths.ComposePath,
        };

        arguments.AddRange(composeArguments);

        return RunDockerCommandAsync(arguments, paths.RootPath, log, cancellationToken);
    }

    private async Task<ProcessResult> RunDockerCommandAsync(IEnumerable<string> arguments, string? workingDirectory, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        try
        {
            return await _processRunner.RunAsync(
                "docker",
                arguments,
                workingDirectory,
                (isError, line) => log?.Invoke(new CommandLogEntry
                {
                    Source = isError ? "docker:err" : "docker",
                    Message = line,
                }),
                cancellationToken);
        }
        catch (Exception exception)
        {
            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = DockerUnavailableMessage,
            });

            throw new InvalidOperationException(DockerUnavailableMessage, exception);
        }
    }

    private async Task<ProcessResult> StartWithCleanupOnFailureAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        var result = await RunComposeAsync(paths, definition, new[] { "up", "-d" }, log, cancellationToken);
        if (result.IsSuccess)
        {
            return result;
        }

        log?.Invoke(new CommandLogEntry
        {
            Source = "app",
            Message = "Docker reported a failed start. Cleaning up partial containers so the next retry starts from a consistent state.",
        });

        await RunComposeAsync(paths, definition, new[] { "down", "--remove-orphans" }, log, cancellationToken);
        return result;
    }
}
