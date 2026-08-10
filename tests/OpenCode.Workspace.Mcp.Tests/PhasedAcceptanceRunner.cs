using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCode.Workspace.Mcp.Tests;

internal static class AcceptancePhaseIds
{
    public const string Provision = "provision";
    public const string DiscoverConnect = "discover-connect";
    public const string AssistantImportRollbackPull = "assistant-import-rollback-pull";
    public const string CompilerDrivenRepair = "compiler-driven-repair";
    public const string Cleanup = "cleanup";

    public static IReadOnlyList<string> Required { get; } = new ReadOnlyCollection<string>(
    [
        Provision,
        DiscoverConnect,
        AssistantImportRollbackPull,
        CompilerDrivenRepair,
        Cleanup,
    ]);
}

internal interface IPhasedAcceptanceDriver
{
    AcceptancePackageProvenanceEvidence? PackageProvenance { get; }
    string? McpStartupDetail { get; }
    int ProvisioningCount { get; }
    IReadOnlyList<string> CleanupStepIds { get; }

    Task StartMcpAsync(CancellationToken cancellationToken);
    Task ExecutePhaseAsync(string phaseId, CancellationToken cancellationToken);
    Task ExecuteCleanupStepAsync(string stepId, CancellationToken cancellationToken);
    Task ShutdownMcpAsync(CancellationToken cancellationToken);
    Task WriteEvidenceAsync(string json, CancellationToken cancellationToken);
}

internal sealed class PhasedAcceptanceRunner(IPhasedAcceptanceDriver driver, TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan CleanupStepTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan McpShutdownTimeout = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<AcceptanceRunEvidence> RunAsync(CancellationToken cancellationToken = default)
    {
        var evidence = new AcceptanceRunEvidence
        {
            StartedAt = _timeProvider.GetUtcNow(),
            PackageProvenance = driver.PackageProvenance,
            Phases = AcceptancePhaseIds.Required.Select(id => new AcceptancePhaseEvidence { Id = id }).ToArray(),
        };
        Exception? primaryFailure = null;

        try
        {
            try
            {
                await driver.StartMcpAsync(cancellationToken);
                evidence.McpStartup = new AcceptanceMcpStartupEvidence
                {
                    Status = AcceptanceEvidenceStatus.Passed,
                    Detail = driver.McpStartupDetail,
                };
            }
            catch (Exception exception)
            {
                evidence.McpStartup = new AcceptanceMcpStartupEvidence
                {
                    Status = AcceptanceEvidenceStatus.Failed,
                    Detail = driver.McpStartupDetail,
                    Failure = AcceptanceFailureEvidence.From("mcp-startup", exception),
                };
                throw;
            }

            foreach (var phase in evidence.Phases.Where(item => item.Id != AcceptancePhaseIds.Cleanup))
            {
                phase.Status = AcceptanceEvidenceStatus.Running;
                phase.StartedAt = _timeProvider.GetUtcNow();
                try
                {
                    await driver.ExecutePhaseAsync(phase.Id, cancellationToken);
                    phase.Status = AcceptanceEvidenceStatus.Passed;
                }
                catch (Exception exception)
                {
                    phase.Status = AcceptanceEvidenceStatus.Failed;
                    phase.Failure = AcceptanceFailureEvidence.From(phase.Id, exception);
                    throw;
                }
                finally
                {
                    phase.CompletedAt = _timeProvider.GetUtcNow();
                }
            }
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            evidence.PrimaryFailure = AcceptanceFailureEvidence.From(
                evidence.Phases.FirstOrDefault(item => item.Status == AcceptanceEvidenceStatus.Failed)?.Id ?? "mcp-startup",
                exception);
            foreach (var phase in evidence.Phases.Where(item => item.Id != AcceptancePhaseIds.Cleanup && item.Status == AcceptanceEvidenceStatus.NotStarted))
            {
                phase.Status = AcceptanceEvidenceStatus.Skipped;
            }
        }
        finally
        {
            var cleanupExceptions = await RunCleanupAsync(evidence);
            evidence.ProvisioningInvariant.ActualCalls = driver.ProvisioningCount;
            evidence.ProvisioningInvariant.Passed = evidence.ProvisioningInvariant.ActualCalls == evidence.ProvisioningInvariant.ExpectedCalls;
            evidence.CompletedAt = _timeProvider.GetUtcNow();

            if (primaryFailure is null && !evidence.ProvisioningInvariant.Passed)
            {
                primaryFailure = new InvalidOperationException($"Oracle acceptance must provision exactly once; observed {evidence.ProvisioningInvariant.ActualCalls} provisioning calls.");
                evidence.PrimaryFailure = AcceptanceFailureEvidence.From("exactly-one-provision", primaryFailure);
            }

            if (primaryFailure is null && cleanupExceptions.Count > 0)
            {
                primaryFailure = cleanupExceptions[0];
                evidence.PrimaryFailure = AcceptanceFailureEvidence.From(AcceptancePhaseIds.Cleanup, primaryFailure);
            }

            evidence.Status = primaryFailure is null
                && evidence.ProvisioningInvariant.Passed
                && evidence.Phases.All(phase => phase.Status == AcceptanceEvidenceStatus.Passed)
                    ? "passed"
                    : "failed";
            var json = JsonSerializer.Serialize(evidence, EvidenceJsonOptions);
            try
            {
                await driver.WriteEvidenceAsync(json, CancellationToken.None);
            }
            catch (Exception exception) when (primaryFailure is not null)
            {
                evidence.EvidenceWriteFailure = AcceptanceFailureEvidence.From("evidence-write", exception);
            }
            catch (Exception exception)
            {
                primaryFailure = exception;
                evidence.PrimaryFailure = AcceptanceFailureEvidence.From("evidence-write", exception);
                evidence.EvidenceWriteFailure = evidence.PrimaryFailure;
            }
        }

        if (primaryFailure is not null)
        {
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        return evidence;
    }

    private async Task<List<Exception>> RunCleanupAsync(AcceptanceRunEvidence evidence)
    {
        var exceptions = new List<Exception>();
        var cleanup = evidence.Phases.Single(item => item.Id == AcceptancePhaseIds.Cleanup);
        cleanup.Status = AcceptanceEvidenceStatus.Running;
        cleanup.StartedAt = _timeProvider.GetUtcNow();

        foreach (var stepId in driver.CleanupStepIds)
        {
            var step = new AcceptanceCleanupStepEvidence { Id = stepId };
            cleanup.CleanupSteps.Add(step);
            await AttemptCleanupAsync(step, token => driver.ExecuteCleanupStepAsync(stepId, token), CleanupStepTimeout, exceptions);
        }

        var shutdown = new AcceptanceCleanupStepEvidence { Id = "mcp-shutdown" };
        cleanup.CleanupSteps.Add(shutdown);
        await AttemptCleanupAsync(shutdown, token => driver.ShutdownMcpAsync(token), McpShutdownTimeout, exceptions);

        cleanup.Status = exceptions.Count == 0 ? AcceptanceEvidenceStatus.Passed : AcceptanceEvidenceStatus.Failed;
        cleanup.CompletedAt = _timeProvider.GetUtcNow();
        cleanup.Failure = exceptions.Count == 0 ? null : AcceptanceFailureEvidence.From(AcceptancePhaseIds.Cleanup, exceptions[0]);
        return exceptions;
    }

    private static async Task AttemptCleanupAsync(
        AcceptanceCleanupStepEvidence step,
        Func<CancellationToken, Task> action,
        TimeSpan timeout,
        ICollection<Exception> exceptions)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        try
        {
            await action(timeoutCts.Token);
            step.Status = AcceptanceEvidenceStatus.Passed;
        }
        catch (OperationCanceledException exception) when (timeoutCts.IsCancellationRequested)
        {
            var timeoutException = new TimeoutException($"Cleanup step '{step.Id}' did not complete within {timeout}.", exception);
            step.Status = AcceptanceEvidenceStatus.Failed;
            step.Failure = AcceptanceFailureEvidence.From(step.Id, timeoutException);
            exceptions.Add(timeoutException);
        }
        catch (Exception exception)
        {
            step.Status = AcceptanceEvidenceStatus.Failed;
            step.Failure = AcceptanceFailureEvidence.From(step.Id, exception);
            exceptions.Add(exception);
        }
    }
}

