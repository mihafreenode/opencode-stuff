using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Diagnostics;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Cli;

public sealed class CliApplication
{
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly Func<string, CancellationToken, Task<WorkspaceDoctorResult>> _doctorRunner;
    private readonly Func<PlatformValidationRequest, CancellationToken, Task<PlatformValidationReport>> _platformValidationRunner;
    private readonly Func<CancellationToken, Task<WorkspaceLoadReport>> _workspaceDiscoveryRunner;
    private readonly Func<SmokeCleanupOptions, CancellationToken, Task<SmokeCleanupResult>> _smokeCleanupRunner;
    private readonly Func<RuntimeOwnershipQuery, CancellationToken, Task<RuntimeResourceInventory>> _runtimeInventoryRunner;

    public CliApplication(TextWriter output, TextWriter error)
        : this(output, error, null, null)
    {
    }

    public CliApplication(
        TextWriter output,
        TextWriter error,
        Func<string, CancellationToken, Task<WorkspaceDoctorResult>>? doctorRunner,
        Func<PlatformValidationRequest, CancellationToken, Task<PlatformValidationReport>>? platformValidationRunner,
        Func<CancellationToken, Task<WorkspaceLoadReport>>? workspaceDiscoveryRunner = null,
        Func<SmokeCleanupOptions, CancellationToken, Task<SmokeCleanupResult>>? smokeCleanupRunner = null,
        Func<RuntimeOwnershipQuery, CancellationToken, Task<RuntimeResourceInventory>>? runtimeInventoryRunner = null)
    {
        _output = output;
        _error = error;
        _doctorRunner = doctorRunner ?? RunDoctorAsync;
        _platformValidationRunner = platformValidationRunner ?? RunPlatformValidationAsync;
        _workspaceDiscoveryRunner = workspaceDiscoveryRunner ?? RunWorkspaceDiscoveryAsync;
        _smokeCleanupRunner = smokeCleanupRunner ?? RunSmokeCleanupAsync;
        _runtimeInventoryRunner = runtimeInventoryRunner ?? RunRuntimeInventoryAsync;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            await _output.WriteLineAsync(CliOutputFormatter.HelpText());
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "doctor" => await RunDoctorCommandAsync(args[1..], cancellationToken),
                "validate-platform" => await RunValidatePlatformCommandAsync(args[1..], cancellationToken),
                "debug-workspace-discovery" => await RunWorkspaceDiscoveryCommandAsync(cancellationToken),
                "smoke" => await RunSmokeCommandAsync(args[1..], cancellationToken),
                "runtime" => await RunRuntimeCommandAsync(args[1..], cancellationToken),
                _ => await FailWithHelpAsync($"Unknown command '{args[0]}'."),
            };
        }
        catch (ArgumentException exception)
        {
            await _error.WriteLineAsync(exception.Message);
            await _error.WriteLineAsync(CliOutputFormatter.HelpText());
            return 1;
        }
        catch (Exception exception)
        {
            await _error.WriteLineAsync(exception.Message);
            return 1;
        }
    }

    private async Task<int> RunDoctorCommandAsync(string[] args, CancellationToken cancellationToken)
    {
        var workspacePath = ParseWorkspaceOption(args);
        var result = await _doctorRunner(workspacePath, cancellationToken);
        await _output.WriteLineAsync(CliOutputFormatter.FormatDoctor(result));
        return 0;
    }

    private async Task<int> RunValidatePlatformCommandAsync(string[] args, CancellationToken cancellationToken)
    {
        var workspacePath = ParseWorkspaceOption(args);
        var target = ParseRequiredTargetOption(args);
        var outputPath = ParseOutputOption(args);
        var report = await _platformValidationRunner(new PlatformValidationRequest
        {
            WorkspacePath = workspacePath,
            TargetPlatform = target,
        }, cancellationToken);
        await _output.WriteLineAsync(CliOutputFormatter.FormatPlatformValidation(report));

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            await WritePlatformValidationReportAsync(report, outputPath, cancellationToken);
        }

        return report.IsSuccess ? 0 : 1;
    }

    private async Task<int> RunWorkspaceDiscoveryCommandAsync(CancellationToken cancellationToken)
    {
        var report = await _workspaceDiscoveryRunner(cancellationToken);
        await _output.WriteLineAsync(CliOutputFormatter.FormatWorkspaceDiscovery(report));
        return 0;
    }

    private async Task<int> RunSmokeCommandAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || !string.Equals(args[0], "cleanup", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Missing or unsupported smoke subcommand. Use 'smoke cleanup'.");
        }

        var options = new SmokeCleanupOptions(
            DryRun: args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase),
            IncludeAll: args.Contains("--all", StringComparer.OrdinalIgnoreCase) || ParseOptionValue(args, "--run-id") is null,
            RunId: ParseOptionValue(args, "--run-id"),
            OutputFormat: ParseOptionValue(args, "--format") ?? "text");

        var result = await _smokeCleanupRunner(options, cancellationToken);
        await _output.WriteLineAsync(CliOutputFormatter.FormatSmokeCleanup(result, options.OutputFormat));
        return result.Succeeded ? 0 : 1;
    }

    private async Task<int> RunRuntimeCommandAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || (!string.Equals(args[0], "list", StringComparison.OrdinalIgnoreCase) && !string.Equals(args[0], "doctor", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Missing runtime subcommand. Use 'runtime list' or 'runtime doctor'.");
        }

        var query = new RuntimeOwnershipQuery
        {
            OwnerKind = ParseOptionValue(args, "--owner"),
            RunId = ParseOptionValue(args, "--run-id"),
            WorkspaceRoot = ParseOptionValue(args, "--workspace"),
            Project = ParseOptionValue(args, "--project"),
        };
        var format = ParseOptionValue(args, "--format") ?? "text";
        var inventory = await _runtimeInventoryRunner(query, cancellationToken);
        await _output.WriteLineAsync(CliOutputFormatter.FormatRuntimeInventory(inventory, format));
        return 0;
    }

    private async Task<int> FailWithHelpAsync(string message)
    {
        await _error.WriteLineAsync(message);
        await _error.WriteLineAsync(CliOutputFormatter.HelpText());
        return 1;
    }

    private static string ParseWorkspaceOption(string[] args)
        => ParseOptionValue(args, "--workspace") ?? Environment.CurrentDirectory;

    private static string ParseRequiredTargetOption(string[] args)
        => ParseOptionValue(args, "--target") ?? throw new ArgumentException("Missing required option --target.");

    private static string? ParseOutputOption(string[] args)
        => ParseOptionValue(args, "--output");

    private static string? ParseOptionValue(string[] args, string optionName)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase))
            {
                if (argument.StartsWith("--", StringComparison.Ordinal)
                    && !string.Equals(argument, "--workspace", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(argument, "--target", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(argument, "--output", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(argument, "--run-id", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(argument, "--format", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(argument, "--dry-run", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(argument, "--all", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(argument, "--owner", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(argument, "--project", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"Unknown option '{argument}'.");
                }

                continue;
            }

            if (index == args.Length - 1)
            {
                throw new ArgumentException($"Missing value for {optionName}.");
            }

            return args[index + 1];
        }

        return null;
    }

    private static async Task WritePlatformValidationReportAsync(PlatformValidationReport report, string outputPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullOutputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var markdown = PlatformValidationMarkdownReportFormatter.Format(report);
        await File.WriteAllTextAsync(fullOutputPath, markdown, cancellationToken);
    }

    private static async Task<WorkspaceDoctorResult> RunDoctorAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var processRunner = new ProcessRunner();
        var service = new WorkspaceDoctorService(
            new PlatformDetector(processRunner),
            new RuntimeResolver(),
            new WorkspaceDiscoveryService(),
            new WorkspaceYamlService(),
            new WorkspaceRuntimeStateService());
        return await service.DiagnoseAsync(workspacePath, cancellationToken);
    }

    private static async Task<PlatformValidationReport> RunPlatformValidationAsync(PlatformValidationRequest request, CancellationToken cancellationToken)
    {
        var catalogRoot = ResolveCatalogRoot(request.WorkspacePath);
        if (catalogRoot is null)
        {
            return new PlatformValidationReport
            {
                WorkspaceRootPath = Path.GetFullPath(request.WorkspacePath),
                TargetPlatform = request.TargetPlatform,
                Checks =
                [
                    new PlatformValidationCheckResult
                    {
                        Name = "Catalog",
                        Severity = DiagnosticSeverity.Error,
                        Message = "Catalog root was not found. Run from the repository root or a package output that includes catalog/.",
                    },
                ],
                IsSuccess = false,
                HasWarnings = false,
                Summary = $"{request.TargetPlatform} validation failed.",
            };
        }

        var provider = new BuiltInCatalogProvider(catalogRoot);
        var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks());
        var processRunner = new ProcessRunner();
        var service = new PlatformValidationService(
            new WorkspaceDiscoveryService(),
            new WorkspaceYamlService(),
            new PlatformDetector(processRunner),
            new RuntimeResolver(),
            resolver,
            new ComposeGenerator(),
            new ProvisioningScriptGenerator());
        return await service.ValidateAsync(request, cancellationToken);
    }

    private static async Task<WorkspaceLoadReport> RunWorkspaceDiscoveryAsync(CancellationToken cancellationToken)
    {
        var appDataRoot = WorkspaceAppDataPaths.GetWorkspaceManagerDataRoot();
        var catalogRoot = ResolveCatalogRoot(appDataRoot) ?? throw new InvalidOperationException("Catalog root was not found. Run from the repository root or a package output that includes catalog/.");
        var provider = new BuiltInCatalogProvider(catalogRoot);
        var yamlService = new WorkspaceYamlService();
        var repository = new WorkspaceRepository(appDataRoot);
        var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks());
        var processRunner = new ProcessRunner();
        var orchestrator = new WorkspaceOrchestrator(
            yamlService,
            new WorkspaceDiscoveryService(),
            repository,
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
            new WorkspaceIgnorePolicyService(),
            new WorkspaceRuntimeStateService(),
            new GitWorkspaceProvider(processRunner, new WorkspaceIgnorePolicyService()),
            new DockerContainerRuntime(new DockerService(processRunner)),
            new PlatformDetector(processRunner),
            new RuntimeResolver(),
            new NullTerminalLauncher());

        var service = new WorkspaceDiscoveryReportService(orchestrator, repository);
        var result = await service.LoadWorkspaceItemsAsync(includeRuntimeInspection: true, progress: null, cancellationToken);
        return result.Report;
    }

    private static Task<SmokeCleanupResult> RunSmokeCleanupAsync(SmokeCleanupOptions options, CancellationToken cancellationToken)
        => new SmokeRuntimeOwnershipService(new DockerContainerRuntime(new DockerService(new ProcessRunner()))).CleanupAsync(options, cancellationToken);

    private static Task<RuntimeResourceInventory> RunRuntimeInventoryAsync(RuntimeOwnershipQuery query, CancellationToken cancellationToken)
        => new RuntimeOwnershipService(new DockerContainerRuntime(new DockerService(new ProcessRunner()))).BuildInventoryAsync(query, cancellationToken);

    internal static string? ResolveCatalogRoot(string workspacePath)
    {
        foreach (var start in CandidateRoots(workspacePath))
        {
            var current = start;
            while (!string.IsNullOrWhiteSpace(current))
            {
                var candidate = Path.Combine(current, "catalog");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                current = Path.GetDirectoryName(current);
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateRoots(string workspacePath)
    {
        var fullWorkspacePath = Path.GetFullPath(string.IsNullOrWhiteSpace(workspacePath) ? Environment.CurrentDirectory : workspacePath);
        if (File.Exists(fullWorkspacePath))
        {
            var directory = Path.GetDirectoryName(fullWorkspacePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                yield return directory;
            }
        }
        else
        {
            yield return fullWorkspacePath;
        }

        yield return Environment.CurrentDirectory;
        yield return AppContext.BaseDirectory;
    }

    private sealed class NullTerminalLauncher : ITerminalLauncher
    {
        public Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Terminal attach is not available in the CLI workspace discovery command.");
    }
}
