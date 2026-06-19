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
        foreach (var detail in FormatDiagnosticSummary(docker?.DiagnosticSummary))
        {
            lines.Add($"  Detail: {detail}");
        }
        lines.Add(string.Empty);
        lines.Add("Workspace:");
        lines.Add($"  workspace.yaml: {FormatWorkspaceConfigStatus(result)}");
        lines.Add($"  Local runtime state: {FormatRuntimeStateStatus(result.RuntimeStateStatus)}");
        lines.Add($"  Local runtime state path: {FormatPath(result.WorkspaceRootPath, result.RuntimeStatePath)}");
        lines.Add(string.Empty);
        lines.Add("Resolution:");
        lines.Add($"  Runtime: {result.ResolvedRuntimePlan?.Runtime ?? "unresolved"}");
        lines.Add($"  Target platform: {result.ResolvedRuntimePlan?.TargetPlatform ?? "unresolved"}");
        lines.Add($"  Compatibility: {FormatCompatibility(result.ResolvedRuntimePlan)}");
        lines.Add(string.Empty);
        lines.Add("Result:");
        lines.Add($"  {FormatConclusion(result)}");

        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatPlatformValidation(PlatformValidationReport report)
    {
        var lines = new List<string>
        {
            "OpenCode Platform Validation",
            string.Empty,
            $"Requested target: {report.TargetPlatform}",
            $"Resolved workspace platform: {report.ResolvedPlatform ?? report.ResolvedRuntimePlan?.TargetPlatform ?? "unresolved"}",
            $"Compatibility: {report.CompatibilityDisplay ?? FormatCompatibility(report.ResolvedRuntimePlan)}",
            $"Requested target execution: {FormatRequestedTargetExecution(report)}",
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
        if (report.ResolvedRuntimePlan is not null)
        {
            lines.Add(report.ValidatedWithFallback
                ? $"  Requested target '{report.TargetPlatform}' was validated through fallback behavior using '{report.ResolvedPlatform ?? report.ResolvedRuntimePlan.TargetPlatform}'."
                : $"  Requested target '{report.TargetPlatform}' was validated directly.");
        }
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

    private static IEnumerable<string> FormatDiagnosticSummary(string? diagnosticSummary)
    {
        if (string.IsNullOrWhiteSpace(diagnosticSummary))
        {
            yield break;
        }

        foreach (var segment in diagnosticSummary.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(segment))
            {
                yield return segment.Trim();
            }
        }
    }

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

    private static string FormatCompatibility(ResolvedRuntimePlan? plan)
    {
        if (plan is null)
        {
            return "unresolved";
        }

        if (plan.IsAvailable && string.Equals(plan.TargetPlatform, plan.HostPlatform.NativeContainerPlatform, StringComparison.OrdinalIgnoreCase))
        {
            return "native";
        }

        return plan.CompatibilityMode switch
        {
            RuntimeCompatibilityMode.MultiArchitecture => "compatible multi-architecture",
            RuntimeCompatibilityMode.Emulated => "emulated",
            RuntimeCompatibilityMode.Unavailable => "unavailable",
            RuntimeCompatibilityMode.Native => "native",
            _ => "unresolved",
        };
    }

    private static string FormatConclusion(WorkspaceDoctorResult result)
    {
        if (result.CanRun)
        {
            return "Workspace can run on this machine.";
        }

        return result.Recommendation;
    }

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

    private static string FormatRequestedTargetExecution(PlatformValidationReport report)
    {
        var executionCheck = report.Checks.FirstOrDefault(check => string.Equals(check.Name, "Container execution", StringComparison.Ordinal));
        if (executionCheck is null)
        {
            return "not probed";
        }

        return executionCheck.Severity switch
        {
            DiagnosticSeverity.Information => string.IsNullOrWhiteSpace(executionCheck.Message)
                ? $"{report.TargetPlatform} OK"
                : $"{report.TargetPlatform} {executionCheck.Message}",
            DiagnosticSeverity.Warning => $"{report.TargetPlatform} warning",
            DiagnosticSeverity.Error => $"{report.TargetPlatform} failed on this host",
            _ => "unresolved",
        };
    }

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
