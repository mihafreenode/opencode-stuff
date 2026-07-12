using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using System.Text.RegularExpressions;

namespace OpenCode.Workspace.Core.Runtime;

/// <summary>
/// Wraps Docker CLI usage behind readable methods so application code can express
/// intent in terms of workspace operations instead of shell command assembly.
/// </summary>
public sealed class DockerService
{
    private static readonly TimeSpan DockerPsPreflightTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DockerComposeDiagnosticTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DockerAvailabilityProbeTimeout = TimeSpan.FromSeconds(5);
    private readonly IProcessRunner _processRunner;
    private readonly WorkspaceRuntimeStateService _workspaceRuntimeStateService;
    private bool _preferWslDocker;
    private const string DockerUnavailableMessage = "Docker is not reachable from this environment. Check Docker Desktop / WSL integration.";
    private const string WindowsDockerUnavailableButWslAvailableMessage = "Docker is reachable from WSL but not from Windows. Enable Docker Desktop Windows CLI integration or configure this workspace to use WSL Docker.";

    public DockerService(IProcessRunner processRunner, WorkspaceRuntimeStateService? workspaceRuntimeStateService = null)
    {
        _processRunner = processRunner;
        _workspaceRuntimeStateService = workspaceRuntimeStateService ?? new WorkspaceRuntimeStateService();
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

    public async Task<ProcessResult?> ValidateVolatileEnvironmentAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var oraclePortConflictResult = await DetectOraclePortConflictAsync(paths, definition, log, cancellationToken);
        if (oraclePortConflictResult is not null)
        {
            return oraclePortConflictResult;
        }

        return await DetectAnalyticsPortConflictAsync(paths, definition, log, cancellationToken);
    }

    public Task<ProcessResult> GetPsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunComposeAsync(paths, definition, new[] { "ps", "--status", "running", "--services" }, log, cancellationToken);

    public Task<ProcessResult> GetComposePsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunComposeAsync(paths, definition, new[] { "ps" }, log, cancellationToken);

