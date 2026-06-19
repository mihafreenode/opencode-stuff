using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Diagnostics;

public sealed class PlatformValidationService
{
    public static readonly IReadOnlySet<string> SupportedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "linux/amd64",
        "linux/arm64",
    };

    private readonly WorkspaceDiscoveryService _workspaceDiscoveryService;
    private readonly WorkspaceYamlService _workspaceYamlService;
    private readonly IPlatformDetector _platformDetector;
    private readonly IRuntimeResolver _runtimeResolver;
    private readonly WorkspaceResolver _workspaceResolver;
    private readonly Func<ResolvedWorkspace, WorkspacePaths, string> _composeGeneration;
    private readonly Func<ResolvedWorkspace, string> _provisioningGeneration;

    public PlatformValidationService(
        WorkspaceDiscoveryService workspaceDiscoveryService,
        WorkspaceYamlService workspaceYamlService,
        IPlatformDetector platformDetector,
        IRuntimeResolver runtimeResolver,
        WorkspaceResolver workspaceResolver,
        ComposeGenerator composeGenerator,
        ProvisioningScriptGenerator provisioningScriptGenerator)
        : this(
            workspaceDiscoveryService,
            workspaceYamlService,
            platformDetector,
            runtimeResolver,
            workspaceResolver,
            composeGenerator.Generate,
            provisioningScriptGenerator.Generate)
    {
    }

    public PlatformValidationService(
        WorkspaceDiscoveryService workspaceDiscoveryService,
        WorkspaceYamlService workspaceYamlService,
        IPlatformDetector platformDetector,
        IRuntimeResolver runtimeResolver,
        WorkspaceResolver workspaceResolver,
        Func<ResolvedWorkspace, WorkspacePaths, string> composeGeneration,
        Func<ResolvedWorkspace, string> provisioningGeneration)
    {
        _workspaceDiscoveryService = workspaceDiscoveryService;
        _workspaceYamlService = workspaceYamlService;
        _platformDetector = platformDetector;
        _runtimeResolver = runtimeResolver;
        _workspaceResolver = workspaceResolver;
        _composeGeneration = composeGeneration;
        _provisioningGeneration = provisioningGeneration;
    }

    public async Task<PlatformValidationReport> ValidateAsync(PlatformValidationRequest request, CancellationToken cancellationToken = default)
    {
        var checks = new List<PlatformValidationCheckResult>();
        var location = WorkspaceDoctorService.ResolveWorkspaceLocation(request.WorkspacePath);
        var discovery = location.ExplicitConfigurationPath is null
            ? _workspaceDiscoveryService.Discover(location.WorkspaceRootPath)
            : new WorkspaceDiscoveryResult
            {
                Status = File.Exists(Path.Combine(location.WorkspaceRootPath, location.ExplicitConfigurationPath.Replace('/', Path.DirectorySeparatorChar)))
                    ? WorkspaceDiscoveryStatus.Found
                    : WorkspaceDiscoveryStatus.NotFound,
                ConfigurationPath = location.ExplicitConfigurationPath,
            };
        var configurationPath = discovery.ConfigurationPath ?? location.ExplicitConfigurationPath;

        if (!SupportedTargets.Contains(request.TargetPlatform))
        {
            checks.Add(Error("Target", $"Unsupported target '{request.TargetPlatform}'. Supported targets: linux/amd64, linux/arm64."));
            return CreateReport(location.WorkspaceRootPath, request.TargetPlatform, configurationPath, checks, null);
        }

        WorkspaceDefinition? definition = null;
        if (discovery.Status == WorkspaceDiscoveryStatus.NotFound)
        {
            checks.Add(Error("Workspace config", "workspace.yaml was not found."));
            return CreateReport(location.WorkspaceRootPath, request.TargetPlatform, configurationPath, checks, null);
        }

        if (discovery.Status == WorkspaceDiscoveryStatus.Invalid)
        {
            checks.Add(Error("Workspace config", discovery.ErrorMessage ?? "Workspace configuration is invalid."));
            return CreateReport(location.WorkspaceRootPath, request.TargetPlatform, configurationPath, checks, null);
        }

        var paths = WorkspacePathBuilder.Build(location.WorkspaceRootPath, configurationPath ?? "workspace.yaml");
        try
        {
            definition = _workspaceYamlService.Read(paths.WorkspaceYamlPath);
            checks.Add(Info("Workspace config", "OK"));
        }
        catch (Exception exception)
        {
            checks.Add(Error("Workspace config", exception.Message));
            return CreateReport(location.WorkspaceRootPath, request.TargetPlatform, configurationPath, checks, null);
        }

        HostPlatformInfo hostPlatform;
        try
        {
            hostPlatform = await _platformDetector.DetectAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            checks.Add(Error("Runtime resolution", $"Platform detection failed. {exception.Message}"));
            return CreateReport(location.WorkspaceRootPath, request.TargetPlatform, configurationPath, checks, null);
        }

        ResolvedRuntimePlan resolvedRuntimePlan;
        try
        {
            resolvedRuntimePlan = await _runtimeResolver.ResolveAsync(definition, hostPlatform, cancellationToken);
        }
        catch (Exception exception)
        {
            checks.Add(Error("Runtime resolution", exception.Message));
            return CreateReport(location.WorkspaceRootPath, request.TargetPlatform, configurationPath, checks, null);
        }

        if (!resolvedRuntimePlan.IsAvailable)
        {
            checks.Add(Error("Runtime resolution", string.IsNullOrWhiteSpace(resolvedRuntimePlan.DiagnosticExplanation)
                ? "Runtime resolution did not produce an available plan."
                : resolvedRuntimePlan.DiagnosticExplanation));
            return CreateReport(location.WorkspaceRootPath, request.TargetPlatform, configurationPath, checks, resolvedRuntimePlan);
        }

        checks.Add(Info("Runtime resolution", $"OK ({resolvedRuntimePlan.Runtime} -> {resolvedRuntimePlan.TargetPlatform})"));

        if (!hostPlatform.Docker.BuildxAvailable)
        {
            checks.Add(Warning("Buildx support", $"Buildx is not available. Native validation may still be possible on target hardware for {request.TargetPlatform}."));
        }
        else if (!hostPlatform.Docker.SupportedPlatforms.Contains(request.TargetPlatform, StringComparer.OrdinalIgnoreCase))
        {
            checks.Add(Warning("Buildx support", $"Active builder does not advertise {request.TargetPlatform}. Native validation may still be possible on target hardware."));
        }
        else
        {
            checks.Add(Info("Buildx support", "OK"));
        }

        ResolvedWorkspace resolvedWorkspace;
        try
        {
            resolvedWorkspace = _workspaceResolver.Resolve(definition);
            _composeGeneration(resolvedWorkspace, paths);
            checks.Add(Info("Compose generation", "OK"));
        }
        catch (Exception exception)
        {
            checks.Add(Error("Compose generation", exception.Message));
            return CreateReport(location.WorkspaceRootPath, request.TargetPlatform, configurationPath, checks, resolvedRuntimePlan);
        }

        try
        {
            _provisioningGeneration(resolvedWorkspace);
            checks.Add(Info("Provisioning generation", "OK"));
        }
        catch (Exception exception)
        {
            checks.Add(Error("Provisioning generation", exception.Message));
            return CreateReport(location.WorkspaceRootPath, request.TargetPlatform, configurationPath, checks, resolvedRuntimePlan);
        }

        return CreateReport(location.WorkspaceRootPath, request.TargetPlatform, configurationPath, checks, resolvedRuntimePlan);
    }

    private static PlatformValidationReport CreateReport(string workspaceRootPath, string targetPlatform, string? configurationPath, IReadOnlyList<PlatformValidationCheckResult> checks, ResolvedRuntimePlan? resolvedRuntimePlan)
    {
        var hasErrors = checks.Any(item => item.Severity == DiagnosticSeverity.Error);
        var hasWarnings = checks.Any(item => item.Severity == DiagnosticSeverity.Warning);
        var summary = hasErrors
            ? $"{targetPlatform} validation failed."
            : hasWarnings
                ? $"{targetPlatform} validation completed with warnings."
                : $"{targetPlatform} validation passed.";

        return new PlatformValidationReport
        {
            WorkspaceRootPath = workspaceRootPath,
            TargetPlatform = targetPlatform,
            WorkspaceConfigurationPath = configurationPath,
            ResolvedRuntimePlan = resolvedRuntimePlan,
            Checks = checks,
            IsSuccess = !hasErrors,
            HasWarnings = hasWarnings,
            Summary = summary,
        };
    }

    private static PlatformValidationCheckResult Info(string name, string message) => new() { Name = name, Severity = DiagnosticSeverity.Information, Message = message };
    private static PlatformValidationCheckResult Warning(string name, string message) => new() { Name = name, Severity = DiagnosticSeverity.Warning, Message = message };
    private static PlatformValidationCheckResult Error(string name, string message) => new() { Name = name, Severity = DiagnosticSeverity.Error, Message = message };
}
