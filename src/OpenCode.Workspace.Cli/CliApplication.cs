using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Diagnostics;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
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
    private readonly Func<LegacyCleanupOptions, CancellationToken, Task<LegacyCleanupResult>> _legacySmokeCleanupRunner;
    private readonly Func<RuntimeOwnershipQuery, CancellationToken, Task<RuntimeResourceInventory>> _runtimeInventoryRunner;
    private readonly Func<WorkspaceSmokeDefinitionQuery, CancellationToken, Task<WorkspaceSmokeDefinitionCatalogResult>> _smokeDefinitionRunner;
    private readonly Func<WorkspaceSmokeSingleRunRequest, CancellationToken, Task<WorkspaceSmokeResult>> _smokeRunRunner;
    private readonly Func<WorkspaceSmokeMatrixRunRequest, CancellationToken, Task<WorkspaceSmokeMatrixResult>> _smokeMatrixRunner;

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
        Func<LegacyCleanupOptions, CancellationToken, Task<LegacyCleanupResult>>? legacySmokeCleanupRunner = null,
        Func<RuntimeOwnershipQuery, CancellationToken, Task<RuntimeResourceInventory>>? runtimeInventoryRunner = null,
        Func<WorkspaceSmokeDefinitionQuery, CancellationToken, Task<WorkspaceSmokeDefinitionCatalogResult>>? smokeDefinitionRunner = null,
        Func<WorkspaceSmokeSingleRunRequest, CancellationToken, Task<WorkspaceSmokeResult>>? smokeRunRunner = null,
        Func<WorkspaceSmokeMatrixRunRequest, CancellationToken, Task<WorkspaceSmokeMatrixResult>>? smokeMatrixRunner = null)
    {
        _output = output;
        _error = error;
        _doctorRunner = doctorRunner ?? RunDoctorAsync;
        _platformValidationRunner = platformValidationRunner ?? RunPlatformValidationAsync;
        _workspaceDiscoveryRunner = workspaceDiscoveryRunner ?? RunWorkspaceDiscoveryAsync;
        _smokeCleanupRunner = smokeCleanupRunner ?? RunSmokeCleanupAsync;
        _legacySmokeCleanupRunner = legacySmokeCleanupRunner ?? RunLegacySmokeCleanupAsync;
        _runtimeInventoryRunner = runtimeInventoryRunner ?? RunRuntimeInventoryAsync;
        _smokeDefinitionRunner = smokeDefinitionRunner ?? RunSmokeDefinitionsAsync;
        _smokeRunRunner = smokeRunRunner ?? RunSmokeTemplateAsync;
        _smokeMatrixRunner = smokeMatrixRunner ?? RunSmokeMatrixAsync;
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
                "interactive-session" => await RunInteractiveSessionCommandAsync(args[1..], cancellationToken),
                "mcp" => await McpCliCommands.RunAsync(args[1..], _output, cancellationToken),
                "smoke" => await RunSmokeCommandAsync(args[1..], cancellationToken),
                "runtime" => await RunRuntimeCommandAsync(args[1..], cancellationToken),
                _ => await FailWithHelpAsync($"Unknown command '{args[0]}'."),
            };
        }
        catch (OperationCanceledException)
        {
            await _error.WriteLineAsync("Command cancelled.");
            return 130;
        }
        catch (WorkspaceSmokeSelectionException exception)
        {
            await _error.WriteLineAsync(exception.Message);
            return 6;
        }
        catch (ArgumentException exception)
        {
            await _error.WriteLineAsync(exception.Message);
            await _error.WriteLineAsync(CliOutputFormatter.HelpText());
            return 2;
        }
        catch (Exception exception)
        {
            await _error.WriteLineAsync(exception.Message);
            return 7;
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
        if (args.Length == 0)
        {
            throw new ArgumentException("Missing or unsupported smoke subcommand. Use 'smoke list', 'smoke run', or 'smoke cleanup'.");
        }

        if (string.Equals(args[0], "list", StringComparison.OrdinalIgnoreCase))
        {
            var family = ParseOptionValue(args, "--family");
            var format = ParseFormat(args);
            var catalog = await _smokeDefinitionRunner(new WorkspaceSmokeDefinitionQuery { Family = family }, cancellationToken);
            await _output.WriteLineAsync(CliOutputFormatter.FormatSmokeDefinitions(catalog, format));
            return 0;
        }

        if (string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase))
        {
            var format = ParseFormat(args);
            var verbosity = ParseVerbosity(args);
            var keepWorkspace = args.Contains("--keep-workspace", StringComparer.OrdinalIgnoreCase);
            var keepRuntimeOnFailure = args.Contains("--keep-runtime-on-failure", StringComparer.OrdinalIgnoreCase);
            var artifactsRoot = ParseOptionValue(args, "--artifacts-root") ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "template-smoke");
            var timeout = ParseTimeoutOption(args);
            var selection = ParseSmokeRunSelection(args);
            var definitions = (await _smokeDefinitionRunner(new WorkspaceSmokeDefinitionQuery { Family = selection.Family }, cancellationToken)).Definitions;

            if (selection.Mode != SmokeRunSelectionMode.SingleTemplate)
            {
                var selectedDefinitions = selection.Mode == SmokeRunSelectionMode.All
                    ? definitions.OrderBy(item => item.TemplateId, StringComparer.OrdinalIgnoreCase).ToArray()
                    : definitions.Where(item => string.Equals(item.Family, selection.Family, StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.TemplateId, StringComparer.OrdinalIgnoreCase).ToArray();
                if (selectedDefinitions.Length == 0)
                {
                    throw new WorkspaceSmokeSelectionException(string.IsNullOrWhiteSpace(selection.Family)
                        ? "Smoke selection is empty."
                        : $"Unknown smoke family '{selection.Family}'.");
                }

                var matrixResult = await _smokeMatrixRunner(new WorkspaceSmokeMatrixRunRequest
                {
                    TemplateIds = selectedDefinitions.Select(item => item.TemplateId).ToArray(),
                    ArtifactsRoot = artifactsRoot,
                    ParallelCount = int.TryParse(ParseOptionValue(args, "--parallel"), out var parallel) ? parallel : 1,
                    KeepWorkspace = keepWorkspace,
                    KeepRuntimeOnFailure = keepRuntimeOnFailure,
                    MatrixTimeout = timeout,
                }, cancellationToken);
                await _output.WriteLineAsync(CliOutputFormatter.FormatSmokeMatrixResult(matrixResult, format, verbosity));
                return MapOutcomeToExitCode(WorkspaceSmokeAutomationOutcomeClassifier.Classify(matrixResult));
            }

            var templateId = selection.TemplateId ?? throw new ArgumentException("Missing smoke template id. Use 'opencode smoke run <template>', '--family', or '--all'.");
            var singleResult = await _smokeRunRunner(new WorkspaceSmokeSingleRunRequest
            {
                TemplateId = templateId,
                ArtifactsRoot = artifactsRoot,
                KeepWorkspace = keepWorkspace,
                KeepRuntimeOnFailure = keepRuntimeOnFailure,
                Timeout = timeout,
            }, cancellationToken);
            await _output.WriteLineAsync(CliOutputFormatter.FormatSmokeResult(singleResult, format, verbosity));
            return MapOutcomeToExitCode(WorkspaceSmokeAutomationOutcomeClassifier.Classify(singleResult));
        }

        if (!string.Equals(args[0], "cleanup", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Missing or unsupported smoke subcommand. Use 'smoke list', 'smoke run', or 'smoke cleanup'.");
        }

        var outputFormat = ParseFormat(args);
        var outputVerbosity = ParseVerbosity(args);
        var options = new global::OpenCode.Workspace.Core.Runtime.SmokeCleanupOptions(
            DryRun: args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase),
            IncludeAll: args.Contains("--all", StringComparer.OrdinalIgnoreCase) || ParseOptionValue(args, "--run-id") is null,
            RunId: ParseOptionValue(args, "--run-id"),
            OutputFormat: outputFormat);

        if (args.Contains("--legacy", StringComparer.OrdinalIgnoreCase))
        {
            var legacyFormat = outputFormat;
            var legacyResult = await _legacySmokeCleanupRunner(new LegacyCleanupOptions
            {
                DryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase),
                OutputFormat = legacyFormat,
            }, cancellationToken);
            await _output.WriteLineAsync(CliOutputFormatter.FormatLegacySmokeCleanup(legacyResult, legacyFormat));
            return legacyResult.Succeeded ? 0 : 1;
        }

        var result = await _smokeCleanupRunner(options, cancellationToken);
        await _output.WriteLineAsync(CliOutputFormatter.FormatSmokeCleanup(result, options.OutputFormat, outputVerbosity));
        return MapOutcomeToExitCode(WorkspaceSmokeAutomationOutcomeClassifier.Classify(result));
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
        var format = ParseFormat(args);
        var verbosity = ParseVerbosity(args);
        var inventory = await _runtimeInventoryRunner(query, cancellationToken);
        await _output.WriteLineAsync(CliOutputFormatter.FormatRuntimeInventory(inventory, format, string.Equals(args[0], "doctor", StringComparison.OrdinalIgnoreCase), verbosity));
        return 0;
    }

    private async Task<int> RunInteractiveSessionCommandAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || !string.Equals(args[0], "attach", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Missing interactive-session subcommand. Use 'interactive-session attach'.");
        }

        return await new InteractiveSessionAttachHelper(_output, _error).RunAsync(args[1..], cancellationToken);
    }

    private async Task<int> FailWithHelpAsync(string message)
    {
        await _error.WriteLineAsync(message);
        await _error.WriteLineAsync(CliOutputFormatter.HelpText());
        return 2;
    }

    private static string ParseWorkspaceOption(string[] args)
        => ParseOptionValue(args, "--workspace") ?? Environment.CurrentDirectory;

    private static string ParseRequiredTargetOption(string[] args)
        => ParseOptionValue(args, "--target") ?? throw new ArgumentException("Missing required option --target.");

    private static string? ParseOutputOption(string[] args)
        => ParseOptionValue(args, "--output");

    private static string ParseFormat(string[] args)
    {
        var format = ParseOptionValue(args, "--format") ?? "text";
        if (!string.Equals(format, "text", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsupported format '{format}'. Use 'text' or 'json'.");
        }

        return format.ToLowerInvariant();
    }

    private static CliVerbosity ParseVerbosity(string[] args)
    {
        var quiet = args.Contains("--quiet", StringComparer.OrdinalIgnoreCase);
        var verbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);
        if (quiet && verbose)
        {
            throw new ArgumentException("Use either --quiet or --verbose, not both.");
        }

        return quiet ? CliVerbosity.Quiet : verbose ? CliVerbosity.Verbose : CliVerbosity.Default;
    }

    private static TimeSpan? ParseTimeoutOption(string[] args)
    {
        var value = ParseOptionValue(args, "--timeout");
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!TimeSpan.TryParse(value, out var timeout) || timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException($"Invalid timeout '{value}'. Use a positive TimeSpan such as 00:10:00.");
        }

        return timeout;
    }

    private static SmokeRunSelection ParseSmokeRunSelection(string[] args)
    {
        string? positionalTemplate = null;
        for (var index = 1; index < args.Length; index++)
        {
            if (args[index].StartsWith("--", StringComparison.Ordinal))
            {
                if (OptionConsumesValue(args[index]))
                {
                    index++;
                }

                continue;
            }

            positionalTemplate = args[index];
            break;
        }

        var family = ParseOptionValue(args, "--family");
        var includeAll = args.Contains("--all", StringComparer.OrdinalIgnoreCase);
        var selectedCount = (string.IsNullOrWhiteSpace(positionalTemplate) ? 0 : 1)
            + (string.IsNullOrWhiteSpace(family) ? 0 : 1)
            + (includeAll ? 1 : 0);

        if (selectedCount == 0)
        {
            throw new ArgumentException("Missing smoke selection. Use 'opencode smoke run <template>', '--family <family>', or '--all'.");
        }

        if (selectedCount > 1)
        {
            throw new ArgumentException("Use exactly one smoke selection: a template id, '--family <family>', or '--all'.");
        }

        if (includeAll)
        {
            return new SmokeRunSelection { Mode = SmokeRunSelectionMode.All };
        }

        if (!string.IsNullOrWhiteSpace(family))
        {
            return new SmokeRunSelection { Mode = SmokeRunSelectionMode.Family, Family = family };
        }

        return new SmokeRunSelection { Mode = SmokeRunSelectionMode.SingleTemplate, TemplateId = positionalTemplate };
    }

    private static bool OptionConsumesValue(string optionName)
        => optionName is "--workspace"
            or "--target"
            or "--output"
            or "--run-id"
            or "--format"
            or "--owner"
            or "--project"
            or "--family"
            or "--template"
            or "--parallel"
            or "--artifacts-root"
            or "--timeout";

    private static int MapOutcomeToExitCode(WorkspaceSmokeAutomationOutcome outcome)
        => outcome switch
        {
            WorkspaceSmokeAutomationOutcome.Success => 0,
            WorkspaceSmokeAutomationOutcome.ValidationFailure => 1,
            WorkspaceSmokeAutomationOutcome.InvalidConfiguration => 2,
            WorkspaceSmokeAutomationOutcome.CleanupFailure => 3,
            WorkspaceSmokeAutomationOutcome.LockFailure => 4,
            WorkspaceSmokeAutomationOutcome.ResourceExhaustion => 5,
            WorkspaceSmokeAutomationOutcome.UnsupportedSelection => 6,
            WorkspaceSmokeAutomationOutcome.ToolingFailure => 7,
            WorkspaceSmokeAutomationOutcome.Cancelled => 130,
            _ => 7,
        };

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
                    && !string.Equals(argument, "--project", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(argument, "--legacy", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(argument, "--family", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(argument, "--template", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(argument, "--parallel", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(argument, "--artifacts-root", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(argument, "--keep-workspace", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(argument, "--keep-runtime-on-failure", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(argument, "--quiet", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(argument, "--verbose", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(argument, "--timeout", StringComparison.OrdinalIgnoreCase))
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
        OpenCodeWorkspaceInstallationLayout? installationLayout = null;
        try
        {
            installationLayout = OpenCodeWorkspaceInstallationLayout.Resolve(AppContext.BaseDirectory);
        }
        catch (InvalidOperationException)
        {
        }

        var catalogRoot = ResolveCatalogRoot(request.WorkspacePath) ?? installationLayout?.CatalogRoot;
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
        var catalogRoot = ResolveCatalogRoot(appDataRoot) ?? OpenCodeWorkspaceInstallationLayout.Resolve(AppContext.BaseDirectory).CatalogRoot;
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
        => new global::OpenCode.Workspace.Core.Runtime.SmokeRuntimeOwnershipService(new DockerContainerRuntime(new DockerService(new ProcessRunner()))).CleanupAsync(options, cancellationToken);

    private static Task<LegacyCleanupResult> RunLegacySmokeCleanupAsync(LegacyCleanupOptions options, CancellationToken cancellationToken)
        => new SmokeRuntimeOwnershipService(new DockerContainerRuntime(new DockerService(new ProcessRunner()))).CleanupLegacyAsync(options, cancellationToken);

    private static Task<RuntimeResourceInventory> RunRuntimeInventoryAsync(RuntimeOwnershipQuery query, CancellationToken cancellationToken)
        => new RuntimeOwnershipService(new DockerContainerRuntime(new DockerService(new ProcessRunner()))).BuildInventoryAsync(query, cancellationToken);

    private static Task<WorkspaceSmokeDefinitionCatalogResult> RunSmokeDefinitionsAsync(WorkspaceSmokeDefinitionQuery query, CancellationToken cancellationToken)
    {
        return CreateSmokeApplicationService().ListDefinitionsAsync(query, cancellationToken);
    }

    private static async Task<WorkspaceSmokeResult> RunSmokeTemplateAsync(WorkspaceSmokeSingleRunRequest request, CancellationToken cancellationToken)
        => await CreateSmokeApplicationService().RunAsync(request, cancellationToken);

    private static async Task<WorkspaceSmokeMatrixResult> RunSmokeMatrixAsync(WorkspaceSmokeMatrixRunRequest request, CancellationToken cancellationToken)
        => await CreateSmokeApplicationService().RunMatrixAsync(request, cancellationToken);

    private static WorkspaceSmokeApplicationService CreateSmokeApplicationService()
    {
        var catalogRoot = ResolveCatalogRoot(Environment.CurrentDirectory) ?? OpenCodeWorkspaceInstallationLayout.Resolve(AppContext.BaseDirectory).CatalogRoot;
        var stateRoot = Path.Combine(Path.GetTempPath(), "opencode-workspace-smoke-state");
        return new WorkspaceSmokeApplicationService(catalogRoot, stateRoot, new DockerContainerRuntime(new DockerService(new ProcessRunner())));
    }

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

internal enum SmokeRunSelectionMode
{
    SingleTemplate,
    Family,
    All,
}

internal sealed class SmokeRunSelection
{
    public required SmokeRunSelectionMode Mode { get; init; }
    public string? TemplateId { get; init; }
    public string? Family { get; init; }
}
