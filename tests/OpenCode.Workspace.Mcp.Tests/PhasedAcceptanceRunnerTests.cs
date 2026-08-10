using System.Text.Json;
using Xunit;

namespace OpenCode.Workspace.Mcp.Tests;

public sealed class PhasedAcceptanceRunnerTests
{
    [Fact]
    public async Task Success_RunsRequiredPhasesInOrderAndProvisionsExactlyOnce()
    {
        var driver = new FakeAcceptanceDriver("remove-runtime", "remove-registration");

        var evidence = await new PhasedAcceptanceRunner(driver).RunAsync();

        Assert.Equal(new[]
        {
            "start-mcp",
            "phase:provision",
            "phase:discover-connect",
            "phase:assistant-import-rollback-pull",
            "phase:compiler-driven-repair",
            "cleanup:remove-runtime",
            "cleanup:remove-registration",
            "shutdown-mcp",
            "write-evidence",
        }, driver.Calls);
        Assert.Equal(AcceptancePhaseIds.Required, evidence.Phases.Select(item => item.Id));
        Assert.All(evidence.Phases, phase => Assert.Equal(AcceptanceEvidenceStatus.Passed, phase.Status));
        Assert.Equal(1, evidence.ProvisioningInvariant.ActualCalls);
        Assert.True(evidence.ProvisioningInvariant.Passed);
        Assert.Equal(1, driver.Calls.Count(item => item == "phase:provision"));

        using var json = JsonDocument.Parse(driver.EvidenceJson!);
        Assert.Equal("1", json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("oracleVerificationPhaseEvidence", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("passed", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("provisioningCount").GetInt32());
        Assert.Equal(AcceptancePhaseIds.Required, json.RootElement.GetProperty("phases").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        Assert.Equal("package.zip", json.RootElement.GetProperty("packageProvenance").GetProperty("packagePath").GetString());
        Assert.Equal("passed", json.RootElement.GetProperty("mcpStartup").GetProperty("status").GetString());
        Assert.Equal("exactly-one-provision", json.RootElement.GetProperty("provisioningInvariant").GetProperty("id").GetString());
    }

    [Fact]
    public async Task PhaseFailure_StillRunsCleanupAndPreservesPrimaryFailure()
    {
        var failure = new InvalidOperationException("discover failed");
        var driver = new FakeAcceptanceDriver("remove-runtime", "remove-registration")
        {
            PhaseFailures = { [AcceptancePhaseIds.DiscoverConnect] = failure },
            CleanupFailures = { ["remove-runtime"] = new InvalidOperationException("cleanup also failed") },
        };

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => new PhasedAcceptanceRunner(driver).RunAsync());

        Assert.Same(failure, thrown);
        Assert.Contains("cleanup:remove-runtime", driver.Calls);
        Assert.Contains("cleanup:remove-registration", driver.Calls);
        Assert.Contains("shutdown-mcp", driver.Calls);
        Assert.Contains("write-evidence", driver.Calls);
        Assert.DoesNotContain("phase:assistant-import-rollback-pull", driver.Calls);
        using var json = JsonDocument.Parse(driver.EvidenceJson!);
        Assert.Equal("discover-connect", json.RootElement.GetProperty("primaryFailure").GetProperty("operation").GetString());
        Assert.Equal("failed", Phase(json, AcceptancePhaseIds.Cleanup).GetProperty("status").GetString());
    }

    [Fact]
    public async Task CleanupSubfailure_DoesNotShortCircuitRemainingCleanupOrShutdown()
    {
        var failure = new InvalidOperationException("runtime cleanup failed");
        var driver = new FakeAcceptanceDriver("remove-runtime", "remove-registration")
        {
            CleanupFailures = { ["remove-runtime"] = failure },
        };

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => new PhasedAcceptanceRunner(driver).RunAsync());

        Assert.Same(failure, thrown);
        Assert.True(driver.Calls.IndexOf("cleanup:remove-runtime") < driver.Calls.IndexOf("cleanup:remove-registration"));
        Assert.True(driver.Calls.IndexOf("cleanup:remove-registration") < driver.Calls.IndexOf("shutdown-mcp"));
        Assert.Contains("write-evidence", driver.Calls);
        using var json = JsonDocument.Parse(driver.EvidenceJson!);
        var cleanup = Phase(json, AcceptancePhaseIds.Cleanup);
        Assert.Equal("failed", cleanup.GetProperty("status").GetString());
        Assert.Equal(
            new[] { "remove-runtime", "remove-registration", "mcp-shutdown" },
            cleanup.GetProperty("cleanupSteps").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task PreWorkspaceFailure_StillShutsDownAndWritesEvidence()
    {
        var failure = new InvalidOperationException("MCP did not start");
        var driver = new FakeAcceptanceDriver("preflight-cleanup") { StartupFailure = failure };

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => new PhasedAcceptanceRunner(driver).RunAsync());

        Assert.Same(failure, thrown);
        Assert.DoesNotContain(driver.Calls, call => call.StartsWith("phase:", StringComparison.Ordinal));
        Assert.Equal(new[] { "start-mcp", "cleanup:preflight-cleanup", "shutdown-mcp", "write-evidence" }, driver.Calls);
        using var json = JsonDocument.Parse(driver.EvidenceJson!);
        Assert.Equal("failed", json.RootElement.GetProperty("mcpStartup").GetProperty("status").GetString());
        Assert.Equal("mcp-startup", json.RootElement.GetProperty("primaryFailure").GetProperty("operation").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("provisioningInvariant").GetProperty("actualCalls").GetInt32());
        Assert.All(
            json.RootElement.GetProperty("phases").EnumerateArray().Where(item => item.GetProperty("id").GetString() != AcceptancePhaseIds.Cleanup),
            phase => Assert.Equal("skipped", phase.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task DuplicateProvisioning_FailsTheRunAfterCleanup()
    {
        var driver = new FakeAcceptanceDriver("remove-runtime") { AdditionalProvisioningCalls = 1 };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new PhasedAcceptanceRunner(driver).RunAsync());

        Assert.Contains("exactly once", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cleanup:remove-runtime", driver.Calls);
        Assert.Contains("shutdown-mcp", driver.Calls);
        using var json = JsonDocument.Parse(driver.EvidenceJson!);
        Assert.Equal("failed", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("provisioningCount").GetInt32());
    }

    private static JsonElement Phase(JsonDocument json, string phaseId) =>
        json.RootElement.GetProperty("phases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == phaseId);

    private sealed class FakeAcceptanceDriver(params string[] cleanupStepIds) : IPhasedAcceptanceDriver
    {
        public AcceptancePackageProvenanceEvidence? PackageProvenance { get; } = new("package.zip", "abc123");
        public string? McpStartupDetail => "fake MCP ready";
        public int ProvisioningCount { get; private set; }
        public IReadOnlyList<string> CleanupStepIds { get; } = cleanupStepIds;
        public List<string> Calls { get; } = [];
        public Dictionary<string, Exception> PhaseFailures { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Exception> CleanupFailures { get; } = new(StringComparer.Ordinal);
        public Exception? StartupFailure { get; init; }
        public int AdditionalProvisioningCalls { get; init; }
        public string? EvidenceJson { get; private set; }

        public Task StartMcpAsync(CancellationToken cancellationToken)
        {
            Calls.Add("start-mcp");
            return StartupFailure is null ? Task.CompletedTask : Task.FromException(StartupFailure);
        }

        public Task ExecutePhaseAsync(string phaseId, CancellationToken cancellationToken)
        {
            Calls.Add($"phase:{phaseId}");
            if (phaseId == AcceptancePhaseIds.Provision)
            {
                ProvisioningCount += 1 + AdditionalProvisioningCalls;
            }
            return PhaseFailures.TryGetValue(phaseId, out var failure) ? Task.FromException(failure) : Task.CompletedTask;
        }

        public Task ExecuteCleanupStepAsync(string stepId, CancellationToken cancellationToken)
        {
            Calls.Add($"cleanup:{stepId}");
            return CleanupFailures.TryGetValue(stepId, out var failure) ? Task.FromException(failure) : Task.CompletedTask;
        }

        public Task ShutdownMcpAsync(CancellationToken cancellationToken)
        {
            Calls.Add("shutdown-mcp");
            return Task.CompletedTask;
        }

        public Task WriteEvidenceAsync(string json, CancellationToken cancellationToken)
        {
            Calls.Add("write-evidence");
            EvidenceJson = json;
            return Task.CompletedTask;
        }
    }
}
