using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using System.Text.Json;

namespace OpenCode.Workspace.Cli;

public static class CliOutputFormatter
{
    public static string SerializeJson<T>(T model)
        => JsonSerializer.Serialize(model, WorkspaceSmokeContract.JsonOptions);

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
        if (result.RuntimeInventory is not null)
        {
            lines.Add($"  Active owned runtimes: {result.RuntimeInventory.Resources.Count}");
            lines.Add($"  Orphaned resources: {result.RuntimeInventory.Orphans.Count}");
            lines.Add($"  Stale runtimes: {result.RuntimeInventory.StaleRuntimes.Count}");
            lines.Add($"  Duplicate run ids: {result.RuntimeInventory.DuplicateRunIds.Count}");
            lines.Add($"  Missing labels: {result.RuntimeInventory.MissingRequiredLabels.Count}");
            lines.Add($"  Missing compose files: {result.RuntimeInventory.MissingComposeFiles.Count}");
            lines.Add($"  Missing workspace directories: {result.RuntimeInventory.MissingWorkspaceDirectories.Count}");
        }
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
            "  opencode runtime list --format json",
            "  opencode runtime doctor --owner smoke",
            "  opencode smoke list",
            "  opencode smoke run <template>",
            "  opencode smoke run --family <family>",
            "  opencode smoke run --all",
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

    public static string FormatSmokeCleanup(SmokeCleanupResult result, string format, CliVerbosity verbosity)
    {
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return SerializeJson(result);
        }

        if (verbosity == CliVerbosity.Quiet)
        {
            return $"status={(result.Succeeded ? "passed" : "failed")}{Environment.NewLine}cleanupVerificationSucceeded={result.VerificationSucceeded}";
        }

        var lines = new List<string>
        {
            "OpenCode Smoke Cleanup",
            string.Empty,
            $"Result: {(result.Succeeded ? "success" : "failure")}",
            $"Dry run: {result.DryRun}",
            $"Compose down attempted: {result.ComposeDownAttempted}",
            $"Compose down succeeded: {result.ComposeDownSucceeded}",
            $"Fallback removal required: {result.FallbackRemovalRequired}",
            $"Verification succeeded: {result.VerificationSucceeded}",
            $"Resources discovered: {result.Resources.Count}",
        };

        if (result.Actions.Count > 0 && verbosity == CliVerbosity.Verbose)
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

