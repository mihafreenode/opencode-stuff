using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Diagnostics;

public sealed class WorkspaceDoctorService
{
    private static readonly TimeSpan Arm64ExecutionProbeTimeout = TimeSpan.FromSeconds(60);

    private readonly IPlatformDetector _platformDetector;
    private readonly IRuntimeResolver _runtimeResolver;
    private readonly WorkspaceDiscoveryService _workspaceDiscoveryService;
    private readonly WorkspaceYamlService _workspaceYamlService;
    private readonly WorkspaceRuntimeStateService _workspaceRuntimeStateService;
    private readonly Func<CancellationToken, Task<ProcessResult>> _arm64ExecutionProbe;

    public WorkspaceDoctorService(
        IPlatformDetector platformDetector,
        IRuntimeResolver runtimeResolver,
        WorkspaceDiscoveryService workspaceDiscoveryService,
        WorkspaceYamlService workspaceYamlService,
        WorkspaceRuntimeStateService workspaceRuntimeStateService)
        : this(
            platformDetector,
            runtimeResolver,
            workspaceDiscoveryService,
            workspaceYamlService,
            workspaceRuntimeStateService,
            cancellationToken => RunArm64ExecutionProbeAsync(new ProcessRunner(), cancellationToken))
    {
    }

    public WorkspaceDoctorService(
        IPlatformDetector platformDetector,
        IRuntimeResolver runtimeResolver,
        WorkspaceDiscoveryService workspaceDiscoveryService,
        WorkspaceYamlService workspaceYamlService,
        WorkspaceRuntimeStateService workspaceRuntimeStateService,
        Func<CancellationToken, Task<ProcessResult>> arm64ExecutionProbe)
    {
        _platformDetector = platformDetector;
        _runtimeResolver = runtimeResolver;
        _workspaceDiscoveryService = workspaceDiscoveryService;
        _workspaceYamlService = workspaceYamlService;
        _workspaceRuntimeStateService = workspaceRuntimeStateService;
        _arm64ExecutionProbe = arm64ExecutionProbe;
    }

    public async Task<WorkspaceDoctorResult> DiagnoseAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var location = ResolveWorkspaceLocation(workspacePath);
        var discovery = location.ExplicitConfigurationPath is null
            ? _workspaceDiscoveryService.Discover(location.WorkspaceRootPath)
            : new WorkspaceDiscoveryResult
            {
                Status = File.Exists(Path.Combine(location.WorkspaceRootPath, location.ExplicitConfigurationPath.Replace('/', Path.DirectorySeparatorChar)))
                    ? WorkspaceDiscoveryStatus.Found
                    : WorkspaceDiscoveryStatus.NotFound,
                ConfigurationPath = location.ExplicitConfigurationPath,
            };
        var paths = WorkspacePathBuilder.Build(location.WorkspaceRootPath, discovery.ConfigurationPath ?? location.ExplicitConfigurationPath ?? "workspace.yaml");
        var runtimeStateRead = _workspaceRuntimeStateService.ReadWithStatus(paths.RuntimeStatePath);

        HostPlatformInfo? hostPlatform = null;
        ResolvedRuntimePlan? resolvedRuntimePlan = null;
        WorkspaceConfigurationStatus configurationStatus;
        string? configurationError = null;
        var canRun = false;
        var recommendation = string.Empty;
        var arm64ExecutionSupportStatus = Arm64ExecutionSupportStatus.Unknown;
        string? arm64ExecutionSupportDetails = null;

