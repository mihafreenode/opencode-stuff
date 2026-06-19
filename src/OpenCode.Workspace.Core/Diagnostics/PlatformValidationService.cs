using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Diagnostics;

public sealed class PlatformValidationService
{
    private static readonly TimeSpan ExecutionProbeTimeout = TimeSpan.FromSeconds(60);

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
    private readonly Func<string, CancellationToken, Task<ProcessResult>> _containerExecutionProbe;

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
            provisioningScriptGenerator.Generate,
            (targetPlatform, cancellationToken) => RunContainerExecutionProbeAsync(new ProcessRunner(), targetPlatform, cancellationToken))
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
        : this(
            workspaceDiscoveryService,
            workspaceYamlService,
            platformDetector,
            runtimeResolver,
            workspaceResolver,
            composeGeneration,
            provisioningGeneration,
            (targetPlatform, cancellationToken) => RunContainerExecutionProbeAsync(new ProcessRunner(), targetPlatform, cancellationToken))
    {
    }

    public PlatformValidationService(
        WorkspaceDiscoveryService workspaceDiscoveryService,
        WorkspaceYamlService workspaceYamlService,
        IPlatformDetector platformDetector,
        IRuntimeResolver runtimeResolver,
        WorkspaceResolver workspaceResolver,
        Func<ResolvedWorkspace, WorkspacePaths, string> composeGeneration,
        Func<ResolvedWorkspace, string> provisioningGeneration,
        Func<string, CancellationToken, Task<ProcessResult>> containerExecutionProbe)
    {
        _workspaceDiscoveryService = workspaceDiscoveryService;
        _workspaceYamlService = workspaceYamlService;
        _platformDetector = platformDetector;
        _runtimeResolver = runtimeResolver;
        _workspaceResolver = workspaceResolver;
        _composeGeneration = composeGeneration;
        _provisioningGeneration = provisioningGeneration;
        _containerExecutionProbe = containerExecutionProbe;
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

        var validatedWithFallback = !string.Equals(request.TargetPlatform, resolvedRuntimePlan.TargetPlatform, StringComparison.OrdinalIgnoreCase);
        checks.Add(Info(
            "Runtime resolution",
            validatedWithFallback
                ? $"OK ({resolvedRuntimePlan.Runtime} resolved {request.TargetPlatform} through fallback to {resolvedRuntimePlan.TargetPlatform})"
                : $"OK ({resolvedRuntimePlan.Runtime} resolved {request.TargetPlatform} directly)"));

        if (!hostPlatform.Docker.BuildxAvailable)
        {
            checks.Add(Warning("Buildx build support", $"Buildx is not available. Native validation may still be possible on target hardware for {request.TargetPlatform}."));
        }
        else if (!hostPlatform.Docker.SupportedPlatforms.Contains(request.TargetPlatform, StringComparer.OrdinalIgnoreCase))
        {
            checks.Add(Warning("Buildx build support", $"Active builder does not advertise {request.TargetPlatform}. Native validation may still be possible on target hardware."));
        }
        else
        {
            checks.Add(Info("Buildx build support", "OK"));
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

        try
        {
            var executionProbeResult = await _containerExecutionProbe(request.TargetPlatform, cancellationToken);
            if (!executionProbeResult.IsSuccess)
            {
                checks.Add(Error("Container execution", BuildExecutionFailureMessage(request.TargetPlatform, executionProbeResult.StandardError)));
                return CreateReport(location.WorkspaceRootPath, request.TargetPlatform, configurationPath, checks, resolvedRuntimePlan);
            }

            var reportedArchitecture = executionProbeResult.StandardOutputLines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim()
                ?? executionProbeResult.StandardOutput.Trim();
            if (!IsExpectedExecutionArchitecture(request.TargetPlatform, reportedArchitecture))
            {
                checks.Add(Error("Container execution", $"Execution probe for {request.TargetPlatform} returned unexpected architecture '{reportedArchitecture}'."));
                return CreateReport(location.WorkspaceRootPath, request.TargetPlatform, configurationPath, checks, resolvedRuntimePlan);
            }

            checks.Add(Info("Container execution", $"OK ({reportedArchitecture})"));
        }
        catch (Exception exception)
        {
            checks.Add(Error("Container execution", BuildExecutionFailureMessage(request.TargetPlatform, exception.Message)));
            return CreateReport(location.WorkspaceRootPath, request.TargetPlatform, configurationPath, checks, resolvedRuntimePlan);
        }

        return CreateReport(location.WorkspaceRootPath, request.TargetPlatform, configurationPath, checks, resolvedRuntimePlan);
    }

    private static PlatformValidationReport CreateReport(string workspaceRootPath, string targetPlatform, string? configurationPath, IReadOnlyList<PlatformValidationCheckResult> checks, ResolvedRuntimePlan? resolvedRuntimePlan)
    {
        var hasErrors = checks.Any(item => item.Severity == DiagnosticSeverity.Error);
        var hasWarnings = checks.Any(item => item.Severity == DiagnosticSeverity.Warning);
        var hostExecutionFailed = checks.Any(item => item.Name == "Container execution" && item.Severity == DiagnosticSeverity.Error);
        var summary = hasErrors
            ? hostExecutionFailed
                ? $"{targetPlatform} validation failed on this host."
                : $"{targetPlatform} validation failed."
            : hasWarnings
                ? $"{targetPlatform} validation completed with warnings."
                : $"{targetPlatform} validation passed.";

        var resolvedPlatform = resolvedRuntimePlan?.TargetPlatform;
        var validatedWithFallback = resolvedRuntimePlan is not null
            && !string.IsNullOrWhiteSpace(targetPlatform)
            && !string.IsNullOrWhiteSpace(resolvedPlatform)
            && !string.Equals(targetPlatform, resolvedPlatform, StringComparison.OrdinalIgnoreCase);

        return new PlatformValidationReport
        {
            WorkspaceRootPath = workspaceRootPath,
            TargetPlatform = targetPlatform,
            WorkspaceConfigurationPath = configurationPath,
            ResolvedRuntimePlan = resolvedRuntimePlan,
            ResolvedPlatform = resolvedPlatform,
            CompatibilityDisplay = FormatCompatibility(resolvedRuntimePlan, targetPlatform),
            ValidatedWithFallback = validatedWithFallback,
            Checks = checks,
            IsSuccess = !hasErrors,
            HasWarnings = hasWarnings,
            Summary = summary,
        };
    }

    private static string? FormatCompatibility(ResolvedRuntimePlan? resolvedRuntimePlan, string requestedTarget)
    {
        if (resolvedRuntimePlan is null)
        {
            return null;
        }

        if (resolvedRuntimePlan.IsAvailable && string.Equals(requestedTarget, resolvedRuntimePlan.TargetPlatform, StringComparison.OrdinalIgnoreCase))
        {
            return "direct";
        }

        return resolvedRuntimePlan.CompatibilityMode switch
        {
            RuntimeCompatibilityMode.Native => "fallback",
            RuntimeCompatibilityMode.MultiArchitecture => "multi-architecture fallback",
            RuntimeCompatibilityMode.Emulated => "emulated fallback",
            RuntimeCompatibilityMode.Unavailable => "unavailable",
            _ => "unresolved",
        };
    }

    public static bool IsExpectedExecutionArchitecture(string targetPlatform, string? reportedArchitecture)
    {
        if (string.IsNullOrWhiteSpace(reportedArchitecture))
        {
            return false;
        }

        var normalized = reportedArchitecture.Trim();
        return targetPlatform.ToLowerInvariant() switch
        {
            "linux/arm64" => string.Equals(normalized, "aarch64", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "arm64", StringComparison.OrdinalIgnoreCase),
            "linux/amd64" => string.Equals(normalized, "x86_64", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "amd64", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static Task<ProcessResult> RunContainerExecutionProbeAsync(IProcessRunner processRunner, string targetPlatform, CancellationToken cancellationToken)
    {
        return processRunner.RunAsync(
            "docker",
            ["run", "--rm", "--platform", targetPlatform, "ubuntu:24.04", "uname", "-m"],
            cancellationToken: cancellationToken,
            timeout: ExecutionProbeTimeout);
    }

    private static string BuildExecutionFailureMessage(string targetPlatform, string? technicalDetails)
    {
        if (string.Equals(targetPlatform, "linux/arm64", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = string.IsNullOrWhiteSpace(technicalDetails) ? string.Empty : $" Technical details: {technicalDetails.Trim()}";
            return "This host cannot currently execute linux/arm64 containers. Enable container emulation, use a builder/runtime with linux/arm64 support, or validate on real ARM64 hardware." + suffix;
        }

        return string.IsNullOrWhiteSpace(technicalDetails)
            ? $"Execution probe failed for {targetPlatform}."
            : technicalDetails.Trim();
    }

    private static PlatformValidationCheckResult Info(string name, string message) => new() { Name = name, Severity = DiagnosticSeverity.Information, Message = message };
    private static PlatformValidationCheckResult Warning(string name, string message) => new() { Name = name, Severity = DiagnosticSeverity.Warning, Message = message };
    private static PlatformValidationCheckResult Error(string name, string message) => new() { Name = name, Severity = DiagnosticSeverity.Error, Message = message };
}
