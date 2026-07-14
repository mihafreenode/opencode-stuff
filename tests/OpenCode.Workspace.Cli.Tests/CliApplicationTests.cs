using System.Text;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Cli;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

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
    public async Task DebugWorkspaceDiscovery_PrintsReport()
    {
        var output = new StringWriter();
        var app = new CliApplication(
            output,
            new StringWriter(),
            (_, _) => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            _ => Task.FromResult(new WorkspaceLoadReport
            {
                AppDataRoot = "C:/Users/test/AppData/Local/OpenCode.Workspace.Manager",
                IndexFilePath = "C:/Users/test/AppData/Local/OpenCode.Workspace.Manager/workspaces.json",
                IndexFileExists = true,
                RawRecordCount = 9,
                SnapshotAttemptCount = 9,
                SnapshotCount = 4,
                ItemsReturnedCount = 9,
                Failures = [new WorkspaceLoadFailure("broken", "C:/broken", "workspace.yaml missing")],
            }));

        var exitCode = await app.RunAsync(["debug-workspace-discovery"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("Raw workspace record count: 9", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Returned workspace item count: 9", output.ToString(), StringComparison.Ordinal);
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
                ResolvedPlatform = "linux/amd64",
                CompatibilityDisplay = "direct",
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
                CompatibilityDisplay = "unresolved",
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

    [Fact]
    public async Task ValidatePlatform_WithOutput_WritesMarkdownReport()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cli-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var output = new StringWriter();
            var reportPath = Path.Combine(root, "report.md");
            var app = new CliApplication(output, new StringWriter(),
                (_, _) => throw new NotSupportedException(),
                (_, _) => Task.FromResult(new PlatformValidationReport
                {
                    WorkspaceRootPath = root,
                    TargetPlatform = "linux/amd64",
                    ResolvedPlatform = "linux/amd64",
                    CompatibilityDisplay = "direct",
                    Checks =
                    [
                        new PlatformValidationCheckResult { Name = "Workspace Config", Severity = DiagnosticSeverity.Information, Message = "OK" },
                        new PlatformValidationCheckResult { Name = "Container Execution", Severity = DiagnosticSeverity.Information, Message = "OK (x86_64)" },
                    ],
                    IsSuccess = true,
                    HasWarnings = false,
                    Summary = "linux/amd64 validation passed.",
                }));

            var exitCode = await app.RunAsync(["validate-platform", "--target", "linux/amd64", "--output", reportPath]);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(reportPath));
            var markdown = await File.ReadAllTextAsync(reportPath);
            Assert.Contains("# Platform Validation Report", markdown, StringComparison.Ordinal);
            Assert.Contains("linux/amd64 validation passed.", markdown, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ValidatePlatform_WithDirectoryOutputPath_ExitsNonZero()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cli-output-invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var error = new StringWriter();
            var app = new CliApplication(new StringWriter(), error,
                (_, _) => throw new NotSupportedException(),
                (_, _) => Task.FromResult(new PlatformValidationReport
                {
                    WorkspaceRootPath = root,
                    TargetPlatform = "linux/amd64",
                    ResolvedPlatform = "linux/amd64",
                    CompatibilityDisplay = "direct",
                    Checks = [new PlatformValidationCheckResult { Name = "Workspace Config", Severity = DiagnosticSeverity.Information, Message = "OK" }],
                    IsSuccess = true,
                    HasWarnings = false,
                    Summary = "linux/amd64 validation passed.",
                }));

            var exitCode = await app.RunAsync(["validate-platform", "--target", "linux/amd64", "--output", root]);

            Assert.Equal(1, exitCode);
            Assert.Contains("Access to the path", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ValidatePlatform_WithOutput_WritesFailureReport_AndReturnsNonZero()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cli-output-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var reportPath = Path.Combine(root, "failure.md");
            var app = new CliApplication(new StringWriter(), new StringWriter(),
                (_, _) => throw new NotSupportedException(),
                (_, _) => Task.FromResult(new PlatformValidationReport
                {
                    WorkspaceRootPath = root,
                    TargetPlatform = "linux/arm64",
                    ResolvedPlatform = "linux/amd64",
                    CompatibilityDisplay = "fallback",
                    ValidatedWithFallback = true,
                    Checks =
                    [
                        new PlatformValidationCheckResult { Name = "Buildx Build Support", Severity = DiagnosticSeverity.Warning, Message = "Buildx is not available." },
                        new PlatformValidationCheckResult { Name = "Container Execution", Severity = DiagnosticSeverity.Error, Message = "This host cannot currently execute linux/arm64 containers." },
                    ],
                    IsSuccess = false,
                    HasWarnings = true,
                    Summary = "linux/arm64 validation failed on this host.",
                }));

            var exitCode = await app.RunAsync(["validate-platform", "--target", "linux/arm64", "--output", reportPath]);

            Assert.Equal(1, exitCode);
            Assert.True(File.Exists(reportPath));
            var markdown = await File.ReadAllTextAsync(reportPath);
            Assert.Contains("linux/arm64 validation failed on this host.", markdown, StringComparison.Ordinal);
            Assert.Contains("Container execution details:", markdown, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SmokeCleanup_DryRun_PrintsCleanupReport()
    {
        var output = new StringWriter();
        var app = new CliApplication(
            output,
            new StringWriter(),
            (_, _) => throw new NotSupportedException(),
            (_, _) => throw new NotSupportedException(),
            _ => throw new NotSupportedException(),
            (_, _) => Task.FromResult(new SmokeCleanupResult
            {
                Succeeded = true,
                DryRun = true,
                Actions = ["compose-down:oracle-smoke"],
            }));

        var exitCode = await app.RunAsync(["smoke", "cleanup", "--dry-run"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("OpenCode Smoke Cleanup", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("compose-down:oracle-smoke", output.ToString(), StringComparison.Ordinal);
    }
}
