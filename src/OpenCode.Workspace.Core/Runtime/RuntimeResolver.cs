using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Runtime;

public sealed class RuntimeResolver : IRuntimeResolver
{
    public Task<ResolvedRuntimePlan> ResolveAsync(WorkspaceDefinition definition, HostPlatformInfo hostPlatform, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var docker = hostPlatform.Docker;
        if (!docker.CliAvailable)
        {
            return Task.FromResult(Unavailable(hostPlatform, "Docker CLI is not available on this machine."));
        }

        if (!docker.EngineReachable)
        {
            return Task.FromResult(Unavailable(hostPlatform, "Docker is installed but the engine is not reachable."));
        }

        var nativePlatform = hostPlatform.NativeContainerPlatform;
        if (string.IsNullOrWhiteSpace(nativePlatform))
        {
            return Task.FromResult(Unavailable(hostPlatform, $"Host architecture '{hostPlatform.Architecture}' does not map to a supported Linux container target yet."));
        }

        var supportedPlatforms = docker.SupportedPlatforms;
        var supportsNative = supportedPlatforms.Count == 0 || supportedPlatforms.Contains(nativePlatform, StringComparer.OrdinalIgnoreCase);
        if (supportsNative)
        {
            var compatibilityMode = docker.BuildxAvailable && supportedPlatforms.Contains("linux/amd64", StringComparer.OrdinalIgnoreCase) && supportedPlatforms.Contains("linux/arm64", StringComparer.OrdinalIgnoreCase)
                ? RuntimeCompatibilityMode.MultiArchitecture
                : RuntimeCompatibilityMode.Native;
            var explanation = compatibilityMode == RuntimeCompatibilityMode.MultiArchitecture
                ? $"Docker is reachable and supports the native target '{nativePlatform}' with multi-architecture builder support available."
                : $"Docker is reachable and the native target '{nativePlatform}' can be used."
                ;

            return Task.FromResult(new ResolvedRuntimePlan
            {
                Runtime = "docker",
                TargetPlatform = nativePlatform,
                CompatibilityMode = compatibilityMode,
                SupportLevel = compatibilityMode == RuntimeCompatibilityMode.Native ? SupportLevel.NativeTested : SupportLevel.EmulatedTested,
                IsAvailable = true,
                DiagnosticExplanation = explanation,
                HostPlatform = hostPlatform,
            });
        }

        var fallbackPlatform = ResolveFallbackPlatform(hostPlatform.Architecture, supportedPlatforms);
        if (!string.IsNullOrWhiteSpace(fallbackPlatform))
        {
            return Task.FromResult(new ResolvedRuntimePlan
            {
                Runtime = "docker",
                TargetPlatform = fallbackPlatform,
                CompatibilityMode = RuntimeCompatibilityMode.Emulated,
                SupportLevel = SupportLevel.EmulatedTested,
                IsAvailable = true,
                DiagnosticExplanation = $"Docker cannot confirm native target '{nativePlatform}', so the workspace will fall back to compatible target '{fallbackPlatform}'.",
                HostPlatform = hostPlatform,
            });
        }

        return Task.FromResult(Unavailable(hostPlatform, string.IsNullOrWhiteSpace(docker.DiagnosticSummary)
            ? $"Docker does not report support for the native target '{nativePlatform}'."
            : docker.DiagnosticSummary));
    }

    private static ResolvedRuntimePlan Unavailable(HostPlatformInfo hostPlatform, string explanation)
    {
        return new ResolvedRuntimePlan
        {
            Runtime = "docker",
            TargetPlatform = hostPlatform.NativeContainerPlatform,
            CompatibilityMode = RuntimeCompatibilityMode.Unavailable,
            SupportLevel = SupportLevel.Unavailable,
            IsAvailable = false,
            DiagnosticExplanation = explanation,
            HostPlatform = hostPlatform,
        };
    }

    private static string ResolveFallbackPlatform(HostArchitecture architecture, IReadOnlyList<string> supportedPlatforms)
    {
        return architecture switch
        {
            HostArchitecture.Arm64 when supportedPlatforms.Contains("linux/amd64", StringComparer.OrdinalIgnoreCase) => "linux/amd64",
            HostArchitecture.X64 when supportedPlatforms.Contains("linux/arm64", StringComparer.OrdinalIgnoreCase) => "linux/arm64",
            _ => string.Empty,
        };
    }
}
