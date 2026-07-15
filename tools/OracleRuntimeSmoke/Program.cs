using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using OpenCode.Workspace.Core.Workspaces;

return await OracleRuntimeSmokeCli.RunAsync(args);

public enum SmokeFailureClassification
{
    ValidationToolingFailure,
    EnvironmentFailure,
    ProductFailure,
    OracleRuntimeFailure,
    ApexPrerequisiteFailure,
    RuntimeResourceExhaustion,
}

public enum SmokeValidationHost
{
    Auto,
    Current,
    Windows,
}

public sealed record SmokeOptions(
    string TemplateId,
    string? WorkspaceRoot,
    string? ArtifactsRoot,
    SmokeValidationHost Host,
    bool DryRun,
    bool InvokedFromWrapper);

public sealed record DockerProbeResult(string Label, bool Success, string Output);

public static class OracleRuntimeSmokeCli
{
    private const string SmokeOwnerKind = "smoke";
    private static readonly string[] SupportedTemplateIds =
    [
        OracleWorkspaceFamily.OraclePlSqlTemplateId,
        OracleWorkspaceFamily.OracleApexTemplateId,
        OracleWorkspaceFamily.OracleApexLangTemplateId,
    ];

    private static readonly string[] ApexRouteProbeUrls =
    [
        "/ords",
        "/ords/",
        "/ords/apex",
        "/ords/apex/",
        "/ords/r",
        "/ords/r/",
        "/ords/f",
        "/ords/f?p=4550",
    ];

    public static async Task<int> RunAsync(string[] args)
    {
        var options = Parse(args);
        var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        var artifactsRoot = options.ArtifactsRoot ?? Path.Combine(repositoryRoot, "artifacts", "oracle-runtime-smoke", CreateArtifactRunDirectoryName(DateTimeOffset.UtcNow));
        Directory.CreateDirectory(artifactsRoot);

        var wslDocker = ProbeDocker("wsl-current", "docker", "version");
        var windowsDocker = ProbeDockerFromWindows();
        var selectedHost = SelectHost(options, wslDocker, windowsDocker);
        File.WriteAllText(Path.Combine(artifactsRoot, "docker-wsl-current.txt"), wslDocker.Output);
        File.WriteAllText(Path.Combine(artifactsRoot, "docker-windows.txt"), windowsDocker.Output);

        if (!options.DryRun && selectedHost == SmokeValidationHost.Windows && !OperatingSystem.IsWindows())
        {
            return await DelegateToWindowsAsync(repositoryRoot, options, artifactsRoot);
        }

        var catalogRoot = Path.Combine(repositoryRoot, "catalog");
        var provider = new BuiltInCatalogProvider(catalogRoot);
        var template = provider.LoadTemplates().Single(item => string.Equals(item.Id, options.TemplateId, StringComparison.OrdinalIgnoreCase));
        var definition = new TemplateExpander().Expand($"{options.TemplateId}-runtime-smoke-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}", template);
        var oracleSettings = OracleWorkspaceSettings.From(definition);
        var service = new WorkspaceSmokeApplicationService(
            catalogRoot,
            Path.Combine(Path.GetTempPath(), "opencode-workspace-smoke-state"),
            new DockerContainerRuntime(new DockerService(new ProcessRunner())));
        var result = await service.RunAsync(new WorkspaceSmokeSingleRunRequest
        {
            TemplateId = options.TemplateId,
            ArtifactsRoot = artifactsRoot,
            WorkspaceRoot = options.WorkspaceRoot,
            DryRun = options.DryRun,
        });

        CopyCompatibilityArtifacts(result.ArtifactDirectory, artifactsRoot);
        var summary = BuildCompatibilitySummary(options, artifactsRoot, selectedHost, wslDocker, windowsDocker, oracleSettings, result);
        WriteSummary(artifactsRoot, summary);

        if (result.Status != WorkspaceSmokeStatus.Passed)
        {
            await File.WriteAllTextAsync(Path.Combine(artifactsRoot, "failure.txt"), result.FailureMessage, CancellationToken.None);
        }

        await WriteCompatibilityCleanupArtifactsAsync(artifactsRoot, result, CancellationToken.None);
        Console.WriteLine($"[compat] template={result.TemplateId} status={result.Status} run_id={result.RunId}");
        Console.WriteLine($"[compat] artifacts={artifactsRoot}");
        return result.Status == WorkspaceSmokeStatus.Passed ? 0 : 1;
    }

