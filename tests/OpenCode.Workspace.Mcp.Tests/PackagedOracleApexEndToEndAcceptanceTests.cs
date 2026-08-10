using ModelContextProtocol.Protocol;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.LocalClient;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using LocalWorkspaceRecord = OpenCode.Workspace.LocalClient.WorkspaceRecordModel;

namespace OpenCode.Workspace.Mcp.Tests;

[Collection("Packaged distribution")]
public sealed class PackagedOracleApexEndToEndAcceptanceTests(PackagedDistributionFixture fixture)
{
    [SkippableFact]
    [Trait("Category", "PackagedOracleApexEndToEndIntegration")]
    public async Task PackagedOracleApex_EndToEnd_PhasedAcceptance()
    {
        const string enablementMessage = "Packaged Oracle APEX end-to-end acceptance requires OPENCODE_RUN_PACKAGED_ORACLE_MCP=true.";
        var enabled = string.Equals(Environment.GetEnvironmentVariable("OPENCODE_RUN_PACKAGED_ORACLE_MCP"), "true", StringComparison.OrdinalIgnoreCase);
        var verificationMode = string.Equals(Environment.GetEnvironmentVariable("OPENCODE_ORACLE_VERIFICATION_MODE"), "true", StringComparison.OrdinalIgnoreCase);
        if (!verificationMode)
        {
            Skip.IfNot(enabled, enablementMessage);
        }

        Assert.True(enabled, enablementMessage);
        var archive = Environment.GetEnvironmentVariable("OPENCODE_EXISTING_PACKAGE_ARCHIVE");
        if (verificationMode)
        {
            Assert.False(string.IsNullOrWhiteSpace(archive), "Oracle verification requires the exact release archive through OPENCODE_EXISTING_PACKAGE_ARCHIVE.");
            Assert.True(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENCODE_EXISTING_PACKAGE_ROOT")), "Oracle verification does not permit OPENCODE_EXISTING_PACKAGE_ROOT to replace the release archive.");
            Assert.True(fixture.IsExternalPackage, "Oracle verification must execute the distribution extracted by PackagedDistributionFixture from OPENCODE_EXISTING_PACKAGE_ARCHIVE.");
        }

        var runRoot = Path.Combine(Path.GetTempPath(), "opencode packaged oracle apex acceptance", Guid.NewGuid().ToString("n"));
        var evidenceRoot = Environment.GetEnvironmentVariable("OPENCODE_PACKAGE_TEST_ARTIFACT_ROOT");
        evidenceRoot = string.IsNullOrWhiteSpace(evidenceRoot)
            ? Path.Combine(Path.GetTempPath(), "opencode packaged oracle apex evidence")
            : Path.Combine(Path.GetFullPath(evidenceRoot), "packaged-oracle-apex-end-to-end");
        Directory.CreateDirectory(runRoot);
        Directory.CreateDirectory(evidenceRoot);

        var driver = new PackagedOracleApexAcceptanceDriver(
            fixture.PackageRoot,
            runRoot,
            evidenceRoot,
            string.IsNullOrWhiteSpace(archive) ? null : Path.GetFullPath(archive));
        await new PhasedAcceptanceRunner(driver).RunAsync();
    }
}

internal sealed class PackagedOracleApexAcceptanceDriver : IPhasedAcceptanceDriver
{
    private const string WorkspaceNamePrefix = "packaged-oracle-apex-rc5";
    private const string EnvironmentName = "dev";
    private const string SourceRelativePath = "src/apex";
    private const string SentinelContents = "packaged-oracle-apex-rc5-sentinel\n";
    private static readonly TimeSpan ProvisionTimeout = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(10);
    private readonly string _packageRoot;
    private readonly string _runRoot;
    private readonly string _stateRoot;
    private readonly string _workspaceDestination;
    private readonly string _workspaceName;
    private readonly string _evidenceRoot;
    private readonly string _evidencePath;
    private PackagedMcpHarness? _mcp;
    private LocalHostClient? _localClient;
    private LocalWorkspaceRecord? _workspace;
    private string? _baselineSourceHash;