    public Task<ProcessResult> GetServiceLogsAsync(WorkspacePaths paths, WorkspaceDefinition definition, string serviceName, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunComposeAsync(paths, definition, new[] { "logs", serviceName }, log, cancellationToken);

    public Task<ProcessResult> RestartServiceAsync(WorkspacePaths paths, WorkspaceDefinition definition, string serviceName, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunComposeAsync(paths, definition, new[] { "restart", serviceName }, log, cancellationToken, timeout: TimeSpan.FromMinutes(5));

    public Task<ProcessResult> RunProvisionScriptAsync(WorkspaceDefinition definition, WorkspacePaths paths, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var containerName = GetWorkspaceContainerName(definition);
        return RunDockerCommandAsync(
            new[] { "exec", containerName, "bash", "/opt/opencode-workspace/config/provision.sh" },
            paths.RootPath,
            log,
            cancellationToken);
    }

    public Task<ProcessResult> RepairOracleOrdsGatewayAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var containerName = GetServiceContainerName(definition, "oracle-ords");
        return RunDockerCommandAsync(
            new[] { "exec", containerName, "bash", "/etc/ords/config/repair-ords-db.sh" },
            paths.RootPath,
            log,
            cancellationToken,
            timeout: TimeSpan.FromMinutes(10));
    }

    public Task<ProcessResult> ProbeHttpGetFromWorkspaceAsync(WorkspaceDefinition definition, string url, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var containerName = GetWorkspaceContainerName(definition);
        var command = "tmp_headers=$(mktemp) && tmp_body=$(mktemp) && http_code=$(curl -sS -D \"$tmp_headers\" -o \"$tmp_body\" -w '%{http_code}' \"$1\" || true) && location=$(grep -i '^Location:' \"$tmp_headers\" | tail -n 1 | cut -d' ' -f2- | tr -d '\\r' | xargs || true) && body=$(head -c 300 \"$tmp_body\" | tr '\\n' ' ') && printf 'status=%s\\nlocation=%s\\nbody=%s\\n' \"$http_code\" \"$location\" \"$body\"";
        return RunDockerCommandAsync(new[] { "exec", containerName, "bash", "-lc", command, "bash", url }, null, log, cancellationToken);
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
        const string command = "id opencode >/dev/null 2>&1 || { echo 'Workspace container is running but not provisioned. Run Prepare Workspace or Repair Runtime.' >&2; exit 1; }; repair_needed=0; if [ ! -d /home/opencode/.local/share/opencode/log ] || ! su -s /bin/bash -c 'test -w /home/opencode/.local/share/opencode/log' opencode >/dev/null 2>&1; then repair_needed=1; fi; if [ \"$repair_needed\" -eq 1 ]; then printf '[attach] Initializing OpenCode user directories.\\n'; fi; mkdir -p /home/opencode/.local/share/opencode/log /home/opencode/.config/opencode /home/opencode/.cache/opencode; chown -R opencode:opencode /home/opencode/.local /home/opencode/.config /home/opencode/.cache; test -d /home/opencode/.local/share/opencode/log; su -s /bin/bash -c 'test -w /home/opencode/.local/share/opencode/log' opencode";
        return RunDockerCommandAsync(new[] { "exec", containerName, "bash", "-lc", command }, null, log, cancellationToken);
    }

    public Task<ProcessResult> NormalizeWorkspaceFilePermissionsAsync(string workspaceRootPath, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        => RunDockerCommandAsync(CreatePermissionRepairArguments(workspaceRootPath), null, log, cancellationToken);

    public Task<ProcessResult> RunSimpleDockerCommandAsync(IEnumerable<string> arguments, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var argumentList = arguments.ToList();
        return IsDockerPsCommand(argumentList)
            ? RunDockerPsCommandAsync(argumentList, null, log, cancellationToken, DockerPsPreflightTimeout)
            : RunDockerCommandAsync(argumentList, null, log, cancellationToken);
    }

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

    public static string GetServiceContainerName(WorkspaceDefinition definition, string serviceName) => $"{WorkspacePathBuilder.Slugify(definition.Workspace.Name)}-{serviceName}-1";

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

    private Task<ProcessResult> RunComposeAsync(WorkspacePaths paths, WorkspaceDefinition definition, IEnumerable<string> composeArguments, Action<CommandLogEntry>? log, CancellationToken cancellationToken, TimeSpan? timeout = null)
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

        return RunDockerCommandAsync(arguments, paths.RootPath, log, cancellationToken, timeout);
    }

    private async Task<ProcessResult> RunDockerCommandAsync(IEnumerable<string> arguments, string? workingDirectory, Action<CommandLogEntry>? log, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var argumentList = arguments.ToList();
        var preferWslForThisCommand = _preferWslDocker && SupportsWslDockerFallback(argumentList) && !IsComposeCommand(argumentList);

        if (OperatingSystem.IsWindows() && preferWslForThisCommand)
        {
            var preferredWslResult = await TryRunWslDockerCommandAsync(argumentList, workingDirectory, log, cancellationToken, timeout);
            if (preferredWslResult is not null)
            {
                return preferredWslResult;
            }
        }

        try
        {
            return await RunCommandWithLoggingAsync(
                "docker",
                argumentList,
                workingDirectory,
                log,
                cancellationToken,
                timeout,
                commandSource: "docker:cmd",
                outputSource: "docker",
                errorSource: "docker:err");
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested || IsDockerExecCommand(argumentList))
            {
                throw;
            }

            if (OperatingSystem.IsWindows() && SupportsWslDockerFallback(argumentList))
            {
                var wslResult = await TryRunWslDockerCommandAsync(argumentList, workingDirectory, log, cancellationToken, timeout);
                if (wslResult is { IsSuccess: true })
                {
                    if (!IsComposeCommand(argumentList))
                    {
                        _preferWslDocker = true;
                    }

                    log?.Invoke(new CommandLogEntry
                    {
                        Source = "app",
                        Message = "Windows Docker CLI is unavailable or hung. Using WSL Docker for this operation.",
                    });
                    return wslResult;
                }

                var wslAwareMessage = await BuildDockerUnavailableMessageAsync(log, cancellationToken, exception, wslResult);
                log?.Invoke(new CommandLogEntry
                {
                    Source = "app",
                    Message = wslAwareMessage,
                });

                throw new InvalidOperationException(wslAwareMessage, exception);
            }

            var message = await BuildDockerUnavailableMessageAsync(log, cancellationToken, exception);
            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = message,
            });

