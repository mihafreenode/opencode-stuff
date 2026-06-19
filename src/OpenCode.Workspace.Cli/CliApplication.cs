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

    public CliApplication(TextWriter output, TextWriter error)
        : this(output, error, null, null)
    {
    }

    public CliApplication(
        TextWriter output,
        TextWriter error,
        Func<string, CancellationToken, Task<WorkspaceDoctorResult>>? doctorRunner,
        Func<PlatformValidationRequest, CancellationToken, Task<PlatformValidationReport>>? platformValidationRunner)
    {
        _output = output;
        _error = error;
        _doctorRunner = doctorRunner ?? RunDoctorAsync;
        _platformValidationRunner = platformValidationRunner ?? RunPlatformValidationAsync;
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
        var report = await _platformValidationRunner(new PlatformValidationRequest
        {
            WorkspacePath = workspacePath,
            TargetPlatform = target,
        }, cancellationToken);
        await _output.WriteLineAsync(CliOutputFormatter.FormatPlatformValidation(report));
        return report.IsSuccess ? 0 : 1;
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

    private static string? ParseOptionValue(string[] args, string optionName)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase))
            {
                if (argument.StartsWith("--", StringComparison.Ordinal) && !string.Equals(argument, "--workspace", StringComparison.OrdinalIgnoreCase) && !string.Equals(argument, "--target", StringComparison.OrdinalIgnoreCase))
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
}