        if (result.Warnings.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Warnings:");
            lines.AddRange(result.Warnings.Select(item => $"  {item}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatLegacySmokeCleanup(LegacyCleanupResult result, string format)
    {
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return SerializeJson(result);
        }

        var lines = new List<string>
        {
            "OpenCode Legacy Smoke Cleanup",
            string.Empty,
            $"Result: {(result.Succeeded ? "success" : "failure")}",
            $"Dry run: {result.DryRun}",
            $"Projects discovered: {result.Projects.Count}",
        };

        foreach (var project in result.Projects)
        {
            lines.Add($"  {project.Project}: eligible={project.EligibleForCleanup} reason={project.Reason}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatRuntimeInventory(RuntimeResourceInventory inventory, string format, bool doctorView, CliVerbosity verbosity)
    {
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return SerializeJson(inventory);
        }

        if (verbosity == CliVerbosity.Quiet)
        {
            return $"resources={inventory.Resources.Count}{Environment.NewLine}orphans={inventory.Orphans.Count}{Environment.NewLine}staleRuntimes={inventory.StaleRuntimes.Count}";
        }

        var lines = new List<string>
        {
            doctorView ? "OpenCode Runtime Doctor" : "OpenCode Runtime Inventory",
            string.Empty,
            $"Resources: {inventory.Resources.Count}",
            $"Projects: {inventory.Projects.Count}",
            $"Orphans: {inventory.Orphans.Count}",
            $"Stale runtimes: {inventory.StaleRuntimes.Count}",
            $"Duplicate run ids: {inventory.DuplicateRunIds.Count}",
            $"Missing labels: {inventory.MissingRequiredLabels.Count}",
        };

        foreach (var resource in inventory.Resources.Take(verbosity == CliVerbosity.Verbose ? 50 : 20))
        {
            lines.Add($"  {resource.Type}: {resource.Name} owner={resource.OwnerKind} run_id={resource.RunId} project={resource.Project} status={resource.Status}");
        }

        if (doctorView && verbosity == CliVerbosity.Verbose)
        {
            foreach (var issue in inventory.Orphans.Concat(inventory.StaleRuntimes).Concat(inventory.DuplicateRunIds).Concat(inventory.MissingRequiredLabels).Take(20))
            {
                lines.Add($"  issue: {issue.Kind} {issue.Message}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatSmokeDefinitions(WorkspaceSmokeDefinitionCatalogResult catalog, string format)
    {
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return SerializeJson(catalog);
        }

        var lines = new List<string>
        {
            "OpenCode Smoke Templates",
            string.Empty,
        };

        foreach (var definition in catalog.Definitions)
        {
            lines.Add($"{definition.TemplateId}: family={definition.Family} supported={definition.Supported} resource_class={definition.ResourceClass} timeout_class={definition.TimeoutClass}");
            lines.Add($"  validators={string.Join(", ", definition.ValidatorIds)}");
            if (!definition.Supported && !string.IsNullOrWhiteSpace(definition.UnsupportedReason))
            {
                lines.Add($"  reason={definition.UnsupportedReason}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatSmokeResult(WorkspaceSmokeResult result, string format, CliVerbosity verbosity)
    {
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return SerializeJson(result);
        }

        if (verbosity == CliVerbosity.Quiet)
        {
            return string.Join(Environment.NewLine, new[]
            {
                $"status={result.Status}",
                $"runId={result.RunId}",
                $"artifactDirectory={result.ArtifactDirectory}",
            });
        }

        var lines = new List<string>
        {
            "OpenCode Smoke Run",
            string.Empty,
            $"Template: {result.TemplateId}",
            $"Run id: {result.RunId}",
            $"Status: {result.Status}",
            $"Phase: {result.Phase}",
            $"Failure classification: {result.FailureClassification}",
            $"Failure message: {result.FailureMessage}",
            $"Cleanup verification: {result.CleanupVerificationSucceeded}",
            $"Artifacts: {result.ArtifactDirectory}",
            $"Summary JSON: {result.SummaryJsonPath}",
            $"Summary text: {result.SummaryTextPath}",
        };

        if (verbosity == CliVerbosity.Verbose && result.Validators.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Validators:");
            lines.AddRange(result.Validators.Select(item => $"  {item.ValidatorId}: {(item.Succeeded ? "pass" : "fail")} {item.Message}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatSmokeMatrixResult(WorkspaceSmokeMatrixResult result, string format, CliVerbosity verbosity)
    {
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return SerializeJson(result);
        }

        if (verbosity == CliVerbosity.Quiet)
        {
            return string.Join(Environment.NewLine, new[]
            {
                $"status={result.Status}",
                $"matrixRunId={result.MatrixRunId}",
                $"artifactDirectory={result.ArtifactDirectory}",
            });
        }

        var lines = new List<string>
        {
            "OpenCode Smoke Matrix",
            string.Empty,
            $"Matrix run id: {result.MatrixRunId}",
            $"Status: {result.Status}",
            $"Passed: {result.PassedCount}",
            $"Failed: {result.FailedCount}",
            $"Skipped: {result.SkippedCount}",
            $"Artifacts: {result.ArtifactDirectory}",
            $"Summary JSON: {result.SummaryJsonPath}",
            $"Summary text: {result.SummaryTextPath}",
        };

        foreach (var item in result.Results.Take(verbosity == CliVerbosity.Verbose ? result.Results.Count : 20))
        {
            lines.Add($"  {item.TemplateId}: {item.Status} {item.FailureClassification} {item.FailureMessage}".TrimEnd());
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

public enum CliVerbosity
{
    Default,
    Quiet,
    Verbose,
}
