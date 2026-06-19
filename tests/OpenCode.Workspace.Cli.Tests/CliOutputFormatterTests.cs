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
        Assert.Contains("Result:", text, StringComparison.Ordinal);
        Assert.Contains("Workspace can run on this machine.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatDoctor_NormalizesNativeLinuxAmd64Output_AndSeparatesDockerDetails()
    {
        var text = CliOutputFormatter.FormatDoctor(new WorkspaceDoctorResult
        {
            WorkspaceRootPath = "/workspace",
            RuntimeStatePath = "/workspace/.opencode/local/runtime-state.yaml",
            HostPlatform = new HostPlatformInfo
            {
                OperatingSystem = HostOperatingSystem.Linux,
                Architecture = HostArchitecture.X64,
                NativeContainerPlatform = "linux/amd64",
                Docker = new ContainerRuntimeAvailability
                {
                    EngineId = "docker",
                    CliAvailable = true,
                    EngineReachable = true,
                    BuildxAvailable = true,
                    SupportedPlatforms = ["linux/amd64", "linux/arm64"],
                    DiagnosticSummary = "Docker CLI available: Docker version 27.0.0\nDocker engine reachable.\nDocker Buildx available for linux/amd64, linux/arm64.",
                },
            },
            WorkspaceConfigurationStatus = WorkspaceConfigurationStatus.Found,
            WorkspaceConfigurationPath = "workspace.yaml",
            RuntimeStateStatus = WorkspaceRuntimeStateReadStatus.Missing,
            ResolvedRuntimePlan = new ResolvedRuntimePlan
            {
                Runtime = "docker",
                TargetPlatform = "linux/amd64",
                CompatibilityMode = RuntimeCompatibilityMode.Unavailable,
                SupportLevel = SupportLevel.NativeTested,
                IsAvailable = true,
                DiagnosticExplanation = "Docker is reachable and the native target can be used.",
                HostPlatform = new HostPlatformInfo
                {
                    NativeContainerPlatform = "linux/amd64",
                },
            },
            CanRun = true,
            Recommendation = "Docker CLI available: Docker version 27.0.0. Docker engine reachable.",
        });

        Assert.Contains("Compatibility: native", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Platforms: linux/amd64, linux/arm64", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("(+3)", text, StringComparison.Ordinal);
        Assert.Contains("Detail: Docker engine reachable", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Workspace can run on this machine.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatDoctor_DoesNotSplitDockerVersionNumbersOnPeriods()
    {
        var text = CliOutputFormatter.FormatDoctor(new WorkspaceDoctorResult
        {
            WorkspaceRootPath = "/workspace",
            RuntimeStatePath = "/workspace/.opencode/local/runtime-state.yaml",
            HostPlatform = new HostPlatformInfo
            {
                OperatingSystem = HostOperatingSystem.Linux,
                Architecture = HostArchitecture.X64,
                NativeContainerPlatform = "linux/amd64",
                Docker = new ContainerRuntimeAvailability
                {
                    EngineId = "docker",
                    CliAvailable = true,
                    EngineReachable = true,
                    BuildxAvailable = true,
                    SupportedPlatforms = ["linux/amd64"],
                    DiagnosticSummary = "Docker CLI available: Docker version 29.1.3, build 29.1.3-0ubuntu3~24.04.2\nDocker engine reachable.\nDocker Buildx available for linux/amd64.",
                },
            },
            WorkspaceConfigurationStatus = WorkspaceConfigurationStatus.Found,
            WorkspaceConfigurationPath = "workspace.yaml",
            RuntimeStateStatus = WorkspaceRuntimeStateReadStatus.Missing,
            ResolvedRuntimePlan = new ResolvedRuntimePlan
            {
                Runtime = "docker",
                TargetPlatform = "linux/amd64",
                CompatibilityMode = RuntimeCompatibilityMode.Native,
                SupportLevel = SupportLevel.NativeTested,
                IsAvailable = true,
                DiagnosticExplanation = "Docker is reachable and the native target can be used.",
                HostPlatform = new HostPlatformInfo
                {
                    NativeContainerPlatform = "linux/amd64",
                },
            },
            CanRun = true,
            Recommendation = "Workspace can run on this machine.",
        });

        Assert.Contains("Detail: Docker CLI available: Docker version 29.1.3, build 29.1.3-0ubuntu3~24.04.2", text, StringComparison.Ordinal);
        Assert.Contains("Detail: Docker engine reachable.", text, StringComparison.Ordinal);
        Assert.Contains("Detail: Docker Buildx available for linux/amd64.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Detail: 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Detail: 3-0ubuntu3~24", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatPlatformValidation_SeparatesRequestedResolvedAndFallbackBehavior()
    {
        var text = CliOutputFormatter.FormatPlatformValidation(new PlatformValidationReport
        {
            WorkspaceRootPath = "/workspace",
            TargetPlatform = "linux/arm64",
            ResolvedPlatform = "linux/amd64",
            CompatibilityDisplay = "emulated fallback",
            ValidatedWithFallback = true,
            ResolvedRuntimePlan = new ResolvedRuntimePlan
            {
                Runtime = "docker",
                TargetPlatform = "linux/amd64",
                CompatibilityMode = RuntimeCompatibilityMode.Emulated,
                SupportLevel = SupportLevel.EmulatedTested,
                IsAvailable = true,
            },
            Checks =
            [
                new PlatformValidationCheckResult { Name = "Buildx build support", Severity = DiagnosticSeverity.Warning, Message = "Active builder does not advertise linux/arm64." },
                new PlatformValidationCheckResult { Name = "Container execution", Severity = DiagnosticSeverity.Information, Message = "OK (aarch64)" },
                new PlatformValidationCheckResult { Name = "Runtime resolution", Severity = DiagnosticSeverity.Information, Message = "OK" },
            ],
            IsSuccess = true,
            HasWarnings = true,
            Summary = "linux/arm64 validation completed with warnings.",
        });

        Assert.Contains("Requested target: linux/arm64", text, StringComparison.Ordinal);
        Assert.Contains("Resolved workspace platform: linux/amd64", text, StringComparison.Ordinal);
        Assert.Contains("Compatibility: emulated fallback", text, StringComparison.Ordinal);
        Assert.Contains("Requested target execution: linux/arm64 OK (aarch64)", text, StringComparison.Ordinal);
        Assert.Contains("Buildx build support: Warning", text, StringComparison.Ordinal);
        Assert.Contains("Container execution: OK", text, StringComparison.Ordinal);
        Assert.Contains("validated through fallback behavior using 'linux/amd64'", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatPlatformValidation_HostArm64ExecutionFailure_UsesHostSpecificSummary()
    {
        var text = CliOutputFormatter.FormatPlatformValidation(new PlatformValidationReport
        {
            WorkspaceRootPath = "/workspace",
            TargetPlatform = "linux/arm64",
            ResolvedPlatform = "linux/amd64",
            CompatibilityDisplay = "emulated fallback",
            ValidatedWithFallback = true,
            ResolvedRuntimePlan = new ResolvedRuntimePlan
            {
                Runtime = "docker",
                TargetPlatform = "linux/amd64",
                CompatibilityMode = RuntimeCompatibilityMode.Emulated,
                SupportLevel = SupportLevel.EmulatedTested,
                IsAvailable = true,
            },
            Checks =
            [
                new PlatformValidationCheckResult { Name = "Buildx build support", Severity = DiagnosticSeverity.Warning, Message = "Active builder does not advertise linux/arm64." },
                new PlatformValidationCheckResult { Name = "Compose generation", Severity = DiagnosticSeverity.Information, Message = "OK" },
                new PlatformValidationCheckResult { Name = "Provisioning generation", Severity = DiagnosticSeverity.Information, Message = "OK" },
                new PlatformValidationCheckResult { Name = "Container execution", Severity = DiagnosticSeverity.Error, Message = "This host cannot currently execute linux/arm64 containers. Enable container emulation, use a builder/runtime with linux/arm64 support, or validate on real ARM64 hardware. Technical details: exec /usr/bin/uname: exec format error" },
            ],
            IsSuccess = false,
            HasWarnings = true,
            Summary = "linux/arm64 validation failed on this host.",
        });

        Assert.Contains("Container execution: Failed", text, StringComparison.Ordinal);
        Assert.Contains("Requested target execution: linux/arm64 failed on this host", text, StringComparison.Ordinal);
        Assert.Contains("This host cannot currently execute linux/arm64 containers.", text, StringComparison.Ordinal);
        Assert.Contains("linux/arm64 validation failed on this host.", text, StringComparison.Ordinal);
    }
}
