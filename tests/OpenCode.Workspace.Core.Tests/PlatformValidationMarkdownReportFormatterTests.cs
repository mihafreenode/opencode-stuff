using OpenCode.Workspace.Core.Diagnostics;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Tests;

public sealed class PlatformValidationMarkdownReportFormatterTests
{
    [Fact]
    public void Format_GeneratesAmd64MarkdownReport()
    {
        var markdown = PlatformValidationMarkdownReportFormatter.Format(CreateReport(
            targetPlatform: "linux/amd64",
            resolvedPlatform: "linux/amd64",
            compatibility: "direct",
            checks:
            [
                Info("Workspace Config", "OK"),
                Info("Runtime Resolution", "OK"),
                Info("Container Execution", "OK (x86_64)"),
            ],
            summary: "linux/amd64 validation passed."));

        Assert.Contains("# Platform Validation Report", markdown, StringComparison.Ordinal);
        Assert.Contains("Requested Target: linux/amd64", markdown, StringComparison.Ordinal);
        Assert.Contains("Resolved Platform: linux/amd64", markdown, StringComparison.Ordinal);
        Assert.Contains("Compatibility: direct", markdown, StringComparison.Ordinal);
        Assert.Contains("| Workspace Config | OK |", markdown, StringComparison.Ordinal);
        Assert.Contains("Container execution succeeded:", markdown, StringComparison.Ordinal);
        Assert.Contains("x86_64", markdown, StringComparison.Ordinal);
        Assert.Contains("linux/amd64 validation passed.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_GeneratesArm64FallbackMarkdownReport()
    {
        var markdown = PlatformValidationMarkdownReportFormatter.Format(CreateReport(
            targetPlatform: "linux/arm64",
            resolvedPlatform: "linux/amd64",
            compatibility: "fallback",
            validatedWithFallback: true,
            checks:
            [
                Info("Workspace Config", "OK"),
                Info("Runtime Resolution", "OK"),
                Warning("Buildx Build Support", "Active builder does not advertise linux/arm64."),
                Info("Container Execution", "OK (aarch64)"),
                Info("Compose Generation", "OK"),
                Info("Provisioning Generation", "OK"),
            ],
            summary: "linux/arm64 validation completed with warnings."));

        Assert.Contains("Requested target was validated through fallback behavior using linux/amd64.", markdown, StringComparison.Ordinal);
        Assert.Contains("| Buildx Build Support | Warning |", markdown, StringComparison.Ordinal);
        Assert.Contains("Container execution succeeded:", markdown, StringComparison.Ordinal);
        Assert.Contains("aarch64", markdown, StringComparison.Ordinal);
        Assert.Contains("linux/arm64 validation completed with warnings.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_PreservesWarningAndFailureNotes()
    {
        var markdown = PlatformValidationMarkdownReportFormatter.Format(CreateReport(
            targetPlatform: "linux/arm64",
            resolvedPlatform: "linux/amd64",
            compatibility: "fallback",
            validatedWithFallback: true,
            checks:
            [
                Warning("Buildx Build Support", "Buildx is not available."),
                Error("Container Execution", "This host cannot currently execute linux/arm64 containers."),
            ],
            summary: "linux/arm64 validation failed on this host."));

        Assert.Contains("## Notes", markdown, StringComparison.Ordinal);
        Assert.Contains("Buildx Build Support: Buildx is not available.", markdown, StringComparison.Ordinal);
        Assert.Contains("Container execution details:", markdown, StringComparison.Ordinal);
        Assert.Contains("This host cannot currently execute linux/arm64 containers.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_IsStableForFallbackScenario()
    {
        var markdown = PlatformValidationMarkdownReportFormatter.Format(CreateReport(
            targetPlatform: "linux/arm64",
            resolvedPlatform: "linux/amd64",
            compatibility: "fallback",
            validatedWithFallback: true,
            checks:
            [
                Info("Workspace Config", "OK"),
                Info("Runtime Resolution", "OK"),
                Warning("Buildx Build Support", "Active builder does not advertise linux/arm64."),
                Info("Container Execution", "OK (aarch64)"),
                Info("Compose Generation", "OK"),
                Info("Provisioning Generation", "OK"),
            ],
            summary: "linux/arm64 validation completed with warnings."));

        const string expected = "# Platform Validation Report\n\nRequested Target: linux/arm64\nResolved Platform: linux/amd64\nCompatibility: fallback\n\n## Checks\n\n| Check | Status |\n| --- | --- |\n| Workspace Config | OK |\n| Runtime Resolution | OK |\n| Buildx Build Support | Warning |\n| Container Execution | OK |\n| Compose Generation | OK |\n| Provisioning Generation | OK |\n\n## Notes\n\nRequested target was validated through fallback behavior using linux/amd64.\n\nContainer execution succeeded:\naarch64\n\nBuildx Build Support: Active builder does not advertise linux/arm64.\n\n## Result\n\nlinux/arm64 validation completed with warnings.";
        Assert.Equal(expected.Replace("\n", Environment.NewLine, StringComparison.Ordinal), markdown);
    }

    [Fact]
    public void GetDefaultOutputPath_UsesPlatformValidationArtifactsConvention()
    {
        var outputPath = PlatformValidationMarkdownReportFormatter.GetDefaultOutputPath("/workspace/demo", "linux/arm64");
        Assert.Equal(Path.Combine("/workspace/demo", "artifacts", "platform-validation", "linux-arm64.md"), outputPath);
    }

    private static PlatformValidationReport CreateReport(string targetPlatform, string resolvedPlatform, string compatibility, IReadOnlyList<PlatformValidationCheckResult> checks, string summary, bool validatedWithFallback = false)
        => new()
        {
            WorkspaceRootPath = "/workspace",
            TargetPlatform = targetPlatform,
            ResolvedPlatform = resolvedPlatform,
            CompatibilityDisplay = compatibility,
            ValidatedWithFallback = validatedWithFallback,
            Checks = checks,
            IsSuccess = !checks.Any(check => check.Severity == DiagnosticSeverity.Error),
            HasWarnings = checks.Any(check => check.Severity == DiagnosticSeverity.Warning),
            Summary = summary,
        };

    private static PlatformValidationCheckResult Info(string name, string message) => new() { Name = name, Severity = DiagnosticSeverity.Information, Message = message };
    private static PlatformValidationCheckResult Warning(string name, string message) => new() { Name = name, Severity = DiagnosticSeverity.Warning, Message = message };
    private static PlatformValidationCheckResult Error(string name, string message) => new() { Name = name, Severity = DiagnosticSeverity.Error, Message = message };
}
