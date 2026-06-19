using OpenCode.Workspace.Cli;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Cli.Tests;

public sealed class CliOutputFormatterTests
{
    [Fact]
    public void FormatDoctor_IncludesWorkspaceAndResolutionSections()
    {
        var text = CliOutputFormatter.FormatDoctor(new WorkspaceDoctorResult
        {
            WorkspaceRootPath = "/workspace",
            RuntimeStatePath = "/workspace/.opencode/local/runtime-state.yaml",
            HostPlatform = new HostPlatformInfo
            {
                OperatingSystem = HostOperatingSystem.Linux,
                Architecture = HostArchitecture.X64,
                Docker = new ContainerRuntimeAvailability
                {
                    EngineId = "docker",
                    CliAvailable = true,
                    EngineReachable = true,
                    BuildxAvailable = true,
                    SupportedPlatforms = ["linux/amd64", "linux/arm64"],
                },
            },
            WorkspaceConfigurationStatus = WorkspaceConfigurationStatus.Found,
            WorkspaceConfigurationPath = "workspace.yaml",
            RuntimeStateStatus = WorkspaceRuntimeStateReadStatus.Loaded,
            ResolvedRuntimePlan = new ResolvedRuntimePlan
            {
                Runtime = "docker",
                TargetPlatform = "linux/amd64",
                CompatibilityMode = RuntimeCompatibilityMode.Native,
                SupportLevel = SupportLevel.NativeTested,
                IsAvailable = true,
                DiagnosticExplanation = "Workspace can run on this machine.",
            },
            CanRun = true,
            Recommendation = "Workspace can run on this machine.",
        });

        Assert.Contains("OpenCode Doctor", text, StringComparison.Ordinal);
        Assert.Contains("Local runtime state path: .opencode/local/runtime-state.yaml", text, StringComparison.Ordinal);
        Assert.Contains("Compatibility: native", text, StringComparison.OrdinalIgnoreCase);
    }
}