internal enum AcceptanceEvidenceStatus
{
    NotStarted,
    Running,
    Passed,
    Failed,
    Skipped,
}

internal sealed class AcceptanceRunEvidence
{
    public string SchemaVersion { get; init; } = "1";
    public string Kind { get; init; } = "oracleVerificationPhaseEvidence";
    public string Status { get; set; } = "running";
    public int ProvisioningCount => ProvisioningInvariant.ActualCalls;
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; set; }
    public AcceptancePackageProvenanceEvidence? PackageProvenance { get; init; }
    public AcceptanceMcpStartupEvidence McpStartup { get; set; } = new();
    public required IReadOnlyList<AcceptancePhaseEvidence> Phases { get; init; }
    public AcceptanceProvisioningInvariantEvidence ProvisioningInvariant { get; } = new();
    public AcceptanceFailureEvidence? PrimaryFailure { get; set; }
    public AcceptanceFailureEvidence? EvidenceWriteFailure { get; set; }
}

internal sealed record AcceptancePackageProvenanceEvidence(string PackagePath, string Sha256);

internal sealed class AcceptanceMcpStartupEvidence
{
    public AcceptanceEvidenceStatus Status { get; set; } = AcceptanceEvidenceStatus.NotStarted;
    public string? Detail { get; set; }
    public AcceptanceFailureEvidence? Failure { get; set; }
}

internal sealed class AcceptancePhaseEvidence
{
    public required string Id { get; init; }
    public AcceptanceEvidenceStatus Status { get; set; } = AcceptanceEvidenceStatus.NotStarted;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public AcceptanceFailureEvidence? Failure { get; set; }
    public List<AcceptanceCleanupStepEvidence> CleanupSteps { get; } = [];
}

internal sealed class AcceptanceCleanupStepEvidence
{
    public required string Id { get; init; }
    public AcceptanceEvidenceStatus Status { get; set; } = AcceptanceEvidenceStatus.NotStarted;
    public AcceptanceFailureEvidence? Failure { get; set; }
}

internal sealed class AcceptanceProvisioningInvariantEvidence
{
    public string Id { get; } = "exactly-one-provision";
    public int ExpectedCalls { get; } = 1;
    public int ActualCalls { get; set; }
    public bool Passed { get; set; }
}

internal sealed record AcceptanceFailureEvidence(string Operation, string ExceptionType, string Message)
{
    public static AcceptanceFailureEvidence From(string operation, Exception exception) =>
        new(operation, exception.GetType().FullName ?? exception.GetType().Name, exception.Message);
}
