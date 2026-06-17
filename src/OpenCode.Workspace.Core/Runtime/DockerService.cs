using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Runtime;

/// <summary>
/// Wraps Docker CLI usage behind readable methods so application code can express
/// intent in terms of workspace operations instead of shell command assembly.
/// </summary>
public sealed class DockerService
{
    private readonly IProcessRunner _processRunner;
    private const string DockerUnavailableMessage = "Docker is not reachable from this environment. Check Docker Desktop / WSL integration.";

    public DockerService(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public Task<ProcessResult> StartAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null)
        => StartWithCleanupOnFailureAsync(paths, definition, log, cancellationToken, repairComposeAsync);

    public Task<ProcessResult> ValidateAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null)
        => ValidateComposeAsync(paths, definition, log, cancellationToken, repairComposeAsync);

    public Task<ProcessResult> StopAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunComposeAsync(paths, definition, new[] { "stop" }, log, cancellationToken);

    public Task<ProcessResult> RemoveAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null)
        => RunComposeWithValidationRepairAsync(paths, definition, new[] { "down", "--remove-orphans" }, log, cancellationToken, repairComposeAsync);

    public Task<ProcessResult> ResetAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null)
        => RunComposeWithValidationRepairAsync(paths, definition, new[] { "down", "-v", "--remove-orphans" }, log, cancellationToken, repairComposeAsync);

    public Task<ProcessResult> GetPsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunComposeAsync(paths, definition, new[] { "ps", "--status", "running", "--services" }, log, cancellationToken);

    public Task<ProcessResult> GetComposePsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunComposeAsync(paths, definition, new[] { "ps" }, log, cancellationToken);

    public Task<ProcessResult> GetServiceLogsAsync(WorkspacePaths paths, WorkspaceDefinition definition, string serviceName, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunComposeAsync(paths, definition, new[] { "logs", serviceName }, log, cancellationToken);

    public Task<ProcessResult> RunProvisionScriptAsync(WorkspaceDefinition definition, WorkspacePaths paths, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var containerName = GetWorkspaceContainerName(definition);
        return RunDockerCommandAsync(
            new[] { "exec", containerName, "bash", "/opt/opencode-workspace/config/provision.sh" },
            paths.RootPath,
            log,
            cancellationToken);
    }

    public Task<ProcessResult> InspectContainerImageAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var containerName = GetWorkspaceContainerName(definition);
        return RunDockerCommandAsync(new[] { "inspect", containerName, "--format", "{{.Image}}" }, null, log, cancellationToken);
    }

    public Task<ProcessResult> InspectImageRepoTagsAsync(string imageId, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunDockerCommandAsync(new[] { "inspect", imageId, "--format", "{{json .RepoTags}}" }, null, log, cancellationToken);

    public Task<ProcessResult> GetNodeToolDiagnosticsAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var containerName = GetWorkspaceContainerName(definition);
        return RunDockerCommandAsync(new[] { "exec", containerName, "bash", "-lc", "which node && node --version && which npm && npm --version" }, null, log, cancellationToken);
    }

    public Task<ProcessResult> GetNodeAptPolicyAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var containerName = GetWorkspaceContainerName(definition);
        return RunDockerCommandAsync(new[] { "exec", containerName, "bash", "-lc", "apt-cache policy nodejs | sed -n '1,20p'" }, null, log, cancellationToken);
    }

    public Task<ProcessResult> GetOsReleaseAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var containerName = GetWorkspaceContainerName(definition);
        return RunDockerCommandAsync(new[] { "exec", containerName, "bash", "-lc", "cat /etc/os-release" }, null, log, cancellationToken);
    }

    public Task<ProcessResult> CheckOpencodeUserAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var containerName = GetWorkspaceContainerName(definition);
        return RunDockerCommandAsync(new[] { "exec", containerName, "id", "opencode" }, null, log, cancellationToken);
    }

    public Task<ProcessResult> EnsureOpencodeUserDirectoriesAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var containerName = GetWorkspaceContainerName(definition);
        const string command = "id opencode >/dev/null 2>&1 || { echo 'Workspace container is running but not provisioned. Run provisioning/recover workspace.' >&2; exit 1; }; repair_needed=0; if [ ! -d /home/opencode/.local/share/opencode/log ] || ! su -s /bin/bash -c 'test -w /home/opencode/.local/share/opencode/log' opencode >/dev/null 2>&1; then repair_needed=1; fi; if [ \"$repair_needed\" -eq 1 ]; then printf '[attach] Initializing OpenCode user directories.\\n'; fi; mkdir -p /home/opencode/.local/share/opencode/log /home/opencode/.config/opencode /home/opencode/.cache/opencode; chown -R opencode:opencode /home/opencode/.local /home/opencode/.config /home/opencode/.cache; test -d /home/opencode/.local/share/opencode/log; su -s /bin/bash -c 'test -w /home/opencode/.local/share/opencode/log' opencode";
        return RunDockerCommandAsync(new[] { "exec", containerName, "bash", "-lc", command }, null, log, cancellationToken);
    }

    public Task<ProcessResult> NormalizeWorkspaceFilePermissionsAsync(string workspaceRootPath, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunDockerCommandAsync(CreatePermissionRepairArguments(workspaceRootPath), null, log, cancellationToken);

    public Task<ProcessResult> RunSimpleDockerCommandAsync(IEnumerable<string> arguments, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunDockerCommandAsync(arguments, null, log, cancellationToken);

    public Task<ProcessResult> ListOpenCodeSessionsAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var containerName = GetWorkspaceContainerName(definition);
        return RunDockerCommandAsync(new[] { "exec", containerName, "bash", "-lc", "cd /workspace && opencode session list || true" }, null, log, cancellationToken);
    }

    public Task<ProcessResult> ExportOpenCodeSessionAsync(WorkspaceDefinition definition, string sessionId, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var containerName = GetWorkspaceContainerName(definition);
        var command = $"cd /workspace && opencode export {sessionId} || true";
        return RunDockerCommandAsync(new[] { "exec", containerName, "bash", "-lc", command }, null, log, cancellationToken);
    }

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

        foreach (var profile in GetComposeProfiles(definition))
        {
            arguments.Add("--profile");
            arguments.Add(profile);
        }

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

    private async Task<ProcessResult> StartWithCleanupOnFailureAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log, CancellationToken cancellationToken, Func<CancellationToken, Task<bool>>? repairComposeAsync)
    {
        var validationResult = await ValidateComposeAsync(paths, definition, log, cancellationToken, repairComposeAsync);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        var portConflictResult = await DetectOraclePortConflictAsync(paths, definition, log, cancellationToken);
        if (portConflictResult is not null)
        {
            return portConflictResult;
        }

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

    private async Task<ProcessResult> RunComposeWithValidationRepairAsync(WorkspacePaths paths, WorkspaceDefinition definition, IEnumerable<string> composeArguments, Action<CommandLogEntry>? log, CancellationToken cancellationToken, Func<CancellationToken, Task<bool>>? repairComposeAsync)
    {
        var validationResult = await ValidateComposeAsync(paths, definition, log, cancellationToken, repairComposeAsync);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        return await RunComposeAsync(paths, definition, composeArguments, log, cancellationToken);
    }

    private async Task<ProcessResult> ValidateComposeAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log, CancellationToken cancellationToken, Func<CancellationToken, Task<bool>>? repairComposeAsync)
    {
        var validationResult = await RunComposeAsync(paths, definition, new[] { "config" }, log, cancellationToken);
        if (validationResult.IsSuccess)
        {
            return validationResult;
        }

        LogComposeValidationFailure(log, validationResult);

        if (repairComposeAsync is null)
        {
            return validationResult;
        }

        log?.Invoke(new CommandLogEntry
        {
            Source = "app",
            Message = "Stale compose detected. Attempting compose regeneration/repair.",
        });

        var repaired = await repairComposeAsync(cancellationToken);
        if (!repaired)
        {
            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = "Compose regeneration/repair did not change compose.yaml.",
            });
            return validationResult;
        }

        log?.Invoke(new CommandLogEntry
        {
            Source = "app",
            Message = "Compose regenerated/repaired. Re-running docker compose config.",
        });

        var repairedValidationResult = await RunComposeAsync(paths, definition, new[] { "config" }, log, cancellationToken);
        log?.Invoke(new CommandLogEntry
        {
            Source = "app",
            Message = repairedValidationResult.IsSuccess
                ? "Validation after repair succeeded."
                : "Validation after repair failed.",
        });

        if (!repairedValidationResult.IsSuccess)
        {
            LogComposeValidationFailure(log, repairedValidationResult);
        }

        return repairedValidationResult;
    }

    private static void LogComposeValidationFailure(Action<CommandLogEntry>? log, ProcessResult validationResult)
    {
        var validationError = string.IsNullOrWhiteSpace(validationResult.StandardError)
            ? validationResult.StandardOutput
            : validationResult.StandardError;

        log?.Invoke(new CommandLogEntry
        {
            Source = "app",
            Message = $"Docker Compose validation failed. {validationError}".Trim(),
        });
    }

    private static IReadOnlyList<string> GetComposeProfiles(WorkspaceDefinition definition)
        => definition.Services
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(service => service, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private async Task<ProcessResult?> DetectOraclePortConflictAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        if (!OracleWorkspaceFamily.IsOracleWorkspace(definition))
        {
            return null;
        }

        var oracleSettings = OracleWorkspaceSettings.From(definition);
        var projectName = WorkspacePathBuilder.Slugify(definition.Workspace.Name);
        var ports = new List<OracleHostPortCheck>
        {
            new(oracleSettings.HostPort, "Oracle", "another Oracle demo workspace is running", "Oracle Database is already running locally"),
        };

        if (OracleWorkspaceFamily.HasApex(definition))
        {
            ports.Add(new OracleHostPortCheck(oracleSettings.OrdsPort, "Oracle ORDS/APEX", "another Oracle APEX workspace is running", "another service is already using the ORDS/APEX port locally"));
        }

        var dockerPsResult = await RunDockerCommandAsync(new[] { "ps", "--format", "{{.Names}}\t{{.Ports}}" }, paths.RootPath, log, cancellationToken);
        var composePsResult = await GetComposePsAsync(paths, definition, log, cancellationToken);

        foreach (var port in ports)
        {
            var containerOwner = dockerPsResult.IsSuccess ? FindPortOwningContainer(dockerPsResult.StandardOutputLines, port.Port, projectName) : null;
            if (containerOwner?.BelongsToCurrentWorkspace == true)
            {
                continue;
            }

            var hostDiagnostic = await GetHostPortDiagnosticAsync(port.Port, cancellationToken);
            if (containerOwner is null && !hostDiagnostic.IsInUse)
            {
                continue;
            }

            var message = BuildOraclePortConflictMessage(port, containerOwner, hostDiagnostic, dockerPsResult, composePsResult);
            log?.Invoke(new CommandLogEntry { Source = "app", Message = message });

            return new ProcessResult
            {
                Command = $"oracle-port-preflight {port.Port}",
                ExitCode = 1,
                StandardOutput = string.Empty,
                StandardError = message,
                StandardOutputLines = Array.Empty<string>(),
                StandardErrorLines = message.Split(Environment.NewLine),
                Duration = TimeSpan.Zero,
                FailureClassification = WorkspaceFailureClassification.EnvironmentPortConflict,
            };
        }

        return null;
    }

    private async Task<HostPortDiagnostic> GetHostPortDiagnosticAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var script = $"$connections = Get-NetTCPConnection -LocalPort {port} -State Listen -ErrorAction SilentlyContinue; if (-not $connections) {{ exit 0 }}; foreach ($connection in ($connections | Sort-Object OwningProcess -Unique)) {{ $processName = 'unknown'; try {{ $processName = (Get-Process -Id $connection.OwningProcess -ErrorAction Stop).ProcessName }} catch {{ }}; Write-Output ('LISTEN port={port} pid=' + $connection.OwningProcess + ' process=' + $processName) }}";
                var result = await _processRunner.RunAsync("powershell.exe", new[] { "-NoProfile", "-Command", script }, cancellationToken: cancellationToken);
                var lines = result.StandardOutputLines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).ToList();
                return new HostPortDiagnostic(lines.Count > 0, lines);
            }

            var command = $"ss -ltnp '( sport = :{port} )' 2>/dev/null || true";
            var linuxResult = await _processRunner.RunAsync("bash", new[] { "-lc", command }, cancellationToken: cancellationToken);
            var linuxLines = linuxResult.StandardOutputLines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).ToList();
            return new HostPortDiagnostic(linuxLines.Count > 1, linuxLines);
        }
        catch
        {
            return new HostPortDiagnostic(false, Array.Empty<string>());
        }
    }

    private static ContainerPortOwner? FindPortOwningContainer(IEnumerable<string> dockerPsLines, int hostPort, string currentProjectName)
    {
        foreach (var line in dockerPsLines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || !trimmed.Contains($":{hostPort}->", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = trimmed.Split('\t', 2);
            var containerName = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(containerName))
            {
                continue;
            }

            var belongsToCurrentWorkspace = containerName.Equals($"{currentProjectName}-workspace", StringComparison.OrdinalIgnoreCase)
                || containerName.StartsWith(currentProjectName + "-", StringComparison.OrdinalIgnoreCase);

            return new ContainerPortOwner(containerName, belongsToCurrentWorkspace);
        }

        return null;
    }

    private static string BuildOraclePortConflictMessage(OracleHostPortCheck port, ContainerPortOwner? containerOwner, HostPortDiagnostic hostDiagnostic, ProcessResult dockerPsResult, ProcessResult composePsResult)
    {
        var lines = new List<string>
        {
            $"{port.Label} port {port.Port} is already in use.",
            string.Empty,
            "Likely causes:",
            $"- {port.LikelyCauseOne}",
            $"- {port.LikelyCauseTwo}",
            "- stale container still owns the port",
        };

        if (containerOwner is not null)
        {
            lines.Add(string.Empty);
            lines.Add($"Owning container: {containerOwner.ContainerName}");
        }

        if (hostDiagnostic.Details.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Host port details:");
            lines.AddRange(hostDiagnostic.Details.Select(detail => $"- {detail}"));
        }

        if (composePsResult.IsSuccess && !string.IsNullOrWhiteSpace(composePsResult.StandardOutput))
        {
            lines.Add(string.Empty);
            lines.Add("This workspace docker compose ps:");
            lines.Add(composePsResult.StandardOutput.Trim());
        }

        if (dockerPsResult.IsSuccess && !string.IsNullOrWhiteSpace(dockerPsResult.StandardOutput))
        {
            lines.Add(string.Empty);
            lines.Add("Running containers:");
            lines.Add(dockerPsResult.StandardOutput.Trim());
        }

        lines.Add(string.Empty);
        lines.Add("Suggested actions:");
        lines.Add("- Stop other Oracle workspace");
        lines.Add("- Use a different port");
        lines.Add("- Open Docker containers");
        lines.Add("- Run recovery cleanup for this workspace only");
        lines.Add("- Retry");

        return string.Join(Environment.NewLine, lines);
    }

    private sealed record OracleHostPortCheck(int Port, string Label, string LikelyCauseOne, string LikelyCauseTwo);

    private sealed record ContainerPortOwner(string ContainerName, bool BelongsToCurrentWorkspace);

    private sealed record HostPortDiagnostic(bool IsInUse, IReadOnlyList<string> Details);
}