    public PackagedOracleApexAcceptanceDriver(string packageRoot, string runRoot, string evidenceRoot, string? archivePath)
    {
        _packageRoot = packageRoot;
        _runRoot = runRoot;
        _stateRoot = Path.Combine(runRoot, "local-host-state");
        _workspaceDestination = Path.Combine(runRoot, "workspaces");
        _workspaceName = $"{WorkspaceNamePrefix}-{Guid.NewGuid():n}";
        _evidenceRoot = evidenceRoot;
        _evidencePath = Path.Combine(evidenceRoot, "phase-evidence.json");
        Directory.CreateDirectory(_workspaceDestination);

        if (archivePath is not null)
        {
            using var stream = File.OpenRead(archivePath);
            PackageProvenance = new AcceptancePackageProvenanceEvidence(archivePath, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
        }
    }

    public AcceptancePackageProvenanceEvidence? PackageProvenance { get; }
    public string? McpStartupDetail => _mcp is null ? null : $"pid={_mcp.Report.ProcessId}; packageRoot={_packageRoot}; stateRoot={_stateRoot}";
    public int ProvisioningCount { get; private set; }
    public IReadOnlyList<string> CleanupStepIds { get; } = ["stop-workspace", "remove-runtime", "local-host-inventory", "direct-compose-labels", "preserve-diagnostics"];

    public async Task StartMcpAsync(CancellationToken cancellationToken)
    {
        var executable = Path.Combine(_packageRoot, "bin", "mcp", "OpenCode.Workspace.Mcp" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        _mcp = await PackagedMcpHarness.StartAsync(
            executable,
            _runRoot,
            new Dictionary<string, string?>
            {
                ["mcp__catalogRoot"] = null,
                ["mcp__workspaceStateRoot"] = Path.Combine(_stateRoot, "workspace-state"),
                ["mcp__smokeArtifactsRoot"] = Path.Combine(_stateRoot, "artifacts"),
                ["localHost__stateRoot"] = _stateRoot,
                ["localHost__executableDirectory"] = Path.Combine(_packageRoot, "bin", "local-host"),
                ["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "None",
                ["Logging__LogLevel__Default"] = "None",
            },
            TimeSpan.FromMinutes(1));

        _localClient = await LocalHostClient.ConnectAsync(new LocalHostClientOptions
        {
            DistributionRoot = _packageRoot,
            StateRoot = _stateRoot,
        }, cancellationToken);
        Assert.Equal("ready", (await _localClient.GetReadinessAsync(cancellationToken)).Status, ignoreCase: true);
    }

    public Task ExecutePhaseAsync(string phaseId, CancellationToken cancellationToken) => phaseId switch
    {
        AcceptancePhaseIds.Provision => ProvisionAsync(cancellationToken),
        AcceptancePhaseIds.DiscoverConnect => DiscoverConnectAsync(cancellationToken),
        AcceptancePhaseIds.AssistantImportRollbackPull => AssistantImportRollbackPullAsync(cancellationToken),
        AcceptancePhaseIds.CompilerDrivenRepair => CompilerDrivenRepairAsync(),
        _ => throw new ArgumentOutOfRangeException(nameof(phaseId), phaseId, "Unknown acceptance phase."),
    };

    public Task ExecuteCleanupStepAsync(string stepId, CancellationToken cancellationToken) => stepId switch
    {
        "stop-workspace" => RunMcpLifecycleAsync("stop_workspace", "stop", cancellationToken),
        "remove-runtime" => RunMcpLifecycleAsync("remove_workspace_runtime", "remove-runtime", cancellationToken),
        "local-host-inventory" => AssertLocalHostInventoryAsync(cancellationToken),
        "direct-compose-labels" => AssertNoComposeResourcesAsync(cancellationToken),
        "preserve-diagnostics" => PreserveDiagnosticsAsync(cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(stepId), stepId, "Unknown cleanup step."),
    };

    public async Task ShutdownMcpAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_localClient is not null)
            {
                await _localClient.DisposeAsync();
                _localClient = null;
            }
            if (_mcp is not null)
            {
                await _mcp.DisposeClientAndTransportAsync(TimeSpan.FromSeconds(30));
                Assert.False(_mcp.Report.ForcedTerminationRequired);
                Assert.NotNull(_mcp.Report.ExitedUtc);
                _mcp = null;
            }
        }
        finally
        {
            if (Directory.Exists(_runRoot))
            {
                Directory.Delete(_runRoot, recursive: true);
            }
        }
    }

