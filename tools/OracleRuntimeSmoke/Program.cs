using System.Diagnostics;
using System.Text;
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

    public static async Task<int> RunAsync(string[] args)
    {
        SmokeRunSummary? summary = null;

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
            var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices());
            var template = provider.LoadTemplates().Single(item => string.Equals(item.Id, options.TemplateId, StringComparison.OrdinalIgnoreCase));
            var definition = new TemplateExpander().Expand($"{options.TemplateId}-runtime-smoke", template);
            var orchestrator = CreateOrchestrator(workspaceRoot, resolver);

            var provisioningLog = new StringBuilder();
            void Log(CommandLogEntry entry)
            {
                var line = $"[{entry.Source}] {entry.Message}";
                Console.WriteLine(line);
                provisioningLog.AppendLine(line);
            }

            Console.WriteLine($"[stage] Creating workspace for template '{options.TemplateId}'.");
            var snapshot = orchestrator.CreateWorkspace(workspaceRoot, definition, Log);
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
            summary = summary with { ElapsedSeconds = Math.Round(elapsed.TotalSeconds, 1) };

            File.WriteAllText(Path.Combine(artifactsRoot, "provisioning.log"), provisioningLog.ToString());
            await CaptureRuntimeArtifactsAsync(snapshot.Paths, definition, artifactsRoot);

            summary = summary with { Result = "Live smoke completed", FailureClassification = null };
            WriteSummary(artifactsRoot, summary);
            return 0;
        }
        catch (Exception exception)
        {
            var classification = ClassifyFailure(exception);
            Console.Error.WriteLine($"[{classification}] {exception.Message}");
            Console.Error.WriteLine(exception);

            if (summary is not null)
            {
                summary = summary with { Result = exception.Message, FailureClassification = classification.ToString() };
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
    public string? Result { get; init; }
}