    private static void CopyCompatibilityArtifacts(string sourceDirectory, string destinationDirectory)
    {
        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(filePath, destinationPath, overwrite: true);
        }
    }

    private static SmokeRunSummary BuildCompatibilitySummary(
        SmokeOptions options,
        string artifactsRoot,
        SmokeValidationHost selectedHost,
        DockerProbeResult wslDocker,
        DockerProbeResult windowsDocker,
        OracleWorkspaceSettings oracleSettings,
        WorkspaceSmokeResult result)
    {
        var oracleValidator = result.Validators.FirstOrDefault(item => string.Equals(item.ValidatorId, "oracle-apex-runtime", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.ValidatorId, "oracle-apexlang-runtime", StringComparison.OrdinalIgnoreCase));
        return new SmokeRunSummary(options.TemplateId, artifactsRoot)
        {
            RunId = result.RunId,
            WorkspaceRoot = result.WorkspacePath,
            SelectedHost = selectedHost.ToString().ToLowerInvariant(),
            WslDockerSuccess = wslDocker.Success,
            WindowsDockerSuccess = windowsDocker.Success,
            SelectionReason = DescribeHostSelection(options, selectedHost, wslDocker, windowsDocker),
            DryRun = options.DryRun,
            ElapsedSeconds = Math.Round(result.Duration.TotalSeconds, 1),
            FailureClassification = result.FailureClassification == WorkspaceSmokeFailureClassification.None ? null : MapFailureClassification(result.FailureClassification).ToString(),
            OrdsHostPort = oracleSettings.OrdsPort,
            OrdsContainerPort = OracleWorkspaceSettings.ContainerOrdsPort,
            OrdsBaseUrlTested = oracleSettings.OrdsBaseUrl,
            ApexUrlTested = oracleSettings.ApexLoginUrl,
            OrdsHttpStatusCode = ParseNullableInt(oracleValidator?.Data.GetValueOrDefault("ords_landing_status_code")),
            ApexHttpStatusCode = ParseNullableInt(oracleValidator?.Data.GetValueOrDefault("apex_http_status_code")),
            ApexInstalled = ParseNullableBool(oracleValidator?.Data.GetValueOrDefault("apex_installed")),
            ApexVersion = oracleValidator?.Data.GetValueOrDefault("apex_version"),
            ApexRegistryStatus = oracleValidator?.Data.GetValueOrDefault("apex_registry_status"),
            ApexSchemasPresent = ParseNullableBool(oracleValidator?.Data.GetValueOrDefault("apex_schemas_present")),
            ApexInstallationState = oracleValidator?.Data.GetValueOrDefault("apex_installation_state"),
            CleanupComposeDownAttempted = result.CleanupResult?.ComposeDownAttempted,
            CleanupComposeDownSucceeded = result.CleanupResult?.ComposeDownSucceeded,
            CleanupFallbackRemovalRequired = result.CleanupResult?.FallbackRemovalRequired,
            CleanupVerificationSucceeded = result.CleanupResult?.VerificationSucceeded,
            CleanupWarningCount = result.CleanupResult?.Warnings.Count,
            CleanupErrorCount = result.CleanupResult?.Errors.Count,
            Result = options.DryRun ? "Dry run completed" : result.Status == WorkspaceSmokeStatus.Passed ? "Live smoke completed" : result.FailureMessage,
        };
    }

    private static async Task WriteCompatibilityCleanupArtifactsAsync(string artifactsRoot, WorkspaceSmokeResult result, CancellationToken cancellationToken)
    {
        if (result.CleanupResult is null)
        {
            return;
        }

        var cleanupLines = new List<string>
        {
            $"compose_down_attempted={result.CleanupResult.ComposeDownAttempted}",
            $"compose_down_succeeded={result.CleanupResult.ComposeDownSucceeded}",
            $"fallback_removal_required={result.CleanupResult.FallbackRemovalRequired}",
            $"verification_succeeded={result.CleanupResult.VerificationSucceeded}",
            $"cleanup_succeeded={result.CleanupResult.Succeeded}",
        };
        cleanupLines.AddRange(result.CleanupResult.Actions);
        cleanupLines.AddRange(result.CleanupResult.Warnings.Select(item => "warning:" + item));
        cleanupLines.AddRange(result.CleanupResult.Errors.Select(item => "error:" + item));
        await File.WriteAllTextAsync(Path.Combine(artifactsRoot, "smoke-final-cleanup.txt"), string.Join(Environment.NewLine, cleanupLines), cancellationToken);
    }

    private static SmokeFailureClassification MapFailureClassification(WorkspaceSmokeFailureClassification classification)
        => classification switch
        {
            WorkspaceSmokeFailureClassification.ValidationToolingFailure => SmokeFailureClassification.ValidationToolingFailure,
            WorkspaceSmokeFailureClassification.EnvironmentFailure => SmokeFailureClassification.EnvironmentFailure,
            WorkspaceSmokeFailureClassification.RuntimeResourceExhaustion => SmokeFailureClassification.RuntimeResourceExhaustion,
            WorkspaceSmokeFailureClassification.ApexPrerequisiteFailure => SmokeFailureClassification.ApexPrerequisiteFailure,
            WorkspaceSmokeFailureClassification.OracleRuntimeFailure => SmokeFailureClassification.OracleRuntimeFailure,
            _ => SmokeFailureClassification.ProductFailure,
        };

    private static int? ParseNullableInt(string? value)
        => int.TryParse(value, out var parsed) ? parsed : null;

    private static bool? ParseNullableBool(string? value)
        => bool.TryParse(value, out var parsed) ? parsed : null;

    public static SmokeOptions Parse(string[] args)
    {
        string? templateId = null;
        string? workspaceRoot = null;
        string? artifactsRoot = null;
        var host = SmokeValidationHost.Auto;
        var dryRun = false;
        var invokedFromWrapper = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--template":
                    templateId = GetRequiredValue(args, ++index, "--template");
                    break;
                case "--workspace-root":
                    workspaceRoot = GetRequiredValue(args, ++index, "--workspace-root");
                    break;
                case "--artifacts-root":
                    artifactsRoot = GetRequiredValue(args, ++index, "--artifacts-root");
                    break;
                case "--host":
                    host = ParseHost(GetRequiredValue(args, ++index, "--host"));
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--invoked-from-wrapper":
                    invokedFromWrapper = true;
                    break;
                default:
                    throw new ArgumentException($"Unsupported argument '{args[index]}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new ArgumentException("Missing required argument '--template'.");
        }

        if (!SupportedTemplateIds.Contains(templateId, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsupported template '{templateId}'. Supported templates: {string.Join(", ", SupportedTemplateIds)}.");
        }

        return new SmokeOptions(templateId, workspaceRoot, artifactsRoot, host, dryRun, invokedFromWrapper);
    }

    public static string CreateArtifactRunDirectoryName(DateTimeOffset timestamp)
        => timestamp.ToString("yyyyMMdd-HHmmss");

    private static string GetRequiredValue(string[] args, int index, string option)
        => index >= args.Length
            ? throw new ArgumentException($"Missing value for '{option}'.")
            : args[index];

    private static SmokeValidationHost ParseHost(string value)
        => value.ToLowerInvariant() switch
        {
            "auto" => SmokeValidationHost.Auto,
            "current" => SmokeValidationHost.Current,
            "windows" => SmokeValidationHost.Windows,
            _ => throw new ArgumentException($"Unsupported host '{value}'. Use auto, current, or windows."),
        };

    private static string FindRepositoryRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "OpenCode.Workspace.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Validation tooling failure: could not locate repository root from the current tool path.");
    }

    private static DockerProbeResult ProbeDocker(string label, string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException($"Validation tooling failure: could not start '{fileName} {arguments}'.");

            process.WaitForExit(30000);
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            return new DockerProbeResult(label, process.ExitCode == 0, output.Trim());
        }
        catch (Exception exception)
        {
            return new DockerProbeResult(label, false, exception.Message);
        }
    }

    private static DockerProbeResult ProbeDockerFromWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return ProbeDocker("windows", "docker", "version");
        }

        return ProbeDocker("windows", "powershell.exe", "-NoProfile -Command \"docker version\"");
    }

    private static SmokeValidationHost SelectHost(SmokeOptions options, DockerProbeResult wslDocker, DockerProbeResult windowsDocker)
    {
        if (options.Host != SmokeValidationHost.Auto)
        {
            return options.Host;
        }

        if (windowsDocker.Success)
        {
            return SmokeValidationHost.Windows;
        }

        if (wslDocker.Success)
        {
            return SmokeValidationHost.Current;
        }

        throw new InvalidOperationException("Environment failure: Docker is unavailable from both the current shell and the Windows host.");
    }

    private static string DescribeHostSelection(SmokeOptions options, SmokeValidationHost selectedHost, DockerProbeResult wslDocker, DockerProbeResult windowsDocker)
    {
        if (options.Host != SmokeValidationHost.Auto)
        {
            return $"Host forced to '{selectedHost}'.";
        }

        if (!wslDocker.Success && windowsDocker.Success)
        {
            return "WSL/current-shell Docker failed but Windows Docker Desktop succeeded, so Windows is authoritative for runtime validation.";
        }

        if (windowsDocker.Success)
        {
            return "Windows Docker Desktop succeeded and is the authoritative runtime validation host for this Windows product.";
        }

        return "Using the current shell because Windows Docker was not available but current-shell Docker succeeded.";
    }

    private static WorkspaceOrchestrator CreateOrchestrator(string workspaceRoot, WorkspaceResolver resolver)
    {
        var ignorePolicyService = new WorkspaceIgnorePolicyService();
        return new WorkspaceOrchestrator(
            new WorkspaceYamlService(),
            new WorkspaceDiscoveryService(),
            new WorkspaceRepository(Path.Combine(workspaceRoot, ".appdata")),
            resolver,
            new ComposeGenerator(),
            new EnvironmentFileGenerator(),
            new ProvisioningScriptGenerator(),
            new TerminalArtifactsGenerator(),
            new AttachArtifactsGenerator(),
            new WorkspaceContentGenerator(),
            new WorkspaceAppliedStateService(),
            new WorkspaceCheckpointService(),
            new WorkspaceTimelineService(),
            new WorkspaceSafetyService(),
            ignorePolicyService,
            new GitWorkspaceProvider(new ProcessRunner(), ignorePolicyService),
            new DockerService(new ProcessRunner()),
            new NoOpTerminalLauncher());
    }

    private static void CaptureGeneratedArtifacts(WorkspacePaths paths, string artifactsRoot)
    {
        CopyIfExists(paths.ComposePath, Path.Combine(artifactsRoot, "compose.yaml"));
        CopyIfExists(paths.WorkspaceYamlPath, Path.Combine(artifactsRoot, "workspace.yaml"));

        if (File.Exists(paths.EnvironmentFilePath))
        {
            var envContent = File.ReadAllLines(paths.EnvironmentFilePath)
                .Select(line =>
                {
                    if (line.StartsWith("ORACLE_PASSWORD=", StringComparison.OrdinalIgnoreCase)
                        || line.StartsWith("ORACLE_DEMO_PASSWORD=", StringComparison.OrdinalIgnoreCase))
                    {
                        var separatorIndex = line.IndexOf('=');
                        return separatorIndex >= 0
                            ? line[..(separatorIndex + 1)] + "<redacted>"
                            : line;
                    }

                    return line;
                });
            File.WriteAllLines(Path.Combine(artifactsRoot, "env.redacted"), envContent);
        }
    }

    private static async Task CaptureRuntimeArtifactsAsync(WorkspacePaths paths, WorkspaceDefinition definition, string artifactsRoot)
    {
        var profiles = definition.Services
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .Select(item => $"--profile {item}");
        var profileArgs = string.Join(' ', profiles);
        var composeFile = Quote(paths.ComposePath);

        var psResult = ProbeDocker(
            "docker-ps",
            OperatingSystem.IsWindows() ? "docker" : "powershell.exe",
            OperatingSystem.IsWindows()
                ? $"compose -f {composeFile} {profileArgs} ps"
                : $"-NoProfile -Command \"docker compose -f {composeFile} {profileArgs} ps\"");
        await File.WriteAllTextAsync(Path.Combine(artifactsRoot, "container-status.txt"), psResult.Output);
    }

    private static async Task<OrdsFailureDiagnostic?> CaptureOrdsFailureDiagnosticsAsync(WorkspacePaths paths, WorkspaceDefinition definition, string artifactsRoot)
    {
        if (!OracleWorkspaceFamily.HasApex(definition))
        {
            return null;
        }

        var projectName = WorkspacePathBuilder.Slugify(definition.Workspace.Name);
        var ordsContainerName = $"{projectName}-oracle-ords-1";
        var databaseContainerName = $"{projectName}-oracle-demo-1";
        var workspaceContainerName = $"{projectName}-workspace";
        var oracleSettings = OracleWorkspaceSettings.From(definition);

        var ordsInspect = await RunDockerDiagnosticAsync(["inspect", ordsContainerName]);
        var ordsLogs = await RunDockerDiagnosticAsync(["logs", ordsContainerName]);
        var ordsPs = await RunDockerDiagnosticAsync(["ps", "-a", "--format", "{{.Names}}\t{{.Status}}\t{{.Ports}}" ]);
        var databaseInspect = await RunDockerDiagnosticAsync(["inspect", databaseContainerName]);
        var workspaceCurl = await RunDockerDiagnosticAsync(["exec", workspaceContainerName, "bash", "-lc", $"curl -s -o /dev/null -w '%{{http_code}}' http://oracle-ords:{OracleWorkspaceSettings.ContainerOrdsPort}/ords || true"]);
        var workspaceApexCurl = await RunDockerDiagnosticAsync(["exec", workspaceContainerName, "bash", "-lc", $"curl -s -o /dev/null -w '%{{http_code}}' http://oracle-ords:{OracleWorkspaceSettings.ContainerOrdsPort}/ords/apex || true"]);
        var hostHttpProbe = await ProbeOracleHostEndpointsAsync(oracleSettings);

        var ordsState = DockerContainerRuntimeState.FromInspectJson(ordsInspect.Output);
        var databaseState = DockerContainerRuntimeState.FromInspectJson(databaseInspect.Output);
        var ordsClassification = ClassifyOrdsFailure(ordsLogs.Output, ordsInspect.Output);
        var lastLogLine = GetLastNonEmptyLine(ordsLogs.Output);

        var diagnostics = new StringBuilder();
        diagnostics.AppendLine($"container_name={ordsContainerName}");
        diagnostics.AppendLine($"failure_classification={ordsClassification}");
        diagnostics.AppendLine($"restart_count={ordsState.RestartCount}");
        diagnostics.AppendLine($"exit_code={ordsState.ExitCode}");
        diagnostics.AppendLine($"status={ordsState.Status}");
        diagnostics.AppendLine($"running={ordsState.Running}");
        diagnostics.AppendLine($"image={ordsState.Image}");
        diagnostics.AppendLine($"image_config={ordsState.ImageConfig}");
        diagnostics.AppendLine($"entrypoint={ordsState.Entrypoint}");
        diagnostics.AppendLine($"command={ordsState.Command}");
        diagnostics.AppendLine($"working_dir={ordsState.WorkingDirectory}");
        diagnostics.AppendLine($"published_ports={ordsState.PublishedPorts}");
        diagnostics.AppendLine($"mounts={ordsState.Mounts}");
        diagnostics.AppendLine($"env={ordsState.Environment}");
        diagnostics.AppendLine($"ords_host_port={oracleSettings.OrdsPort}");
        diagnostics.AppendLine($"ords_container_port={OracleWorkspaceSettings.ContainerOrdsPort}");
        diagnostics.AppendLine($"ords_base_url_tested={oracleSettings.OrdsBaseUrl}");
        diagnostics.AppendLine($"apex_url_tested={oracleSettings.ApexLoginUrl}");
        diagnostics.AppendLine($"host_ords_http_status={hostHttpProbe.OrdsStatusCode}");
        diagnostics.AppendLine($"host_apex_http_status={hostHttpProbe.ApexStatusCode}");
        diagnostics.AppendLine($"workspace_ords_http_status={workspaceCurl.Output.Trim()}");
        diagnostics.AppendLine($"workspace_apex_http_status={workspaceApexCurl.Output.Trim()}");
        diagnostics.AppendLine($"db_container_name={databaseContainerName}");
        diagnostics.AppendLine($"db_status={databaseState.Status}");
        diagnostics.AppendLine($"db_health={databaseState.HealthStatus}");
        diagnostics.AppendLine($"db_ip={databaseState.NetworkAddress}");
        diagnostics.AppendLine($"db_env={databaseState.Environment}");
        diagnostics.AppendLine($"db_connection_target=host=oracle-demo;port=1521;service=FREEPDB1");
        diagnostics.AppendLine("docker_ps_output=");
        diagnostics.AppendLine(ordsPs.Output.Trim());
        diagnostics.AppendLine($"last_log_line={lastLogLine}");

        await File.WriteAllTextAsync(Path.Combine(artifactsRoot, "ords-diagnostics.txt"), diagnostics.ToString());

        var combinedLogs = new StringBuilder();
        combinedLogs.AppendLine("== docker compose logs oracle-ords ==");
        combinedLogs.AppendLine("unavailable because the generated compose file uses profiled dependencies that Docker Compose cannot re-evaluate safely after failure");
        combinedLogs.AppendLine();
        combinedLogs.AppendLine("== docker logs oracle-ords ==");
        combinedLogs.AppendLine(ordsLogs.Output.Trim());
        await File.WriteAllTextAsync(Path.Combine(artifactsRoot, "ords-container-logs.txt"), combinedLogs.ToString());

        return new OrdsFailureDiagnostic(ordsClassification, ordsState.RestartCount, ordsState.ExitCode, lastLogLine);
    }

    private static async Task<ApexFailureDiagnostic?> CaptureApexFailureDiagnosticsAsync(string workspaceRoot, WorkspaceDefinition definition, string artifactsRoot)
    {
        if (!OracleWorkspaceFamily.HasApex(definition))
        {
            return null;
        }

        var oracleSettings = OracleWorkspaceSettings.From(definition);
        var routeResults = await ProbeApexRoutesAsync(oracleSettings);
        await File.WriteAllTextAsync(Path.Combine(artifactsRoot, "apex-route-diagnostics.txt"), FormatApexRouteDiagnostics(routeResults));

        var mediaPath = FindApexMediaPath(workspaceRoot);

        var projectName = WorkspacePathBuilder.Slugify(definition.Workspace.Name);
        var databaseContainerName = $"{projectName}-oracle-demo-1";
        var dbStateResult = await RunDockerDiagnosticAsync([
            "exec",
            databaseContainerName,
            "bash",
            "-lc",
            "sqlplus -s 'sys/change-on-first-demo@//localhost:1521/FREEPDB1 as sysdba' <<'SQL'\nset pagesize 200 linesize 200 trimspool on feedback off verify off heading on\nprompt ==REGISTRY==\nselect comp_id, comp_name, version, status from dba_registry where comp_id = 'APEX';\nprompt ==USERS==\nselect username from dba_users where username like 'APEX\\_%' escape '\\' order by username;\nprompt ==INVALID==\nselect owner, object_name, object_type, status from dba_objects where owner like 'APEX\\_%' escape '\\' and status <> 'VALID' fetch first 20 rows only;\nprompt ==VERSION==\nselect version_no from apex_release;\nexit\nSQL"
        ]);
        await File.WriteAllTextAsync(Path.Combine(artifactsRoot, "apex-db-state.txt"), dbStateResult.Output);

        var installationState = ClassifyApexInstallationState(dbStateResult.Output);
        var version = ExtractApexVersion(dbStateResult.Output);
        var registryStatus = ExtractApexRegistryStatus(dbStateResult.Output);
        var installed = string.Equals(installationState, "APEX installed", StringComparison.OrdinalIgnoreCase);
        var schemasPresent = ExtractApexSchemasPresent(dbStateResult.Output);

        return new ApexFailureDiagnostic(mediaPath is not null, mediaPath, installed, version, registryStatus, schemasPresent, installationState);
    }

    public static async Task<IReadOnlyList<RouteProbeResult>> ProbeApexRoutesAsync(OracleWorkspaceSettings oracleSettings)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var results = new List<RouteProbeResult>();

        foreach (var relativeUrl in ApexRouteProbeUrls)
        {
            var absoluteUrl = $"http://localhost:{oracleSettings.OrdsPort}{relativeUrl}";
            try
            {
                using var response = await client.GetAsync(absoluteUrl);
                var body = await response.Content.ReadAsStringAsync();
                var bodyPreview = string.Join(" | ", body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(3));
                results.Add(new RouteProbeResult(relativeUrl, absoluteUrl, (int)response.StatusCode, response.Headers.Location?.ToString(), bodyPreview));
            }
            catch (Exception exception)
            {
                results.Add(new RouteProbeResult(relativeUrl, absoluteUrl, null, null, exception.Message));
            }
        }

        return results;
    }

    public static string FormatApexRouteDiagnostics(IReadOnlyList<RouteProbeResult> routeResults)
    {
        var builder = new StringBuilder();
        foreach (var result in routeResults)
        {
            builder.AppendLine($"URL={result.AbsoluteUrl}");
            builder.AppendLine($"STATUS={(result.StatusCode is null ? "ERROR" : result.StatusCode.Value)}");
            builder.AppendLine($"LOCATION={result.Location}");
            builder.AppendLine($"BODY={result.BodyPreview}");
            builder.AppendLine("---");
        }

        return builder.ToString();
    }

    public static string ClassifyApexInstallationState(string databaseDiagnosticOutput)
    {
        var output = databaseDiagnosticOutput ?? string.Empty;
        if (output.Contains("ORA-00942", StringComparison.OrdinalIgnoreCase)
            && !output.Contains("APEX\n", StringComparison.Ordinal))
        {
            return "APEX not installed";
        }

        if (output.Contains("==REGISTRY==", StringComparison.Ordinal)
            && output.Contains("==USERS==", StringComparison.Ordinal)
            && !output.Contains("APEX\n", StringComparison.Ordinal)
            && !output.Contains("APEX_", StringComparison.Ordinal))
        {
            return "APEX not installed";
        }

        if (output.Contains("APEX", StringComparison.OrdinalIgnoreCase)
            && output.Contains("VALID", StringComparison.OrdinalIgnoreCase))
        {
            return "APEX installed";
        }

        if (output.Contains("INVALID", StringComparison.OrdinalIgnoreCase))
        {
            return "APEX registry invalid";
        }

        return "APEX state unknown";
    }

    private static string? FindApexMediaPath(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return null;
        }

        var downloadsRoot = Path.Combine(workspaceRoot, ".local", "oracle", "downloads");
        if (!Directory.Exists(downloadsRoot))
        {
            return null;
        }

        var candidates = Directory.GetFiles(downloadsRoot, "apex*.zip", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return candidates.FirstOrDefault();
    }

    private static string? ExtractApexVersion(string databaseDiagnosticOutput)
    {
        var lines = (databaseDiagnosticOutput ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var versionMarker = Array.FindIndex(lines, line => string.Equals(line.Trim(), "==VERSION==", StringComparison.Ordinal));
        if (versionMarker < 0)
        {
            return null;
        }

        for (var index = versionMarker + 1; index < lines.Length; index++)
        {
            var value = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(value)
                || value.StartsWith("select ", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("*", StringComparison.Ordinal)
                || value.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("ORA-", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("Help:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return value;
        }

        return null;
    }

    private static string? ExtractApexRegistryStatus(string databaseDiagnosticOutput)
    {
        if (string.IsNullOrWhiteSpace(databaseDiagnosticOutput))
        {
            return null;
        }

        if (databaseDiagnosticOutput.Contains("ORA-00942", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (databaseDiagnosticOutput.Contains("VALID", StringComparison.OrdinalIgnoreCase))
        {
            return "VALID";
        }

        if (databaseDiagnosticOutput.Contains("INVALID", StringComparison.OrdinalIgnoreCase))
        {
            return "INVALID";
        }

        return null;
    }

    private static bool ExtractApexSchemasPresent(string databaseDiagnosticOutput)
        => (databaseDiagnosticOutput ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.TrimStart().StartsWith("APEX_", StringComparison.OrdinalIgnoreCase));

    private static async Task<DockerDiagnosticCommandResult> RunDockerDiagnosticAsync(IReadOnlyList<string> arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "docker" : "docker",
                Arguments = string.Join(' ', arguments.Select(QuoteArgument)),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Failed to start docker diagnostics command.");

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = (await standardOutputTask) + (await standardErrorTask);
            return new DockerDiagnosticCommandResult(process.ExitCode, output.Trim());
        }
        catch (Exception exception)
        {
            return new DockerDiagnosticCommandResult(1, exception.ToString());
        }
    }

    public static string ClassifyOrdsFailure(string containerLogs, string inspectJson)
    {
        var combined = string.Join(Environment.NewLine, [containerLogs ?? string.Empty, inspectJson ?? string.Empty]);

        if (combined.Contains("pull access denied", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("not found", StringComparison.OrdinalIgnoreCase) && combined.Contains("image", StringComparison.OrdinalIgnoreCase))
        {
            return "image pull issue";
        }

        if (combined.Contains("config directory /etc/ords/config is empty", StringComparison.OrdinalIgnoreCase))
        {
            return "ORDS configuration volume issue";
        }

        if (combined.Contains("can't find a valid configuration", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("CONN_STRING and ORACLE_PWD", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("DBHOST, DBPORT, DBSERVICENAME, and ORACLE_PWD", StringComparison.OrdinalIgnoreCase))
        {
            return "configuration issue";
        }

        if (combined.Contains("authentication", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("invalid username/password", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("ORA-01017", StringComparison.OrdinalIgnoreCase))
        {
            return "authentication issue";
        }

        if (combined.Contains("ORA-12514", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("ORA-12541", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("no listener", StringComparison.OrdinalIgnoreCase))
        {
            return "database connectivity issue";
        }

        if (combined.Contains("liquibase", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("initializ", StringComparison.OrdinalIgnoreCase))
        {
            return "ORDS initialization issue";
        }

        if (combined.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("incompatible", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("version", StringComparison.OrdinalIgnoreCase) && combined.Contains("Oracle", StringComparison.OrdinalIgnoreCase))
        {
            return "Oracle version compatibility issue";
        }

        if (combined.Contains("exitcode", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("restartcount", StringComparison.OrdinalIgnoreCase))
        {
            return "container crash";
        }

        return "startup timeout";
    }

    private static async Task<OracleHttpProbeResult> ProbeOracleHostEndpointsAsync(OracleWorkspaceSettings oracleSettings)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        return new OracleHttpProbeResult(
            await GetStatusCodeAsync(client, oracleSettings.OrdsBaseUrl),
            await GetStatusCodeAsync(client, oracleSettings.ApexLoginUrl));
    }

    private static async Task<int?> GetStatusCodeAsync(HttpClient client, string url)
    {
        try
        {
            using var response = await client.GetAsync(url);
            return (int)response.StatusCode;
        }
        catch
        {
            return null;
        }
    }

    private static string GetLastNonEmptyLine(string? content)
        => string.IsNullOrWhiteSpace(content)
            ? string.Empty
            : content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? string.Empty;

    private static async Task<int> DelegateToWindowsAsync(string repositoryRoot, SmokeOptions options, string artifactsRoot)
    {
        var wrapperPath = Path.Combine(repositoryRoot, "scripts", "testing", "oracle-runtime-smoke.ps1");
        if (!File.Exists(wrapperPath))
        {
            throw new InvalidOperationException("Validation tooling failure: Windows smoke wrapper script is missing.");
        }

        var arguments = new StringBuilder()
            .Append("-NoProfile -ExecutionPolicy Bypass -File ")
            .Append(Quote(wrapperPath))
            .Append(" -Template ")
            .Append(Quote(options.TemplateId))
            .Append(" -ArtifactsRoot ")
            .Append(Quote(artifactsRoot))
            .Append(" -Host windows");

        if (!string.IsNullOrWhiteSpace(options.WorkspaceRoot))
        {
            arguments.Append(" -WorkspaceRoot ").Append(Quote(options.WorkspaceRoot!));
        }

        if (options.DryRun)
        {
            arguments.Append(" -DryRun");
        }

        if (options.InvokedFromWrapper)
        {
            throw new InvalidOperationException("Validation tooling failure: Windows delegation loop detected.");
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments.ToString(),
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Validation tooling failure: could not launch Windows PowerShell delegation.");

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static SmokeFailureClassification ClassifyFailure(Exception exception)
    {
        var message = exception.ToString();

        if (message.Contains("Cannot allocate memory", StringComparison.OrdinalIgnoreCase)
            || message.Contains("OutOfMemoryError", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unable to create native thread", StringComparison.OrdinalIgnoreCase)
            || message.Contains("cannot fork", StringComparison.OrdinalIgnoreCase)
            || message.Contains("no space left on device", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Runtime resource exhaustion", StringComparison.OrdinalIgnoreCase))
        {
            return SmokeFailureClassification.RuntimeResourceExhaustion;
        }

        if (message.Contains("Validation tooling failure", StringComparison.OrdinalIgnoreCase)
            || exception is ArgumentException)
        {
            return SmokeFailureClassification.ValidationToolingFailure;
        }

        if (message.Contains("oracle-ords", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Service 'oracle-ords'", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ORDS", StringComparison.OrdinalIgnoreCase))
        {
            return SmokeFailureClassification.OracleRuntimeFailure;
        }

        if (message.Contains("APEX installation media missing", StringComparison.OrdinalIgnoreCase)
            || message.Contains("APEX not installed in database", StringComparison.OrdinalIgnoreCase)
            || message.Contains("APEX registry invalid", StringComparison.OrdinalIgnoreCase)
            || message.Contains("APEX login route not reachable", StringComparison.OrdinalIgnoreCase))
        {
            return SmokeFailureClassification.OracleRuntimeFailure;
        }

        if (message.Contains("Oracle APEX prerequisite validation failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Oracle APEX installation requires the Oracle XML Database database component", StringComparison.OrdinalIgnoreCase))
        {
            return SmokeFailureClassification.ApexPrerequisiteFailure;
        }

        if (message.Contains("docker", StringComparison.OrdinalIgnoreCase)
            || message.Contains("network", StringComparison.OrdinalIgnoreCase)
            || message.Contains("port is already allocated", StringComparison.OrdinalIgnoreCase)
            || message.Contains("daemon", StringComparison.OrdinalIgnoreCase))
        {
            return SmokeFailureClassification.EnvironmentFailure;
        }

        if (message.Contains("ORDS", StringComparison.OrdinalIgnoreCase)
            || message.Contains("APEX", StringComparison.OrdinalIgnoreCase)
            || message.Contains("SQLcl", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Oracle", StringComparison.OrdinalIgnoreCase))
        {
            return SmokeFailureClassification.OracleRuntimeFailure;
        }

        return SmokeFailureClassification.ProductFailure;
    }

    private static void WriteSummary(string artifactsRoot, SmokeRunSummary summary)
    {
        var lines = new[]
        {
            $"template={summary.TemplateId}",
            $"run_id={summary.RunId}",
            $"artifacts_root={summary.ArtifactsRoot}",
            $"workspace_root={summary.WorkspaceRoot}",
            $"selected_host={summary.SelectedHost}",
            $"wsl_docker_success={summary.WslDockerSuccess}",
            $"windows_docker_success={summary.WindowsDockerSuccess}",
            $"selection_reason={summary.SelectionReason}",
            $"dry_run={summary.DryRun}",
            $"elapsed_seconds={summary.ElapsedSeconds}",
            $"failure_classification={summary.FailureClassification}",
            $"ords_failure_classification={summary.OrdsFailureClassification}",
            $"ords_restart_count={summary.OrdsRestartCount}",
            $"ords_exit_code={summary.OrdsExitCode}",
            $"ords_last_log_line={summary.OrdsLastLogLine}",
            $"ords_host_port={summary.OrdsHostPort}",
            $"ords_container_port={summary.OrdsContainerPort}",
            $"ords_base_url_tested={summary.OrdsBaseUrlTested}",
            $"apex_url_tested={summary.ApexUrlTested}",
            $"ords_http_status_code={summary.OrdsHttpStatusCode}",
            $"apex_http_status_code={summary.ApexHttpStatusCode}",
            $"apex_media_found={summary.ApexMediaFound}",
            $"apex_media_path={summary.ApexMediaPath}",
            $"apex_installed={summary.ApexInstalled}",
            $"apex_version={summary.ApexVersion}",
            $"apex_registry_status={summary.ApexRegistryStatus}",
            $"apex_schemas_present={summary.ApexSchemasPresent}",
            $"apex_installation_state={summary.ApexInstallationState}",
            $"result={summary.Result}",
        };

        File.WriteAllLines(Path.Combine(artifactsRoot, "summary.txt"), lines);
    }

    private static void CopyIfExists(string sourcePath, string destinationPath)
    {
        if (File.Exists(sourcePath))
        {
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private static string Quote(string path) => OperatingSystem.IsWindows() ? $"\"{path}\"" : $"'{path}'";

    private static string QuoteArgument(string argument)
        => argument.Contains(' ') || argument.Contains('"')
            ? OperatingSystem.IsWindows()
                ? $"\"{argument.Replace("\"", "\\\"") }\""
                : $"'{argument.Replace("'", "'\\''")}'"
            : argument;

    private static async Task WriteRuntimeInventoryArtifactsAsync(string artifactsRoot, string suffix, RuntimeResourceInventory inventory, CancellationToken cancellationToken)
    {
        var jsonPath = Path.Combine(artifactsRoot, $"runtime-inventory-{suffix}.json");
        var textPath = Path.Combine(artifactsRoot, $"runtime-inventory-{suffix}.txt");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(inventory, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        await File.WriteAllTextAsync(textPath, FormatRuntimeInventorySummary(inventory), cancellationToken);
    }

    private static string FormatRuntimeInventorySummary(RuntimeResourceInventory inventory)
    {
        var containers = inventory.Resources.Count(item => item.Type == RuntimeResourceType.Container);
        var networks = inventory.Resources.Count(item => item.Type == RuntimeResourceType.Network);
        var volumes = inventory.Resources.Count(item => item.Type == RuntimeResourceType.Volume);
        var lines = new List<string>
        {
            "Runtime Inventory",
            "-----------------",
            $"Containers: {containers}",
            $"Networks: {networks}",
            $"Volumes: {volumes}",
            $"Projects: {inventory.Projects.Count}",
            string.Empty,
            "Owned resources:",
        };

        if (inventory.Projects.Count == 0)
        {
            lines.Add("- none");
        }
        else
        {
            foreach (var project in inventory.Projects)
            {
                lines.Add($"- {project.OwnerKind} run {project.RunId}");
                lines.Add($"  project {project.Project}");
                lines.Add($"  containers: {project.Resources.Count(item => item.Type == RuntimeResourceType.Container)}");
                lines.Add($"  volumes: {project.Resources.Count(item => item.Type == RuntimeResourceType.Volume)}");
                lines.Add($"  networks: {project.Resources.Count(item => item.Type == RuntimeResourceType.Network)}");
            }
        }

        var warnings = inventory.Orphans.Concat(inventory.StaleRuntimes).Concat(inventory.DuplicateRunIds).Concat(inventory.MissingRequiredLabels).Concat(inventory.MissingComposeFiles).Concat(inventory.MissingWorkspaceDirectories).ToArray();
        lines.Add(string.Empty);
        lines.Add("Warnings:");
        if (warnings.Length == 0)
        {
            lines.Add("- none");
        }
        else
        {
            lines.AddRange(warnings.Select(item => $"- {item.Message}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static IDisposable AcquireOracleSmokeLock()
    {
        var lockPath = Path.Combine(Path.GetTempPath(), "opencode-oracle-smoke.lock");
        try
        {
            var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new SmokeLockHandle(stream);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("Runtime resource exhaustion: another Oracle smoke run already owns the host-wide smoke lock.", exception);
        }
    }

    private static void ApplySmokeOwnershipLabels(string composePath, WorkspaceDefinition definition, string templateId, string runId, string workspaceRoot)
    {
        var project = WorkspacePathBuilder.Slugify(definition.Workspace.Name);
        var labels = new[]
        {
            $"{RuntimeOwnershipLabels.Owner}: \"{SmokeOwnerKind}\"",
            $"{RuntimeOwnershipLabels.RunId}: \"{runId}\"",
            $"{RuntimeOwnershipLabels.Template}: \"{templateId}\"",
            $"{RuntimeOwnershipLabels.CreatedBy}: \"{RuntimeOwnershipLabels.CreatedByValue}\"",
            $"{RuntimeOwnershipLabels.Project}: \"{project}\"",
            $"{RuntimeOwnershipLabels.WorkspaceRoot}: \"{workspaceRoot.Replace("\\", "/", StringComparison.Ordinal)}\"",
            $"{RuntimeOwnershipLabels.ComposePath}: \"{composePath.Replace("\\", "/", StringComparison.Ordinal)}\"",
            $"{RuntimeOwnershipLabels.CreatedAt}: \"{DateTimeOffset.UtcNow:O}\"",
        };

        var lines = File.ReadAllLines(composePath).ToList();
        InsertLabels(lines, "services:", labels, ensureDefaultChild: false);
        InsertLabels(lines, "networks:", labels, ensureDefaultChild: true);
        InsertLabels(lines, "volumes:", labels, ensureDefaultChild: false);
        File.WriteAllLines(composePath, lines);
    }

    private static void InsertLabels(List<string> lines, string sectionHeader, IReadOnlyList<string> labels, bool ensureDefaultChild)
    {
        var sectionIndex = lines.FindIndex(line => string.Equals(line, sectionHeader, StringComparison.Ordinal));
        if (sectionIndex < 0)
        {
            if (!ensureDefaultChild)
            {
                return;
            }

            lines.Add(sectionHeader);
            lines.Add("  default:");
            sectionIndex = lines.Count - 2;
        }

        if (ensureDefaultChild && !lines.Skip(sectionIndex + 1).TakeWhile(line => line.StartsWith("  ", StringComparison.Ordinal)).Any(line => string.Equals(line, "  default:", StringComparison.Ordinal)))
        {
            lines.Insert(sectionIndex + 1, "  default:");
        }

        for (var index = sectionIndex + 1; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!line.StartsWith("  ", StringComparison.Ordinal))
            {
                break;
            }

            if (!line.EndsWith(":", StringComparison.Ordinal) || line.StartsWith("    ", StringComparison.Ordinal))
            {
                continue;
            }

            if (index + 1 < lines.Count && lines[index + 1].TrimStart().StartsWith("labels:", StringComparison.Ordinal))
            {
                continue;
            }

            lines.Insert(index + 1, "    labels:");
            for (var labelIndex = 0; labelIndex < labels.Count; labelIndex++)
            {
                lines.Insert(index + 2 + labelIndex, "      " + labels[labelIndex]);
            }

            index += labels.Count + 1;
        }
    }

    private sealed class SmokeLockHandle : IDisposable
    {
        private readonly FileStream _stream;

        public SmokeLockHandle(FileStream stream)
        {
            _stream = stream;
        }

        public void Dispose()
        {
            _stream.Dispose();
        }
    }

    private sealed class NoOpTerminalLauncher : ITerminalLauncher
    {
        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

public sealed record SmokeRunSummary(string TemplateId, string ArtifactsRoot)
{
    public string? RunId { get; init; }
    public string? WorkspaceRoot { get; init; }
    public string? SelectedHost { get; init; }
    public bool WslDockerSuccess { get; init; }
    public bool WindowsDockerSuccess { get; init; }
    public string? SelectionReason { get; init; }
    public bool DryRun { get; init; }
    public double? ElapsedSeconds { get; init; }
    public string? FailureClassification { get; init; }
    public string? OrdsFailureClassification { get; init; }
    public int? OrdsRestartCount { get; init; }
    public int? OrdsExitCode { get; init; }
    public string? OrdsLastLogLine { get; init; }
    public int? OrdsHostPort { get; init; }
    public int? OrdsContainerPort { get; init; }
    public string? OrdsBaseUrlTested { get; init; }
    public string? ApexUrlTested { get; init; }
    public int? OrdsHttpStatusCode { get; init; }
    public int? ApexHttpStatusCode { get; init; }
    public bool? ApexMediaFound { get; init; }
    public string? ApexMediaPath { get; init; }
    public bool? ApexInstalled { get; init; }
    public string? ApexVersion { get; init; }
    public string? ApexRegistryStatus { get; init; }
    public bool? ApexSchemasPresent { get; init; }
    public string? ApexInstallationState { get; init; }
    public bool? CleanupComposeDownAttempted { get; init; }
    public bool? CleanupComposeDownSucceeded { get; init; }
    public bool? CleanupFallbackRemovalRequired { get; init; }
    public bool? CleanupVerificationSucceeded { get; init; }
    public int? CleanupWarningCount { get; init; }
    public int? CleanupErrorCount { get; init; }
    public string? Result { get; init; }
}

public sealed record OrdsFailureDiagnostic(string FailureClassification, int RestartCount, int ExitCode, string LastLogLine);

public sealed record DockerDiagnosticCommandResult(int ExitCode, string Output);

public sealed record OracleHttpProbeResult(int? OrdsStatusCode, int? ApexStatusCode);

public sealed record RouteProbeResult(string RelativeUrl, string AbsoluteUrl, int? StatusCode, string? Location, string BodyPreview);

public sealed record ApexFailureDiagnostic(bool MediaFound, string? MediaPath, bool Installed, string? Version, string? RegistryStatus, bool SchemasPresent, string InstallationState);

public sealed record DockerContainerRuntimeState(
    string Status,
    bool Running,
    int RestartCount,
    int ExitCode,
    string HealthStatus,
    string Image,
    string ImageConfig,
    string Entrypoint,
    string Command,
    string WorkingDirectory,
    string Environment,
    string Mounts,
    string PublishedPorts,
    string NetworkAddress)
{
    public static DockerContainerRuntimeState FromInspectJson(string inspectJson)
    {
        if (string.IsNullOrWhiteSpace(inspectJson))
        {
            return Empty();
        }

        try
        {
            using var document = JsonDocument.Parse(inspectJson);
            var root = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement[0] : document.RootElement;
            var state = root.GetProperty("State");
            var config = root.GetProperty("Config");
            var mounts = root.TryGetProperty("Mounts", out var mountsElement) && mountsElement.ValueKind == JsonValueKind.Array
                ? string.Join("; ", mountsElement.EnumerateArray().Select(mount => $"{mount.GetProperty("Source").GetString()}->{mount.GetProperty("Destination").GetString()}"))
                : string.Empty;
            var env = config.TryGetProperty("Env", out var envElement) && envElement.ValueKind == JsonValueKind.Array
                ? string.Join("; ", envElement.EnumerateArray().Select(item => item.GetString()))
                : string.Empty;
            var entrypoint = config.TryGetProperty("Entrypoint", out var entrypointElement) && entrypointElement.ValueKind == JsonValueKind.Array
                ? string.Join(' ', entrypointElement.EnumerateArray().Select(item => item.GetString()))
                : string.Empty;
            var command = config.TryGetProperty("Cmd", out var cmdElement) && cmdElement.ValueKind == JsonValueKind.Array
                ? string.Join(' ', cmdElement.EnumerateArray().Select(item => item.GetString()))
                : string.Empty;
            var ports = root.TryGetProperty("HostConfig", out var hostConfig)
                && hostConfig.TryGetProperty("PortBindings", out var portBindings)
                && portBindings.ValueKind == JsonValueKind.Object
                    ? string.Join("; ", portBindings.EnumerateObject().Select(binding =>
                    {
                        var published = binding.Value.ValueKind == JsonValueKind.Array
                            ? string.Join(',', binding.Value.EnumerateArray().Select(item => item.GetProperty("HostPort").GetString()))
                            : string.Empty;
                        return $"{binding.Name}=>{published}";
                    }))
                    : string.Empty;
            var networkAddress = root.TryGetProperty("NetworkSettings", out var networkSettings)
                && networkSettings.TryGetProperty("Networks", out var networks)
                && networks.ValueKind == JsonValueKind.Object
                    ? networks.EnumerateObject().Select(item => item.Value.TryGetProperty("IPAddress", out var ip) ? ip.GetString() : string.Empty).FirstOrDefault(ip => !string.IsNullOrWhiteSpace(ip)) ?? string.Empty
                    : string.Empty;

            return new DockerContainerRuntimeState(
                state.TryGetProperty("Status", out var statusElement) ? statusElement.GetString() ?? string.Empty : string.Empty,
                state.TryGetProperty("Running", out var runningElement) && runningElement.GetBoolean(),
                root.TryGetProperty("RestartCount", out var restartCountElement) ? restartCountElement.GetInt32() : 0,
                state.TryGetProperty("ExitCode", out var exitCodeElement) ? exitCodeElement.GetInt32() : 0,
                state.TryGetProperty("Health", out var healthElement) && healthElement.TryGetProperty("Status", out var healthStatusElement) ? healthStatusElement.GetString() ?? string.Empty : string.Empty,
                root.TryGetProperty("Image", out var imageElement) ? imageElement.GetString() ?? string.Empty : string.Empty,
                config.TryGetProperty("Image", out var imageConfigElement) ? imageConfigElement.GetString() ?? string.Empty : string.Empty,
                entrypoint,
                command,
                config.TryGetProperty("WorkingDir", out var workingDirElement) ? workingDirElement.GetString() ?? string.Empty : string.Empty,
                env,
                mounts,
                ports,
                networkAddress);
        }
        catch
        {
            return Empty();
        }
    }

    private static DockerContainerRuntimeState Empty()
        => new(string.Empty, false, 0, 0, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
}
