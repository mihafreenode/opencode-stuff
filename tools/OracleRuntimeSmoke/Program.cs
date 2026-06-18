using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

return await OracleRuntimeSmokeCli.RunAsync(args);

public enum SmokeFailureClassification
{
    ValidationToolingFailure,
    EnvironmentFailure,
    ProductFailure,
    OracleRuntimeFailure,
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
        "/ords/apex_admin",
        "/ords/apex_admin/",
        "/ords/apex",
        "/ords/apex/",
        "/ords/r",
        "/ords/r/",
        "/ords/f",
        "/ords/f?p=4550",
    ];

    public static async Task<int> RunAsync(string[] args)
    {
        SmokeRunSummary? summary = null;
        WorkspaceSnapshot? snapshot = null;
        WorkspaceDefinition? definition = null;

        try
        {
            var options = Parse(args);
            var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
            var artifactsRoot = options.ArtifactsRoot ?? Path.Combine(repositoryRoot, "artifacts", "oracle-runtime-smoke", CreateArtifactRunDirectoryName(DateTimeOffset.UtcNow));
            Directory.CreateDirectory(artifactsRoot);

            var wslDocker = ProbeDocker("wsl-current", "docker", "version");
            var windowsDocker = ProbeDockerFromWindows();
            var selectedHost = SelectHost(options, wslDocker, windowsDocker);

            summary = new SmokeRunSummary(options.TemplateId, artifactsRoot)
            {
                WorkspaceRoot = options.WorkspaceRoot,
                SelectedHost = selectedHost.ToString().ToLowerInvariant(),
                WslDockerSuccess = wslDocker.Success,
                WindowsDockerSuccess = windowsDocker.Success,
                SelectionReason = DescribeHostSelection(options, selectedHost, wslDocker, windowsDocker),
                DryRun = options.DryRun,
            };

            File.WriteAllText(Path.Combine(artifactsRoot, "docker-wsl-current.txt"), wslDocker.Output);
            File.WriteAllText(Path.Combine(artifactsRoot, "docker-windows.txt"), windowsDocker.Output);
            WriteSummary(artifactsRoot, summary);

            if (!options.DryRun && selectedHost == SmokeValidationHost.Windows && !OperatingSystem.IsWindows())
            {
                return await DelegateToWindowsAsync(repositoryRoot, options, artifactsRoot);
            }

            var workspaceRoot = options.WorkspaceRoot ?? Path.Combine(Path.GetTempPath(), $"oracle-runtime-smoke-{WorkspacePathBuilder.Slugify(options.TemplateId)}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
            summary = summary with { WorkspaceRoot = workspaceRoot };
            WriteSummary(artifactsRoot, summary);

            var provider = new BuiltInCatalogProvider(Path.Combine(repositoryRoot, "catalog"));
            var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks());
            var template = provider.LoadTemplates().Single(item => string.Equals(item.Id, options.TemplateId, StringComparison.OrdinalIgnoreCase));
            definition = new TemplateExpander().Expand($"{options.TemplateId}-runtime-smoke-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}", template);
            var orchestrator = CreateOrchestrator(workspaceRoot, resolver);
            var oracleSettings = OracleWorkspaceSettings.From(definition);

            summary = summary with
            {
                OrdsHostPort = oracleSettings.OrdsPort,
                OrdsContainerPort = OracleWorkspaceSettings.ContainerOrdsPort,
                OrdsBaseUrlTested = oracleSettings.OrdsBaseUrl,
                ApexUrlTested = oracleSettings.ApexLoginUrl,
            };
            WriteSummary(artifactsRoot, summary);

            var provisioningLog = new StringBuilder();
            void Log(CommandLogEntry entry)
            {
                var line = $"[{entry.Source}] {entry.Message}";
                Console.WriteLine(line);
                provisioningLog.AppendLine(line);
            }

            Console.WriteLine($"[stage] Creating workspace for template '{options.TemplateId}'.");
            snapshot = orchestrator.CreateWorkspace(workspaceRoot, definition, Log);
            CaptureGeneratedArtifacts(snapshot.Paths, artifactsRoot);

            if (options.DryRun)
            {
                summary = summary with { Result = "Dry run completed", FailureClassification = null };
                WriteSummary(artifactsRoot, summary);
                return 0;
            }

            Console.WriteLine("[stage] Provisioning workspace runtime.");
            var started = DateTimeOffset.UtcNow;
            await orchestrator.ProvisionAsync(snapshot, Log);
            var elapsed = DateTimeOffset.UtcNow - started;
            var httpProbe = OracleWorkspaceFamily.HasApex(definition)
                ? await ProbeOracleHostEndpointsAsync(oracleSettings)
                : null;
            summary = summary with { ElapsedSeconds = Math.Round(elapsed.TotalSeconds, 1) };
            if (httpProbe is not null)
            {
                summary = summary with
                {
                    OrdsHttpStatusCode = httpProbe.OrdsStatusCode,
                    ApexHttpStatusCode = httpProbe.ApexStatusCode,
                };
            }

            File.WriteAllText(Path.Combine(artifactsRoot, "provisioning.log"), provisioningLog.ToString());
            await CaptureRuntimeArtifactsAsync(snapshot.Paths, definition, artifactsRoot);

            summary = summary with { Result = "Live smoke completed", FailureClassification = null };
            WriteSummary(artifactsRoot, summary);
            return 0;
        }
        catch (Exception exception)
        {
            OrdsFailureDiagnostic? ordsDiagnostic = null;
            ApexFailureDiagnostic? apexDiagnostic = null;
            if (summary is not null && snapshot is not null && definition is not null)
            {
                ordsDiagnostic = await CaptureOrdsFailureDiagnosticsAsync(snapshot.Paths, definition, summary.ArtifactsRoot);
                apexDiagnostic = await CaptureApexFailureDiagnosticsAsync(snapshot.Paths.RootPath, definition, summary.ArtifactsRoot);
            }

            var oracleSettings = definition is null ? null : OracleWorkspaceSettings.From(definition);
            var httpProbe = oracleSettings is not null && OracleWorkspaceFamily.HasApex(definition!)
                ? await ProbeOracleHostEndpointsAsync(oracleSettings)
                : null;

            var classification = ClassifyFailure(exception);
            Console.Error.WriteLine($"[{classification}] {exception.Message}");
            Console.Error.WriteLine(exception);

            if (ordsDiagnostic is not null)
            {
                Console.Error.WriteLine($"[ords] classification={ordsDiagnostic.FailureClassification}");
                Console.Error.WriteLine($"[ords] restart_count={ordsDiagnostic.RestartCount} exit_code={ordsDiagnostic.ExitCode}");
                if (!string.IsNullOrWhiteSpace(ordsDiagnostic.LastLogLine))
                {
                    Console.Error.WriteLine($"[ords] last_log_line={ordsDiagnostic.LastLogLine}");
                }
            }

            if (summary is not null)
            {
                summary = summary with
                {
                    Result = exception.Message,
                    FailureClassification = classification.ToString(),
                    OrdsFailureClassification = ordsDiagnostic?.FailureClassification,
                    OrdsRestartCount = ordsDiagnostic?.RestartCount,
                    OrdsExitCode = ordsDiagnostic?.ExitCode,
                    OrdsLastLogLine = ordsDiagnostic?.LastLogLine,
                    OrdsHttpStatusCode = httpProbe?.OrdsStatusCode,
                    ApexHttpStatusCode = httpProbe?.ApexStatusCode,
                    ApexMediaFound = apexDiagnostic?.MediaFound,
                    ApexMediaPath = apexDiagnostic?.MediaPath,
                    ApexInstalled = apexDiagnostic?.Installed,
                    ApexVersion = apexDiagnostic?.Version,
                    ApexRegistryStatus = apexDiagnostic?.RegistryStatus,
                    ApexSchemasPresent = apexDiagnostic?.SchemasPresent,
                    ApexInstallationState = apexDiagnostic?.InstallationState,
                };
                WriteSummary(summary.ArtifactsRoot, summary);
                File.WriteAllText(Path.Combine(summary.ArtifactsRoot, "failure.txt"), exception.ToString());
            }

            return 1;
        }
    }

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
            if (File.Exists(Path.Combine(current.FullName, "OpenCode.Workspace.Manager.slnx")))
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
        var workspaceApexCurl = await RunDockerDiagnosticAsync(["exec", workspaceContainerName, "bash", "-lc", $"curl -s -o /dev/null -w '%{{http_code}}' http://oracle-ords:{OracleWorkspaceSettings.ContainerOrdsPort}/ords/apex_admin || true"]);
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

    private sealed class NoOpTerminalLauncher : ITerminalLauncher
    {
        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

public sealed record SmokeRunSummary(string TemplateId, string ArtifactsRoot)
{
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
