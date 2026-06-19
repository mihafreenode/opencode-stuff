using System.Text;
using OpenCode.Workspace.Cli;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Cli.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task Help_PrintsHelpText()
    {
        var output = new StringWriter(new StringBuilder());
        var error = new StringWriter(new StringBuilder());
        var app = new CliApplication(output, error);

        var exitCode = await app.RunAsync(["--help"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("opencode doctor", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownCommand_ExitsNonZero()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var app = new CliApplication(output, error);

        var exitCode = await app.RunAsync(["unknown"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown command", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Doctor_ExitsZeroWhenDiagnosticsHaveWarnings()
    {
        var output = new StringWriter();
        var app = new CliApplication(output, new StringWriter(),
            (_, _) => Task.FromResult(new WorkspaceDoctorResult
            {
                WorkspaceRootPath = Environment.CurrentDirectory,
                RuntimeStatePath = Path.Combine(Environment.CurrentDirectory, ".opencode", "local", "runtime-state.yaml"),
                HostPlatform = new HostPlatformInfo
                {
                    OperatingSystem = HostOperatingSystem.Windows,
                    Architecture = HostArchitecture.X64,
                    Docker = new ContainerRuntimeAvailability { EngineId = "docker", CliAvailable = true, EngineReachable = false, BuildxAvailable = false },
                },
                WorkspaceConfigurationStatus = WorkspaceConfigurationStatus.Found,
                WorkspaceConfigurationPath = "workspace.yaml",
                RuntimeStateStatus = WorkspaceRuntimeStateReadStatus.Corrupted,
                ResolvedRuntimePlan = new ResolvedRuntimePlan
                {
                    Runtime = "docker",
                    TargetPlatform = "linux/amd64",
                    CompatibilityMode = RuntimeCompatibilityMode.Unavailable,
                    SupportLevel = SupportLevel.Unavailable,
                    IsAvailable = false,
                    DiagnosticExplanation = "Docker engine is not reachable.",
                },
                CanRun = false,
                Recommendation = "Docker engine is not reachable. Start Docker Desktop or install a compatible Docker runtime.",
            }),
            (_, _) => throw new NotSupportedException());

        var exitCode = await app.RunAsync(["doctor"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("Docker Engine: unavailable", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidatePlatform_Success_ExitsZero()
    {
        var output = new StringWriter();
        var app = new CliApplication(output, new StringWriter(),
            (_, _) => throw new NotSupportedException(),
            (_, _) => Task.FromResult(new PlatformValidationReport
            {
                WorkspaceRootPath = Environment.CurrentDirectory,
                TargetPlatform = "linux/amd64",
                Checks = [new PlatformValidationCheckResult { Name = "Workspace config", Severity = DiagnosticSeverity.Information, Message = "OK" }],
                IsSuccess = true,
                HasWarnings = false,
                Summary = "linux/amd64 validation passed.",
            }));

        var exitCode = await app.RunAsync(["validate-platform", "--target", "linux/amd64"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("validation passed", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidatePlatform_InvalidTarget_ExitsNonZero()
    {
        var output = new StringWriter();
        var app = new CliApplication(output, new StringWriter(),
            (_, _) => throw new NotSupportedException(),
            (_, _) => Task.FromResult(new PlatformValidationReport
            {
                WorkspaceRootPath = Environment.CurrentDirectory,
                TargetPlatform = "invalid",
                Checks = [new PlatformValidationCheckResult { Name = "Target", Severity = DiagnosticSeverity.Error, Message = "Unsupported target." }],
                IsSuccess = false,
                HasWarnings = false,
                Summary = "invalid validation failed.",
            }));

        var exitCode = await app.RunAsync(["validate-platform", "--target", "invalid"]);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ValidatePlatform_WithoutTarget_ExitsNonZero()
    {
        var error = new StringWriter();
        var app = new CliApplication(new StringWriter(), error,
            (_, _) => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException());

        var exitCode = await app.RunAsync(["validate-platform"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Missing required option --target", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