            throw new InvalidOperationException(message, exception);
        }
    }

    private Task<ProcessResult> RunCommandWithLoggingAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, Action<CommandLogEntry>? log, CancellationToken cancellationToken, TimeSpan? timeout, string commandSource, string outputSource, string errorSource)
    {
        log?.Invoke(new CommandLogEntry
        {
            Source = commandSource,
            Message = $"{fileName} {string.Join(' ', arguments.Select(argument => argument.Contains(' ') ? $"\"{argument}\"" : argument))}",
        });

        return _processRunner.RunAsync(
            fileName,
            arguments,
            workingDirectory,
            (isError, line) => log?.Invoke(new CommandLogEntry
            {
                Source = isError ? errorSource : outputSource,
                Message = line,
            }),
            cancellationToken,
            timeout);
    }

    private async Task<ProcessResult> RunDockerPsPreflightAsync(string? workingDirectory, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        var arguments = new[] { "ps", "--format", "{{.Names}}\t{{.Ports}}" };

        try
        {
            return await RunDockerPsCommandAsync(arguments, workingDirectory, log, cancellationToken, DockerPsPreflightTimeout);
        }
        catch (Exception exception)
        {
            var message = await BuildDockerUnavailableMessageAsync(log, cancellationToken, exception);
            log?.Invoke(new CommandLogEntry { Source = "app", Message = message });
            return CreateDockerUnavailableResult("docker ps --format {{.Names}}\t{{.Ports}}", message);
        }
    }

    private async Task<ProcessResult> RunDockerPsCommandAsync(IReadOnlyList<string> arguments, string? workingDirectory, Action<CommandLogEntry>? log, CancellationToken cancellationToken, TimeSpan timeout)
    {
        try
        {
            return await RunCommandWithLoggingAsync(
                "docker",
                arguments,
                workingDirectory,
                log,
                cancellationToken,
                timeout,
                commandSource: "docker:cmd",
                outputSource: "docker",
                errorSource: "docker:err");
        }
        catch (Exception windowsException) when (OperatingSystem.IsWindows())
        {
            var wslResult = await TryRunWslDockerCommandAsync(arguments, workingDirectory, log, cancellationToken, timeout);
            if (wslResult is { IsSuccess: true })
            {
                log?.Invoke(new CommandLogEntry
                {
                    Source = "app",
                    Message = "Windows Docker CLI is unavailable or hung for docker ps. Using WSL Docker for this runtime check.",
                });
                return wslResult;
            }

            var message = await BuildDockerUnavailableMessageAsync(log, cancellationToken, windowsException, wslResult);
            throw new InvalidOperationException(message, windowsException);
        }
    }

    private static bool IsDockerPsCommand(IReadOnlyList<string> arguments)
        => arguments.Count > 0 && string.Equals(arguments[0], "ps", StringComparison.OrdinalIgnoreCase);

    private async Task<ProcessResult> TryGetComposePsForDiagnosticsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        try
        {
            return await RunComposeAsync(paths, definition, new[] { "ps" }, log, cancellationToken, DockerComposeDiagnosticTimeout);
        }
        catch (Exception exception)
        {
            var message = $"docker compose ps diagnostics unavailable: {exception.Message}";
            log?.Invoke(new CommandLogEntry { Source = "app", Message = message });
            return new ProcessResult
            {
                Command = "docker compose ps",
                ExitCode = 1,
                StandardOutput = string.Empty,
                StandardError = message,
                StandardOutputLines = Array.Empty<string>(),
                StandardErrorLines = [message],
                Duration = TimeSpan.Zero,
                FailureClassification = WorkspaceFailureClassification.EnvironmentDockerUnavailable,
            };
        }
    }

    private Task<ProcessResult?> TryRunWslDockerCommandAsync(IEnumerable<string> dockerArguments, string? workingDirectory, Action<CommandLogEntry>? log, CancellationToken cancellationToken, TimeSpan? timeout)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<ProcessResult?>(null);
        }

        return TryRunWslDockerCommandCoreAsync(dockerArguments.ToArray(), workingDirectory, log, cancellationToken, timeout);
    }

    private async Task<ProcessResult?> TryRunWslDockerCommandCoreAsync(IReadOnlyList<string> dockerArguments, string? workingDirectory, Action<CommandLogEntry>? log, CancellationToken cancellationToken, TimeSpan? timeout)
    {
        try
        {
            var arguments = new List<string> { "--", "docker" };
            arguments.AddRange(PrepareDockerArgumentsForWsl(dockerArguments));
            return await RunCommandWithLoggingAsync(
                "wsl.exe",
                arguments,
                workingDirectory,
                log,
                cancellationToken,
                timeout,
                commandSource: "wsl:cmd",
                outputSource: "wsl",
                errorSource: "wsl:err");
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> BuildDockerUnavailableMessageAsync(Action<CommandLogEntry>? log, CancellationToken cancellationToken, Exception windowsException, ProcessResult? knownWslResult = null)
    {
        if (OperatingSystem.IsWindows())
        {
            var wslResult = await TryRunWslDockerCommandAsync(new[] { "ps", "--format", "{{.Names}}" }, null, log, cancellationToken, DockerAvailabilityProbeTimeout);
            if (wslResult is { IsSuccess: true })
            {
                return WindowsDockerUnavailableButWslAvailableMessage;
            }
        }

        return DockerUnavailableMessage;
    }

    private static ProcessResult CreateDockerUnavailableResult(string command, string message)
        => new()
        {
            Command = command,
            ExitCode = 1,
            StandardOutput = string.Empty,
            StandardError = message,
            StandardOutputLines = Array.Empty<string>(),
            StandardErrorLines = [message],
            Duration = TimeSpan.Zero,
            FailureClassification = WorkspaceFailureClassification.EnvironmentDockerUnavailable,
        };

    private static bool SupportsWslDockerFallback(IReadOnlyList<string> arguments)
        => arguments.Count > 0 && !IsDockerExecCommand(arguments);

    private static bool IsComposeCommand(IReadOnlyList<string> arguments)
        => arguments.Count > 0 && string.Equals(arguments[0], "compose", StringComparison.OrdinalIgnoreCase);

    private static bool IsDockerExecCommand(IReadOnlyList<string> arguments)
        => arguments.Count > 0 && string.Equals(arguments[0], "exec", StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<string> PrepareDockerArgumentsForWsl(IReadOnlyList<string> arguments)
    {
        var translated = new List<string>(arguments.Count);

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            var previous = index == 0 ? string.Empty : arguments[index - 1];

            if (string.Equals(previous, "--file", StringComparison.OrdinalIgnoreCase)
                || string.Equals(previous, "-f", StringComparison.OrdinalIgnoreCase))
            {
                translated.Add(PrepareComposeFileForWsl(argument));
                continue;
            }

            if (string.Equals(previous, "--volume", StringComparison.OrdinalIgnoreCase)
                || string.Equals(previous, "-v", StringComparison.OrdinalIgnoreCase))
            {
                translated.Add(TranslatePotentialPathArgumentForWsl(argument));
                continue;
            }

            translated.Add(argument);
        }

        return translated;
    }

    private string PrepareComposeFileForWsl(string composePath)
    {
        if (!File.Exists(composePath))
        {
            return TranslateWindowsPathToWsl(composePath);
        }

        var content = File.ReadAllText(composePath);
        var translatedContent = TranslateWindowsBindMountSourcesInComposeText(content);

        var workspaceToken = WorkspacePathBuilder.Slugify(Path.GetFileName(Path.GetDirectoryName(composePath) ?? "workspace"));
        var tempRoot = Path.Combine(Path.GetTempPath(), "OpenCode.Workspace.Manager", "wsl-compose", workspaceToken);
        Directory.CreateDirectory(tempRoot);
        var tempComposePath = Path.Combine(tempRoot, "compose.wsl.yaml");
        File.WriteAllText(tempComposePath, translatedContent);

        var envPath = Path.Combine(Path.GetDirectoryName(composePath)!, ".env");
        if (File.Exists(envPath))
        {
            var envContent = File.ReadAllText(envPath);
            File.WriteAllText(Path.Combine(tempRoot, ".env"), TranslateWindowsPathsInText(envContent));
        }

        return TranslateWindowsPathToWsl(tempComposePath);
    }

    private static string TranslateWindowsBindMountSourcesInComposeText(string value)
    {
        var lines = value.Replace("\r\n", "\n").Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            lines[index] = TranslateComposeVolumeLine(lines[index]);
        }

        return string.Join("\n", lines);
    }

    private static string TranslateComposeVolumeLine(string line)
    {
        var listItemMatch = Regex.Match(
            line,
            "^(?<prefix>\\s*-\\s*)(?<quote>[\"']?)(?<source>[A-Za-z]:(?:\\\\|/)[^:\"']*)(?<quoteClose>[\"']?)(?<suffix>:.+)$");
        if (listItemMatch.Success && QuotesMatch(listItemMatch))
        {
            return string.Concat(
                listItemMatch.Groups["prefix"].Value,
                listItemMatch.Groups["quote"].Value,
                TranslateWindowsPathToWsl(listItemMatch.Groups["source"].Value),
                listItemMatch.Groups["quoteClose"].Value,
                listItemMatch.Groups["suffix"].Value);
        }

        var sourceMatch = Regex.Match(
            line,
            "^(?<prefix>\\s*source\\s*:\\s*)(?<quote>[\"']?)(?<source>[A-Za-z]:(?:\\\\|/)[^\"']*)(?<quoteClose>[\"']?)\\s*$",
            RegexOptions.IgnoreCase);
        if (sourceMatch.Success && QuotesMatch(sourceMatch))
        {
            return string.Concat(
                sourceMatch.Groups["prefix"].Value,
                sourceMatch.Groups["quote"].Value,
                TranslateWindowsPathToWsl(sourceMatch.Groups["source"].Value),
                sourceMatch.Groups["quoteClose"].Value);
        }

        return line;
    }

    private static bool QuotesMatch(Match match)
        => string.Equals(match.Groups["quote"].Value, match.Groups["quoteClose"].Value, StringComparison.Ordinal);

    private static string TranslateWindowsPathsInText(string value)
        => Regex.Replace(
            value,
            @"[A-Za-z]:/[A-Za-z0-9_./-]+",
            match => TranslateWindowsPathToWsl(match.Value));

    private static string TranslatePotentialPathArgumentForWsl(string argument)
    {
        var separatorIndex = argument.IndexOf(':');
        if (separatorIndex > 1)
        {
            var left = argument[..separatorIndex];
            var translatedLeft = TranslateWindowsPathToWsl(left);
            if (!string.Equals(left, translatedLeft, StringComparison.Ordinal))
            {
                return translatedLeft + argument[separatorIndex..];
            }
        }

        return TranslateWindowsPathToWsl(argument);
    }

    private static string TranslateWindowsPathToWsl(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Length < 3
            || !char.IsLetter(path[0])
            || path[1] != ':'
            || (path[2] != '\\' && path[2] != '/'))
        {
            return path;
        }

        var drive = char.ToLowerInvariant(path[0]);
        var remainder = path[2..].Replace('\\', '/');
        return $"/mnt/{drive}{remainder}";
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

        var analyticsPortConflictResult = await DetectAnalyticsPortConflictAsync(paths, definition, log, cancellationToken);
        if (analyticsPortConflictResult is not null)
        {
            return analyticsPortConflictResult;
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
        => WorkspaceComposeProfileResolver.GetRuntimeProfiles(definition);

    private async Task<ProcessResult?> DetectOraclePortConflictAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        if (!OracleWorkspaceFamily.IsOracleWorkspace(definition))
        {
            return null;
        }

        var runtimeState = _workspaceRuntimeStateService.Read(paths.RuntimeStatePath);
        var projectName = WorkspacePathBuilder.Slugify(definition.Workspace.Name);
        var ports = new List<OracleHostPortCheck>
        {
            new(WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(definition, runtimeState, WorkspaceRuntimeResourceCatalog.OracleDatabaseResourceId), "Oracle", "another Oracle demo workspace is running", "Oracle Database is already running locally"),
        };

        if (OracleWorkspaceFamily.HasApex(definition))
        {
            ports.Add(new OracleHostPortCheck(WorkspaceRuntimeResourceCatalog.ResolveAllocatedPort(definition, runtimeState, WorkspaceRuntimeResourceCatalog.OracleOrdsResourceId), "Oracle ORDS/APEX", "another Oracle APEX workspace is running", "another service is already using the ORDS/APEX port locally"));
        }

        var dockerPsResult = await RunDockerPsPreflightAsync(paths.RootPath, log, cancellationToken);
        if (!dockerPsResult.IsSuccess)
        {
            return dockerPsResult;
        }

        var composePsResult = await TryGetComposePsForDiagnosticsAsync(paths, definition, log, cancellationToken);

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

    private async Task<ProcessResult?> DetectAnalyticsPortConflictAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        if (!definition.Features.Contains("analytics-reporting", StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var analyticsSettings = AnalyticsWorkspaceSettings.From(definition);
        var projectName = WorkspacePathBuilder.Slugify(definition.Workspace.Name);
        var dockerPsResult = await RunDockerPsPreflightAsync(paths.RootPath, log, cancellationToken);
        if (!dockerPsResult.IsSuccess)
        {
            return dockerPsResult;
        }

        var composePsResult = await TryGetComposePsForDiagnosticsAsync(paths, definition, log, cancellationToken);
        var containerOwner = dockerPsResult.IsSuccess ? FindPortOwningContainer(dockerPsResult.StandardOutputLines, analyticsSettings.MarimoPort, projectName) : null;
        if (containerOwner?.BelongsToCurrentWorkspace == true)
        {
            return null;
        }

        var hostDiagnostic = await GetHostPortDiagnosticAsync(analyticsSettings.MarimoPort, cancellationToken);
        if (containerOwner is null && !hostDiagnostic.IsInUse)
        {
            return null;
        }

        var lines = new List<string>
        {
            $"Marimo port {analyticsSettings.MarimoPort} is already in use.",
            string.Empty,
            "Likely causes:",
            "- another analytics workspace is already running",
            "- another local service is already bound to the Marimo host port",
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

        lines.Add(string.Empty);
        lines.Add("Suggested actions:");
        lines.Add("- Stop the other analytics workspace");
        lines.Add("- Set analytics.marimoPort to a different value");
        lines.Add("- Retry after the port is free");

        var message = string.Join(Environment.NewLine, lines);
        log?.Invoke(new CommandLogEntry { Source = "app", Message = message });

        return new ProcessResult
        {
            Command = $"analytics-port-preflight {analyticsSettings.MarimoPort}",
            ExitCode = 1,
            StandardOutput = string.Empty,
            StandardError = message,
            StandardOutputLines = Array.Empty<string>(),
            StandardErrorLines = message.Split(Environment.NewLine),
            Duration = TimeSpan.Zero,
            FailureClassification = WorkspaceFailureClassification.EnvironmentPortConflict,
        };
    }

    private static string BuildOraclePortConflictMessage(OracleHostPortCheck port, ContainerPortOwner? containerOwner, HostPortDiagnostic hostDiagnostic, ProcessResult dockerPsResult, ProcessResult composePsResult)
    {
        var checkedAt = DateTimeOffset.Now;
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
            lines.Add($"Port {port.Port} currently owned by: {containerOwner.ContainerName}");
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
        lines.Add($"Last checked: {checkedAt:yyyy-MM-dd HH:mm:ss zzz}");
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
