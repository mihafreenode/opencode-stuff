using OpenCode.Workspace.Cli;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Cli.Tests;

public sealed class ExampleOutputSynchronizationTests
{
    [Fact]
    public void DoctorLinuxX64Example_IsSynchronized()
    {
        var actual = CliOutputFormatter.FormatDoctor(new WorkspaceDoctorResult
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
                    DiagnosticSummary = "Docker CLI available: Docker version 29.1.3, build 29.1.3-0ubuntu3~24.04.2\nDocker engine reachable.\nDocker Buildx available for linux/amd64, linux/arm64.",
                },
            },
            WorkspaceConfigurationStatus = WorkspaceConfigurationStatus.Found,
            WorkspaceConfigurationPath = "workspace.yaml",
            RuntimeStateStatus = WorkspaceRuntimeStateReadStatus.Loaded,
            RuntimeState = new WorkspaceRuntimeStateRecord
            {
                ResolvedEngine = "docker",
                ResolvedPlatform = "linux/amd64",
                CompatibilityMode = "Native",
            },
            Arm64ExecutionSupportStatus = Arm64ExecutionSupportStatus.Available,
            Arm64ExecutionSupportDetails = "Execution probe OK (aarch64)",
            ResolvedRuntimePlan = new ResolvedRuntimePlan
            {
                Runtime = "docker",
                TargetPlatform = "linux/amd64",
                CompatibilityMode = RuntimeCompatibilityMode.Native,
                SupportLevel = SupportLevel.NativeTested,
                IsAvailable = true,
                HostPlatform = new HostPlatformInfo { NativeContainerPlatform = "linux/amd64" },
            },
            CanRun = true,
            Recommendation = "Workspace can run on this machine.",
        });

        AssertExample("docs/examples/doctor-linux-x64.txt", actual);
    }

    [Fact]
    public void ValidateLinuxAmd64Example_IsSynchronized()
    {
        var actual = CliOutputFormatter.FormatPlatformValidation(new PlatformValidationReport
        {
            WorkspaceRootPath = "/workspace",
            TargetPlatform = "linux/amd64",
            ResolvedPlatform = "linux/amd64",
            CompatibilityDisplay = "direct",
            Checks =
            [
                new PlatformValidationCheckResult { Name = "Runtime resolution", Severity = DiagnosticSeverity.Information, Message = "OK (docker resolved linux/amd64 directly)" },
                new PlatformValidationCheckResult { Name = "Buildx build support", Severity = DiagnosticSeverity.Information, Message = "OK" },
                new PlatformValidationCheckResult { Name = "Compose generation", Severity = DiagnosticSeverity.Information, Message = "OK" },
                new PlatformValidationCheckResult { Name = "Provisioning generation", Severity = DiagnosticSeverity.Information, Message = "OK" },
                new PlatformValidationCheckResult { Name = "Container execution", Severity = DiagnosticSeverity.Information, Message = "OK (x86_64)" },
            ],
            ResolvedRuntimePlan = new ResolvedRuntimePlan { Runtime = "docker", TargetPlatform = "linux/amd64", CompatibilityMode = RuntimeCompatibilityMode.Native, IsAvailable = true },
            IsSuccess = true,
            Summary = "linux/amd64 validation passed.",
        });

        AssertExample("docs/examples/validate-linux-amd64.txt", actual);
    }

    [Fact]
    public void ValidateLinuxArm64FailedExample_IsSynchronized()
    {
        var actual = CliOutputFormatter.FormatPlatformValidation(new PlatformValidationReport
        {
            WorkspaceRootPath = "/workspace",
            TargetPlatform = "linux/arm64",
            ResolvedPlatform = "linux/amd64",
            CompatibilityDisplay = "emulated fallback",
            ValidatedWithFallback = true,
            Checks =
            [
                new PlatformValidationCheckResult { Name = "Runtime resolution", Severity = DiagnosticSeverity.Information, Message = "OK (docker resolved linux/arm64 through fallback to linux/amd64)" },
                new PlatformValidationCheckResult { Name = "Buildx build support", Severity = DiagnosticSeverity.Warning, Message = "Active builder does not advertise linux/arm64. Native validation may still be possible on target hardware." },
                new PlatformValidationCheckResult { Name = "Compose generation", Severity = DiagnosticSeverity.Information, Message = "OK" },
                new PlatformValidationCheckResult { Name = "Provisioning generation", Severity = DiagnosticSeverity.Information, Message = "OK" },
                new PlatformValidationCheckResult { Name = "Container execution", Severity = DiagnosticSeverity.Error, Message = "This host cannot currently execute linux/arm64 containers.\n\nPossible fixes:\n- Install ARM64 emulation support\n- Configure a Buildx builder with linux/arm64 support\n- Validate on real ARM64 hardware\nTechnical details: exec /usr/bin/uname: exec format error" },
            ],
            ResolvedRuntimePlan = new ResolvedRuntimePlan { Runtime = "docker", TargetPlatform = "linux/amd64", CompatibilityMode = RuntimeCompatibilityMode.Emulated, IsAvailable = true },
            IsSuccess = false,
            HasWarnings = true,
            Summary = "linux/arm64 validation failed on this host.",
        });

        AssertExample("docs/examples/validate-linux-arm64-failed.txt", actual);
    }

    [Fact]
    public void ValidateLinuxArm64PassedExample_IsSynchronized()
    {
        var actual = CliOutputFormatter.FormatPlatformValidation(new PlatformValidationReport
        {
            WorkspaceRootPath = "/workspace",
            TargetPlatform = "linux/arm64",
            ResolvedPlatform = "linux/amd64",
            CompatibilityDisplay = "emulated fallback",
            ValidatedWithFallback = true,
            Checks =
            [
                new PlatformValidationCheckResult { Name = "Runtime resolution", Severity = DiagnosticSeverity.Information, Message = "OK (docker resolved linux/arm64 through fallback to linux/amd64)" },
                new PlatformValidationCheckResult { Name = "Buildx build support", Severity = DiagnosticSeverity.Warning, Message = "Active builder does not advertise linux/arm64. Native validation may still be possible on target hardware." },
                new PlatformValidationCheckResult { Name = "Compose generation", Severity = DiagnosticSeverity.Information, Message = "OK" },
                new PlatformValidationCheckResult { Name = "Provisioning generation", Severity = DiagnosticSeverity.Information, Message = "OK" },
                new PlatformValidationCheckResult { Name = "Container execution", Severity = DiagnosticSeverity.Information, Message = "OK (aarch64)" },
            ],
            ResolvedRuntimePlan = new ResolvedRuntimePlan { Runtime = "docker", TargetPlatform = "linux/amd64", CompatibilityMode = RuntimeCompatibilityMode.Emulated, IsAvailable = true },
            IsSuccess = true,
            HasWarnings = true,
            Summary = "linux/arm64 validation completed with warnings.",
        });

        AssertExample("docs/examples/validate-linux-arm64-passed.txt", actual);
    }

    private static void AssertExample(string relativePath, string actual)
    {
        var fullPath = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        var expected = File.ReadAllText(fullPath).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        Assert.Equal(expected, actual.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n'));
    }

    private static string RepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(Path.Combine(current, "docs")) && Directory.Exists(Path.Combine(current, "src")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current);
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
