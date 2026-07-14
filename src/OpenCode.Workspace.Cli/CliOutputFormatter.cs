using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using System.Text.Json;

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
        lines.Add($"  ARM64 execution support: {FormatArm64ExecutionSupport(result.Arm64ExecutionSupportStatus)}");
        if (!string.IsNullOrWhiteSpace(result.Arm64ExecutionSupportDetails))
        {
            lines.Add($"  ARM64 detail: {result.Arm64ExecutionSupportDetails}");
        }
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
                foreach (var messageLine in SplitMessageLines(check.Message))
                {
                    lines.Add(string.IsNullOrWhiteSpace(messageLine)
                        ? string.Empty
                        : $"    {messageLine}");
                }
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
            "  opencode debug-workspace-discovery",
            "  opencode smoke cleanup --dry-run",
            "  opencode smoke cleanup --all",
            "  opencode smoke cleanup --run-id <run-id>",
            "  opencode smoke cleanup --format json",
            "  opencode validate-platform --target linux/amd64",
            "  opencode validate-platform --target linux/arm64",
            "  opencode validate-platform --workspace <path> --target linux/arm64",
            "  opencode validate-platform --target linux/arm64 --output report.md",
            "  opencode --help",
        });
    }

    public static string FormatSmokeCleanup(SmokeCleanupResult result, string format)
    {
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new
            {
                result.Succeeded,
                result.DryRun,
                Resources = result.Resources,
                Actions = result.Actions,
                Errors = result.Errors,
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        var lines = new List<string>
        {
            "OpenCode Smoke Cleanup",
            string.Empty,
            $"Result: {(result.Succeeded ? "success" : "failure")}",
            $"Dry run: {result.DryRun}",
            $"Resources discovered: {result.Resources.Count}",
        };

        if (result.Actions.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Actions:");
            lines.AddRange(result.Actions.Select(item => $"  {item}"));
        }

        if (result.Errors.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Errors:");
            lines.AddRange(result.Errors.Select(item => $"  {item}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatWorkspaceDiscovery(WorkspaceLoadReport report)
    {
        var lines = new List<string>
        {
            "OpenCode Workspace Discovery",
            string.Empty,
            $"App data directory: {report.AppDataRoot}",
            $"Workspace index path: {report.IndexFilePath}",
            $"Index file exists: {report.IndexFileExists}",
            $"Total discovery time: {FormatDuration(report.TotalDuration)}",
            $"Raw workspace record count: {report.RawRecordCount}",
            $"Snapshot attempts: {report.SnapshotAttemptCount}",
            $"Snapshot successes: {report.SnapshotCount}",
            $"Snapshot failures: {report.FailureCount}",
            $"Returned workspace item count: {report.ItemsReturnedCount}",
        };

        if (report.SlowestTiming is not null)
        {
            lines.Add($"Slowest stage: {report.SlowestTiming.StageLabel} ({report.SlowestTiming.WorkspaceName}) in {FormatDuration(report.SlowestTiming.Duration)}");
        }

        if (report.Timings.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Stage timings:");
            foreach (var timing in report.Timings.OrderByDescending(item => item.Duration).Take(8))
            {
                var scope = string.IsNullOrWhiteSpace(timing.WorkspaceName) ? timing.StageLabel : $"{timing.WorkspaceName} - {timing.StageLabel}";
                var outcome = timing.Succeeded ? string.Empty : $" (failed: {timing.FailureMessage})";
                lines.Add($"  {scope}: {FormatDuration(timing.Duration)}{outcome}");
            }
        }

        if (report.Failures.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Failure summaries:");
            foreach (var failure in report.Failures)
            {
                lines.Add($"  {failure.DisplayName}: {failure.Reason}");
                lines.Add($"    {failure.RootPath}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalMilliseconds >= 1000
            ? $"{duration.TotalSeconds:F1} s"
            : $"{Math.Max(1, duration.TotalMilliseconds):F0} ms";

    private static string FormatAvailability(bool? available)
        => available switch
        {
            true => "available",
            false => "unavailable",
            null => "unknown",
        };

    private static string FormatArm64ExecutionSupport(Arm64ExecutionSupportStatus status)
        => status switch
        {
            Arm64ExecutionSupportStatus.Available => "available",
            Arm64ExecutionSupportStatus.Unavailable => "unavailable",
            _ => "unknown",
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

    private static IEnumerable<string> SplitMessageLines(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            yield break;
        }

        foreach (var line in message.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            yield return line;
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
