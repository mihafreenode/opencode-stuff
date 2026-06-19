using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Cli;

public static class CliOutputFormatter
{
    public static string FormatDoctor(WorkspaceDoctorResult result)
    {
        var lines = new List<string>
        {
            "OpenCode Doctor",
            string.Empty,
            "Host:",
            $"  OS: {result.HostPlatform?.OperatingSystem.ToString() ?? "Unknown"}",
            $"  Architecture: {FormatArchitecture(result.HostPlatform?.Architecture)}",
            string.Empty,
            "Container Runtime:",
        };

        var docker = result.HostPlatform?.Docker;
        lines.Add($"  Docker CLI: {FormatAvailability(docker?.CliAvailable)}");
        lines.Add($"  Docker Engine: {FormatAvailability(docker?.EngineReachable)}");
        lines.Add($"  Buildx: {FormatAvailability(docker?.BuildxAvailable)}");
        lines.Add($"  Platforms: {FormatPlatforms(docker?.SupportedPlatforms)}");
        lines.Add(string.Empty);
        lines.Add("Workspace:");
        lines.Add($"  workspace.yaml: {FormatWorkspaceConfigStatus(result)}");
        lines.Add($"  Local runtime state: {FormatRuntimeStateStatus(result.RuntimeStateStatus)}");
        lines.Add($"  Local runtime state path: {FormatPath(result.WorkspaceRootPath, result.RuntimeStatePath)}");
        lines.Add(string.Empty);
        lines.Add("Resolution:");
        lines.Add($"  Runtime: {result.ResolvedRuntimePlan?.Runtime ?? "unresolved"}");
        lines.Add($"  Target platform: {result.ResolvedRuntimePlan?.TargetPlatform ?? "unresolved"}");
        lines.Add($"  Compatibility: {FormatCompatibility(result.ResolvedRuntimePlan?.CompatibilityMode)}");
        lines.Add(string.Empty);
        lines.Add("Result:");
        lines.Add($"  {result.Recommendation}");

        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatPlatformValidation(PlatformValidationReport report)
    {
        var lines = new List<string>
        {
            "OpenCode Platform Validation",
            string.Empty,
            $"Target: {report.TargetPlatform}",
            string.Empty,
            "Checks:",
        };

        foreach (var check in report.Checks)
        {
            lines.Add($"  {check.Name}: {FormatCheckSeverity(check.Severity)}");
            if (!string.IsNullOrWhiteSpace(check.Message) && !string.Equals(check.Message, "OK", StringComparison.Ordinal))
            {
                lines.Add($"    {check.Message}");
            }
        }

        lines.Add(string.Empty);
        lines.Add("Result:");
        lines.Add($"  {report.Summary}");
        return string.Join(Environment.NewLine, lines);
    }

    public static string HelpText()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "OpenCode CLI",
            string.Empty,
            "Usage:",
            "  opencode doctor",
            "  opencode doctor --workspace <path>",
            "  opencode validate-platform --target linux/amd64",
            "  opencode validate-platform --target linux/arm64",
            "  opencode validate-platform --workspace <path> --target linux/arm64",
            "  opencode --help",
        });
    }

    private static string FormatAvailability(bool? available)
        => available switch
        {
            true => "available",
            false => "unavailable",
            null => "unknown",
        };

    private static string FormatPlatforms(IReadOnlyList<string>? platforms)
        => platforms is { Count: > 0 } ? string.Join(", ", platforms) : "none reported";

    private static string FormatWorkspaceConfigStatus(WorkspaceDoctorResult result)
        => result.WorkspaceConfigurationStatus switch
        {
            WorkspaceConfigurationStatus.Found => "found",
            WorkspaceConfigurationStatus.Invalid => $"invalid ({result.WorkspaceConfigurationError})",
            _ => "missing",
        };

    private static string FormatRuntimeStateStatus(WorkspaceRuntimeStateReadStatus status)
        => status switch
        {
            WorkspaceRuntimeStateReadStatus.Loaded => "found",
            WorkspaceRuntimeStateReadStatus.Corrupted => "corrupted (ignored)",
            _ => "missing",
        };

    private static string FormatCompatibility(RuntimeCompatibilityMode? mode)
        => mode?.ToString().ToLowerInvariant() ?? "unresolved";

    private static string FormatArchitecture(HostArchitecture? architecture)
        => architecture switch
        {
            HostArchitecture.X64 => "x64",
            HostArchitecture.Arm64 => "arm64",
            _ => "unknown",
        };

    private static string FormatCheckSeverity(DiagnosticSeverity severity)
        => severity switch
        {
            DiagnosticSeverity.Error => "Failed",
            DiagnosticSeverity.Warning => "Warning",
            _ => "OK",
        };

    private static string FormatPath(string workspaceRootPath, string path)
    {
        try
        {
            return Path.GetRelativePath(workspaceRootPath, path).Replace('\\', '/');
        }
        catch
        {
            return path;
        }
    }
}