        try
        {
            hostPlatform = await _platformDetector.DetectAsync(cancellationToken);
            if (hostPlatform is not null)
            {
                (arm64ExecutionSupportStatus, arm64ExecutionSupportDetails) = await DetermineArm64ExecutionSupportAsync(hostPlatform, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            recommendation = $"Platform detection failed. {exception.Message}";
        }

        if (discovery.Status == WorkspaceDiscoveryStatus.NotFound)
        {
            configurationStatus = WorkspaceConfigurationStatus.NotFound;
            recommendation = string.IsNullOrWhiteSpace(recommendation)
                ? "workspace.yaml was not found. Run this command from a workspace root or pass --workspace <path>."
                : recommendation;
        }
        else if (discovery.Status == WorkspaceDiscoveryStatus.Invalid)
        {
            configurationStatus = WorkspaceConfigurationStatus.Invalid;
            configurationError = discovery.ErrorMessage;
            recommendation = $"Workspace configuration is invalid. {discovery.ErrorMessage}".Trim();
        }
        else
        {
            configurationStatus = WorkspaceConfigurationStatus.Found;

            try
            {
                var definition = _workspaceYamlService.Read(paths.WorkspaceYamlPath);
                if (hostPlatform is not null)
                {
                    resolvedRuntimePlan = await _runtimeResolver.ResolveAsync(definition, hostPlatform, cancellationToken);
                    canRun = resolvedRuntimePlan.IsAvailable;
                    recommendation = resolvedRuntimePlan.IsAvailable
                        ? "Workspace can run on this machine."
                        : string.IsNullOrWhiteSpace(resolvedRuntimePlan.DiagnosticExplanation)
                            ? "Workspace cannot run on this machine until the container runtime is ready."
                            : resolvedRuntimePlan.DiagnosticExplanation;
                }
                else if (string.IsNullOrWhiteSpace(recommendation))
                {
                    recommendation = "Platform detection did not complete, so runtime readiness could not be confirmed.";
                }
            }
            catch (Exception exception)
            {
                configurationStatus = WorkspaceConfigurationStatus.Invalid;
                configurationError = exception.Message;
                recommendation = $"Workspace configuration is invalid. {exception.Message}".Trim();
            }
        }

        if (hostPlatform is not null && !canRun && configurationStatus == WorkspaceConfigurationStatus.Found)
        {
            recommendation = BuildDockerRecommendation(hostPlatform, recommendation);
        }

        return new WorkspaceDoctorResult
        {
            WorkspaceRootPath = location.WorkspaceRootPath,
            RuntimeStatePath = paths.RuntimeStatePath,
            HostPlatform = hostPlatform,
            WorkspaceConfigurationStatus = configurationStatus,
            WorkspaceConfigurationPath = discovery.ConfigurationPath,
            WorkspaceConfigurationError = configurationError,
            RuntimeStateStatus = runtimeStateRead.Status,
            RuntimeState = runtimeStateRead.State,
            Arm64ExecutionSupportStatus = arm64ExecutionSupportStatus,
            Arm64ExecutionSupportDetails = arm64ExecutionSupportDetails,
            ResolvedRuntimePlan = resolvedRuntimePlan,
            CanRun = canRun,
            Recommendation = recommendation,
        };
    }

    private async Task<(Arm64ExecutionSupportStatus Status, string? Details)> DetermineArm64ExecutionSupportAsync(HostPlatformInfo hostPlatform, CancellationToken cancellationToken)
    {
        if (!hostPlatform.Docker.CliAvailable || !hostPlatform.Docker.EngineReachable)
        {
            return (Arm64ExecutionSupportStatus.Unknown, null);
        }

        try
        {
            var probe = await _arm64ExecutionProbe(cancellationToken);
            if (probe.IsSuccess)
            {
                var reportedArchitecture = probe.StandardOutputLines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim()
                    ?? probe.StandardOutput.Trim();
                if (PlatformValidationService.IsExpectedExecutionArchitecture("linux/arm64", reportedArchitecture))
                {
                    return (Arm64ExecutionSupportStatus.Available, $"Execution probe OK ({reportedArchitecture})");
                }

                return (Arm64ExecutionSupportStatus.Unavailable, $"Execution probe returned unexpected architecture '{reportedArchitecture}'.");
            }

            return (Arm64ExecutionSupportStatus.Unavailable, probe.StandardError.Trim());
        }
        catch
        {
            // Fall back to advertised builder support when runtime probing is unavailable.
        }

        return hostPlatform.Docker.SupportedPlatforms.Contains("linux/arm64", StringComparer.OrdinalIgnoreCase)
            ? (Arm64ExecutionSupportStatus.Available, "Buildx advertises linux/arm64.")
            : (Arm64ExecutionSupportStatus.Unavailable, "Buildx does not advertise linux/arm64.");
    }

    private static string BuildDockerRecommendation(HostPlatformInfo hostPlatform, string existingRecommendation)
    {
        if (!hostPlatform.Docker.CliAvailable)
        {
            return "Docker CLI is not available. Install Docker Desktop or a compatible Docker runtime.";
        }

        if (!hostPlatform.Docker.EngineReachable)
        {
            return "Docker engine is not reachable. Start Docker Desktop or install a compatible Docker runtime.";
        }

        return existingRecommendation;
    }

    internal static WorkspaceLocation ResolveWorkspaceLocation(string workspacePath)
    {
        var fullPath = Path.GetFullPath(string.IsNullOrWhiteSpace(workspacePath) ? Environment.CurrentDirectory : workspacePath);
        if (File.Exists(fullPath))
        {
            var root = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
            var relative = Path.GetFileName(fullPath).Replace(Path.DirectorySeparatorChar, '/');
            return new WorkspaceLocation(root, relative);
        }

        return new WorkspaceLocation(fullPath, null);
    }

    internal sealed record WorkspaceLocation(string WorkspaceRootPath, string? ExplicitConfigurationPath);

    private static Task<ProcessResult> RunArm64ExecutionProbeAsync(IProcessRunner processRunner, CancellationToken cancellationToken)
    {
        return processRunner.RunAsync(
            "docker",
            ["run", "--rm", "--platform", "linux/arm64", "ubuntu:24.04", "uname", "-m"],
            cancellationToken: cancellationToken,
            timeout: Arm64ExecutionProbeTimeout);
    }
}