    public Task WriteEvidenceAsync(string json, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(_evidencePath, json, cancellationToken);

    private async Task ProvisionAsync(CancellationToken cancellationToken)
    {
        var create = await Mcp.CallToolAsync("create_workspace", new Dictionary<string, object?>
        {
            ["templateId"] = "oracle-apexlang-demo",
            ["workspaceName"] = _workspaceName,
            ["destinationRoot"] = _workspaceDestination,
        }, cancellationToken: cancellationToken);
        var created = await WaitForMcpOperationAsync(ReadOperation(create), TimeSpan.FromMinutes(2), cancellationToken);
        Assert.Equal(McpOperationStatus.Succeeded, created.Status);
        _workspace = created.Result?.Deserialize<LocalWorkspaceRecord>(OpenCodeWorkspaceMcpContract.JsonOptions)
            ?? throw new InvalidOperationException("Packaged MCP create_workspace did not return the canonical workspace record.");

        ProvisioningCount++;
        var provision = await Mcp.CallToolAsync("provision_workspace", new Dictionary<string, object?> { ["workspaceId"] = _workspace.WorkspaceId }, cancellationToken: cancellationToken);
        var provisioned = await WaitForMcpOperationAsync(ReadOperation(provision), ProvisionTimeout, cancellationToken);
        Assert.Equal(McpOperationStatus.Succeeded, provisioned.Status);
        Assert.Contains(provisioned.RecentEvents, item => item.Phase.Contains("preparing", StringComparison.OrdinalIgnoreCase) || item.Message.Contains("Preparing workspace", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(provisioned.RecentEvents, item => item.Phase.Contains("starting", StringComparison.OrdinalIgnoreCase) || item.Message.Contains("Starting Oracle", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(provisioned.RecentEvents, item => item.Phase.Contains("installingApex", StringComparison.OrdinalIgnoreCase) || item.Message.Contains("APEX", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(provisioned.RecentEvents, item => item.Phase.Contains("configuringOrds", StringComparison.OrdinalIgnoreCase) || item.Message.Contains("ORDS", StringComparison.OrdinalIgnoreCase));

        _workspace = await LocalClient.ValidateWorkspaceAsync(_workspace.WorkspaceId, cancellationToken);
        Assert.Contains(_workspace.Snapshot.Health.Services, item => item.ServiceId.Contains("oracle", StringComparison.OrdinalIgnoreCase) && item.Status is WorkspaceHealthStatus.Healthy or WorkspaceHealthStatus.Attention);
        Assert.Contains(_workspace.Snapshot.AvailableServices, item => item.HostUrl.Contains("ords", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(_workspace.Snapshot.Health.Services.SelectMany(item => item.Evidence), item => item.Label.Contains("APEX", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.Value));
        Assert.Contains(_workspace.Snapshot.Health.Services.SelectMany(item => item.Evidence), item => item.Label.Contains("XDB", StringComparison.OrdinalIgnoreCase) && item.Value.Contains("VALID", StringComparison.OrdinalIgnoreCase));
        Assert.True(_workspace.Snapshot.Record.LastProvisioningHealth?.Succeeded ?? false);
        Assert.False(string.IsNullOrWhiteSpace(_workspace.Snapshot.Record.LastProvisioningHealth?.ApexVersion));

        var sqlcl = await RunDockerExecAsync(
            DockerService.GetWorkspaceContainerName(_workspace.Snapshot.Definition),
            "scripts/sqlcl.sh -S \"${ORACLE_DEMO_CONNECTION}\" <<'SQL'\nset heading off feedback off verify off serveroutput on\nselect 'SQL_OK' from dual;\nbegin dbms_output.put_line('PLSQL_OK'); end;\n/\nexit\nSQL",
            cancellationToken);
        Assert.Equal(0, sqlcl.ExitCode);
        Assert.Contains("SQL_OK", sqlcl.Output, StringComparison.Ordinal);
        Assert.Contains("PLSQL_OK", sqlcl.Output, StringComparison.Ordinal);
    }

    private async Task DiscoverConnectAsync(CancellationToken cancellationToken)
    {
        var discovery = await LocalClient.DiscoverOracleApexApplicationsAsync(Workspace.WorkspaceId, new OracleApexApplicationDiscoveryQuery
        {
            WorkspaceId = Workspace.WorkspaceId,
            EnvironmentName = EnvironmentName,
            WorkspaceName = "TEST",
            ParsingSchema = "TESTSCHEMA",
            SqlclProfile = "local-apex-dev",
            SourcePath = SourceRelativePath,
        }, cancellationToken);
        var application = Assert.Single(discovery.Applications, item => item.ApplicationId == 101);
        Assert.False(string.IsNullOrWhiteSpace(application.ApplicationName));

        var connect = await LocalClient.StartConnectExistingOracleApexApplicationAsync(Workspace.WorkspaceId, new ConnectExistingOracleApexApplicationRequest
        {
            CommandId = CommandId("connect"),
            WorkspaceId = Workspace.WorkspaceId,
            EnvironmentName = EnvironmentName,
            WorkspaceName = discovery.WorkspaceName,
            ParsingSchema = discovery.ParsingSchema,
            SqlclProfile = discovery.SqlclProfile,
            SourcePath = discovery.SourcePath,
            ApplicationId = application.ApplicationId,
            RequestedBy = Initiator(),
        }, cancellationToken);
        var connected = await WaitForLocalOperationAsync(connect, OperationTimeout, cancellationToken);
        Assert.Equal(WorkspaceOperationStatus.Succeeded, connected.Status);
        var result = connected.Result?.Deserialize<ConnectExistingOracleApexApplicationOperationRecord>(LocalHostContract.JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(application.ApplicationId, result.ApplicationId);

        var sourceRoot = SourceRoot;
        Assert.True(File.Exists(Path.Combine(sourceRoot, "application.apx")), $"Connected source was not exported to '{sourceRoot}'.");
        await File.WriteAllTextAsync(SentinelPath, SentinelContents, cancellationToken);
        _baselineSourceHash = ComputeDirectoryHash(sourceRoot);
        Assert.Equal(WorkspaceSynchronizationState.InSync, (await LocalClient.GetSynchronizationStatusAsync(Workspace.WorkspaceId, EnvironmentName, cancellationToken)).Snapshot.State);
        AssertAtlasCurrent(sourceRoot);
    }

    private async Task AssistantImportRollbackPullAsync(CancellationToken cancellationToken)
    {
        var planOperation = await LocalClient.StartPlanOracleAssistantAsync(Workspace.WorkspaceId, new OracleAssistantPlanRequest
        {
            CommandId = CommandId("plan"),
            WorkspaceId = Workspace.WorkspaceId,
            Intent = "Create RC5 Reports page",
            EnvironmentName = EnvironmentName,
            RequestedBy = Initiator(),
        }, cancellationToken);
        var planned = Result<OracleAssistantPlanOperationRecord>(await WaitForLocalOperationAsync(planOperation, OperationTimeout, cancellationToken));
        Assert.Empty(planned.Response.UnresolvedQuestions);
        Assert.False(string.IsNullOrWhiteSpace(planned.PlanId));

        var applyOperation = await LocalClient.StartApplyOracleAssistantAsync(Workspace.WorkspaceId, new OracleAssistantApplyRequest
        {
            CommandId = CommandId("apply-validate-only"),
            WorkspaceId = Workspace.WorkspaceId,
            PlanId = planned.PlanId,
            ContextRevision = planned.ContextRevision,
            ConfirmPlan = true,
            PostEditBehavior = OracleApexAssistantPostEditBehavior.ValidateOnly,
            EnvironmentName = EnvironmentName,
            RequestedBy = Initiator(),
        }, cancellationToken);
        var applied = Result<OracleAssistantApplyOperationRecord>(await WaitForLocalOperationAsync(applyOperation, OperationTimeout, cancellationToken));
        Assert.True(applied.Response.IsSuccess, applied.Response.Summary);
        Assert.Equal(OracleApexAssistantPostEditBehavior.ValidateOnly, applied.Response.PostEditBehavior);
        Assert.NotEmpty(applied.Response.ChangedFiles);
        Assert.Equal(OracleApexAssistantRollbackState.Available, applied.Response.RollbackManifest?.RollbackState);
        var appliedSynchronization = Assert.IsType<WorkspaceSynchronizationSnapshot>(applied.Response.Synchronization);
        Assert.Equal(WorkspaceSynchronizationState.GitAhead, appliedSynchronization.State);
        Assert.NotEqual(_baselineSourceHash, ComputeDirectoryHash(SourceRoot));
        AssertSentinel();
        var importOperation = await LocalClient.StartImportOracleAssistantAsync(Workspace.WorkspaceId, new OracleAssistantImportRequest
        {
            CommandId = CommandId("assistant-import"),
            WorkspaceId = Workspace.WorkspaceId,
            ExecutionId = applied.ExecutionId,
            EnvironmentName = EnvironmentName,
            RequestedBy = Initiator(),
        }, cancellationToken);
        var imported = Result<OracleAssistantSynchronizationOperationRecord>(await WaitForLocalOperationAsync(importOperation, OperationTimeout, cancellationToken));
        Assert.Equal(applied.ExecutionId, imported.ExecutionId);
        Assert.NotNull(imported.Response.ProcessResult);
        Assert.Equal(0, imported.Response.ProcessResult.ExitCode);
        Assert.Equal(WorkspaceSynchronizationState.InSync, imported.Response.Snapshot.State);
        AssertSentinel();

        var rollbackOperation = await LocalClient.StartRollbackOracleAssistantAsync(Workspace.WorkspaceId, new OracleAssistantRollbackRequest
        {
            CommandId = CommandId("source-rollback"),
            WorkspaceId = Workspace.WorkspaceId,
            ExecutionId = applied.ExecutionId,
            RequestedBy = Initiator(),
        }, cancellationToken);
        var rolledBack = Result<OracleAssistantRollbackOperationRecord>(await WaitForLocalOperationAsync(rollbackOperation, OperationTimeout, cancellationToken));
        Assert.True(rolledBack.Response.IsSuccess, rolledBack.Response.Summary);
        Assert.Equal(OracleApexAssistantRollbackState.Completed, rolledBack.Response.RollbackState);
        Assert.NotEmpty(rolledBack.Response.RestoredFiles);
        Assert.Equal(_baselineSourceHash, ComputeDirectoryHash(SourceRoot));
        AssertSentinel();

        var diffOperation = await LocalClient.StartDiffSynchronizationAsync(Workspace.WorkspaceId, new WorkspaceSynchronizationDiffRequest
        {
            CommandId = CommandId("post-rollback-diff"),
            WorkspaceId = Workspace.WorkspaceId,
            EnvironmentName = EnvironmentName,
            RequestedBy = Initiator(),
        }, cancellationToken);
        var rollbackDrift = Result<WorkspaceSynchronizationDiffResult>(await WaitForLocalOperationAsync(diffOperation, OperationTimeout, cancellationToken));
        Assert.False(string.IsNullOrWhiteSpace(rollbackDrift.DiffText));
        Assert.Contains("report", rollbackDrift.DiffText, StringComparison.OrdinalIgnoreCase);

        var pullOperation = await LocalClient.StartPullSynchronizationAsync(Workspace.WorkspaceId, new WorkspaceSynchronizationExportRequest
        {
            CommandId = CommandId("sync-pull"),
            WorkspaceId = Workspace.WorkspaceId,
            EnvironmentName = EnvironmentName,
            RequestedBy = Initiator(),
        }, cancellationToken);
        var pulled = Result<WorkspaceSynchronizationOperationResult>(await WaitForLocalOperationAsync(pullOperation, OperationTimeout, cancellationToken));
        Assert.Equal(WorkspaceSynchronizationState.InSync, pulled.Snapshot.State);
        Assert.NotNull(pulled.Snapshot.DefaultEnvironment?.LastPullUtc);
        Assert.False(string.IsNullOrWhiteSpace(pulled.Snapshot.DefaultEnvironment?.WorkspaceSourceSignature));
        Assert.False(string.IsNullOrWhiteSpace(pulled.Snapshot.DefaultEnvironment?.RemoteSourceSignature));
        Assert.NotEqual(_baselineSourceHash, ComputeDirectoryHash(SourceRoot));
        AssertSentinel();
        AssertAtlasCurrent(SourceRoot);
        var indexText = File.ReadAllText(Path.Combine(AtlasRoot, "workspace-index.json"));
        Assert.Contains("Reports", indexText, StringComparison.OrdinalIgnoreCase);
    }

    private Task CompilerDrivenRepairAsync()
        => Task.FromException(new InvalidOperationException(
            "Compiler-driven repair acceptance requires an authentic, pinned Oracle APEX compiler diagnostic fixture captured from the exact verified package and Oracle version; no such fixture is checked in, so this phase fails closed."));

    private async Task RunMcpLifecycleAsync(string toolName, string operationName, CancellationToken cancellationToken)
    {
        if (_workspace is null || _mcp is null)
        {
            return;
        }
        var response = await Mcp.CallToolAsync(toolName, new Dictionary<string, object?> { ["workspaceId"] = _workspace.WorkspaceId }, cancellationToken: cancellationToken);
        var terminal = await WaitForMcpOperationAsync(ReadOperation(response), OperationTimeout, cancellationToken);
        Assert.True(terminal.Status == McpOperationStatus.Succeeded, $"MCP {operationName} failed: {terminal.FailureMessage}");
    }

    private async Task AssertLocalHostInventoryAsync(CancellationToken cancellationToken)
    {
        if (_workspace is null || _localClient is null)
        {
            return;
        }
        var inventory = await LocalClient.RunRuntimeDoctorAsync(null, null, null, _workspace.WorkspaceRoot, cancellationToken);
        Assert.Empty(inventory.Resources);
        Assert.Empty(inventory.Orphans);
    }

    private async Task AssertNoComposeResourcesAsync(CancellationToken cancellationToken)
    {
        if (_workspace is null)
        {
            return;
        }
        var project = WorkspacePathBuilder.Slugify(_workspace.Name);
        foreach (var resourceType in new[] { "container", "network", "volume" })
        {
            var startInfo = new ProcessStartInfo("docker") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            startInfo.ArgumentList.Add(resourceType);
            startInfo.ArgumentList.Add("ls");
            if (resourceType == "container") startInfo.ArgumentList.Add("--all");
            startInfo.ArgumentList.Add("--quiet");
            startInfo.ArgumentList.Add("--filter");
            startInfo.ArgumentList.Add($"label=com.docker.compose.project={project}");
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start direct Docker compose-label inventory.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            var error = await errorTask;
            Assert.True(process.ExitCode == 0, $"Docker {resourceType} compose-label inventory failed: {error}");
            Assert.True(string.IsNullOrWhiteSpace(output), $"Docker {resourceType} resources remain for compose project '{project}': {output}");
        }
    }

    private static async Task<(int ExitCode, string Output)> RunDockerExecAsync(string containerName, string command, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("docker") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var argument in new[] { "exec", containerName, "bash", "-lc", command })
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start packaged Oracle SQLcl validation.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await outputTask + Environment.NewLine + await errorTask);
    }

    private async Task PreserveDiagnosticsAsync(CancellationToken cancellationToken)
    {
        var diagnosticsRoot = Path.Combine(_evidenceRoot, "diagnostics");
        Directory.CreateDirectory(diagnosticsRoot);
        if (Directory.Exists(Path.Combine(_stateRoot, "operations")))
        {
            CopyDirectory(Path.Combine(_stateRoot, "operations"), Path.Combine(diagnosticsRoot, "operations"));
        }
        if (_workspace is not null && Directory.Exists(Path.Combine(_workspace.WorkspaceRoot, ".opencode")))
        {
            CopyDirectory(Path.Combine(_workspace.WorkspaceRoot, ".opencode"), Path.Combine(diagnosticsRoot, "workspace-opencode"));
        }
        if (_mcp is not null)
        {
            await File.WriteAllLinesAsync(Path.Combine(diagnosticsRoot, "mcp-stderr.log"), _mcp.StandardErrorLines, cancellationToken);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories).Prepend(source))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private async Task<McpOperationModel> WaitForMcpOperationAsync(McpOperationModel operation, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var current = operation;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (current.Status is McpOperationStatus.Succeeded or McpOperationStatus.Failed or McpOperationStatus.Cancelled) return current;
            var response = await Mcp.CallToolAsync("get_operation", new Dictionary<string, object?> { ["operationId"] = operation.OperationId }, cancellationToken: cancellationToken);
            current = ReadEnvelope<McpOperationModel>(response);
            await Task.Delay(500, cancellationToken);
        }
        throw new TimeoutException($"Packaged MCP operation '{operation.OperationId}' did not complete within {timeout}.");
    }

    private async Task<WorkspaceOperationRecord> WaitForLocalOperationAsync(WorkspaceOperationRecord operation, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var current = operation;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (current.Status is WorkspaceOperationStatus.Succeeded or WorkspaceOperationStatus.Failed or WorkspaceOperationStatus.Cancelled)
            {
                Assert.True(current.Status == WorkspaceOperationStatus.Succeeded, $"LocalHost operation '{current.OperationKind}' failed: {current.OriginalFailure?.Message}");
                return current;
            }
            await Task.Delay(250, cancellationToken);
            current = await LocalClient.GetOperationAsync(operation.OperationId, cancellationToken: cancellationToken);
        }
        throw new TimeoutException($"LocalHost operation '{operation.OperationId}' did not complete within {timeout}.");
    }

    private static McpOperationModel ReadOperation(CallToolResult result)
    {
        var element = Payload(result);
        return JsonSerializer.Deserialize<McpOperationModel>(element.GetRawText(), OpenCodeWorkspaceMcpContract.JsonOptions)
            ?? throw new InvalidOperationException("MCP tool did not return an operation record.");
    }

    private static T ReadEnvelope<T>(CallToolResult result)
    {
        var element = Payload(result);
        return element.Deserialize<McpToolEnvelope<T>>(OpenCodeWorkspaceMcpContract.JsonOptions)!.Data;
    }

    private static JsonElement Payload(CallToolResult result)
        => result.StructuredContent is JsonElement structured
            ? structured
            : JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text).RootElement.Clone();

    private static T Result<T>(WorkspaceOperationRecord operation)
    {
        if (operation.Result is not JsonElement result)
        {
            throw new InvalidOperationException($"LocalHost operation '{operation.OperationKind}' did not return {typeof(T).Name}.");
        }
        return result.Deserialize<T>(LocalHostContract.JsonOptions)
            ?? throw new InvalidOperationException($"LocalHost operation '{operation.OperationKind}' returned an invalid {typeof(T).Name}.");
    }

    private void AssertAtlasCurrent(string sourceRoot)
    {
        var statePath = Path.Combine(AtlasRoot, "state.json");
        Assert.True(File.Exists(statePath), $"Atlas state was not generated at '{statePath}'.");
        Assert.True(File.Exists(Path.Combine(AtlasRoot, "workspace-index.json")), "Atlas workspace index was not generated.");
        using var state = JsonDocument.Parse(File.ReadAllText(statePath));
        Assert.Equal("ready", state.RootElement.GetProperty("status").GetString(), ignoreCase: true);
        Assert.Equal(SourceRelativePath, state.RootElement.GetProperty("sourcePath").GetString());
        Assert.Equal(ComputeAtlasSourceHash(sourceRoot), state.RootElement.GetProperty("sourceHash").GetString());
    }

    private void AssertSentinel() => Assert.Equal(SentinelContents, File.ReadAllText(SentinelPath));

    private static string ComputeDirectoryHash(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => Path.GetRelativePath(root, path), StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            hash.AppendData(Encoding.UTF8.GetBytes(relative + "\n"));
            hash.AppendData(File.ReadAllBytes(path));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string ComputeAtlasSourceHash(string root)
    {
        var files = Directory.GetFiles(root, "*.apx", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.OrdinalIgnoreCase)
            .Select(path => $"{Path.GetRelativePath(root, path).Replace('\\', '/')}\n{File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal)}")
            .ToArray();
        return WorkspaceAppliedStateService.ComputeHash(files);
    }

    private static OperationInitiator Initiator() => new() { Kind = "acceptance", ClientKind = "packaged-test", ClientInstanceId = "oracle-apex-rc5" };
    private static string CommandId(string operation) => $"oracle-apex-rc5-{operation}-{Guid.NewGuid():n}";
    private ModelContextProtocol.Client.McpClient Mcp => _mcp?.Client ?? throw new InvalidOperationException("Packaged MCP has not started.");
    private LocalHostClient LocalClient => _localClient ?? throw new InvalidOperationException("LocalHostClient has not connected to the packaged state root.");
    private LocalWorkspaceRecord Workspace => _workspace ?? throw new InvalidOperationException("Oracle workspace has not been created.");
    private string SourceRoot => Path.Combine(Workspace.WorkspaceRoot, SourceRelativePath.Replace('/', Path.DirectorySeparatorChar));
    private string SentinelPath => Path.Combine(Workspace.WorkspaceRoot, ".rc5-acceptance-sentinel");
    private string AtlasRoot => Path.Combine(Workspace.WorkspaceRoot, ".opencode", "knowledge", "apexlang-atlas");
}
