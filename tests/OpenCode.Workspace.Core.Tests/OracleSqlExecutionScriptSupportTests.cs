using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Generation;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class OracleSqlExecutionScriptSupportTests
{
    [Fact]
    public void NormalizeSingleStatementText_RemovesBomCrLfAndTrailingTerminator()
    {
        var normalized = OracleSqlExecutionScriptSupport.NormalizeSingleStatementText("\uFEFFSELECT status FROM dual;\r\n");

        Assert.Equal("SELECT status FROM dual\n", normalized);
    }

    [Fact]
    public void NormalizeScriptText_RemovesBomAndCrLfButPreservesScriptTerminators()
    {
        var normalized = OracleSqlExecutionScriptSupport.NormalizeScriptText("\uFEFFBEGIN\r\n  NULL;\r\nEND;\r\n/\r\n");

        Assert.Equal("BEGIN\n  NULL;\nEND;\n/\n", normalized);
    }

    [Fact]
    public void BuildDiagnosticPreview_SkipsGeneratedHeaders()
    {
        var preview = OracleSqlExecutionScriptSupport.BuildDiagnosticPreview("""
            -- GENERATED FILE - DO NOT EDIT FOR DURABLE CHANGES
            -- Source inputs: workspace.yaml and catalog manifests under catalog/.
            -- User edits to this file are not preserved. Edit workspace.yaml or catalog manifests instead.
            apex validate -workspace TEST -input /workspace/exports/apexlang/hello-apexlang
            exit
            """);

        Assert.Equal("apex validate -workspace TEST -input /workspace/exports/apexlang/hello-apexlang exit", preview);
    }

    [Fact]
    public void ShellLibrary_ContainsSanitizedDiagnosticsAndSqlErrorHandling()
    {
        var script = OracleSqlExecutionScriptSupport.BuildShellLibrary();

        Assert.Contains("WHENEVER SQLERROR EXIT SQL.SQLCODE", script, StringComparison.Ordinal);
        Assert.Contains("[oracle-sql] phase=", script, StringComparison.Ordinal);
        Assert.Contains("[oracle-sql] source=", script, StringComparison.Ordinal);
        Assert.Contains("[oracle-sql] statement=", script, StringComparison.Ordinal);
        Assert.Contains("oracle_sql_sanitize_output", script, StringComparison.Ordinal);
        Assert.Contains(OracleSqlExecutionScriptSupport.ResultBeginMarker, script, StringComparison.Ordinal);
        Assert.Contains(OracleSqlExecutionScriptSupport.ResultEndMarker, script, StringComparison.Ordinal);
        Assert.Contains("single-sql-statement", script, StringComparison.Ordinal);
        Assert.Contains("query-script", script, StringComparison.Ordinal);
        Assert.Contains("sqlcl-command-script", script, StringComparison.Ordinal);
    }

    [Fact]
    public void OracleProvisioningAndApexlangScripts_UseSharedSqlExecutionMechanism()
    {
        var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
        var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks());
        var expander = new TemplateExpander();
        var resolved = resolver.Resolve(expander.Expand("oracle-apexlang", provider.LoadTemplates().Single(item => item.Id == "oracle-apexlang-demo")));

        var provisionScript = new OracleWorkspaceProvisioningScriptGenerator().Generate(resolved);
        var generatedFiles = new WorkspaceContentGenerator().Generate(resolved);
        var helloWorldScript = generatedFiles[Path.Combine("scripts", "apexlang-hello-world.sh")];

        Assert.Contains("oracle_sql_run_file", provisionScript, StringComparison.Ordinal);
        Assert.Contains("'query_apex_registry'", provisionScript, StringComparison.Ordinal);
        Assert.Contains("'query_database_open_mode'", provisionScript, StringComparison.Ordinal);
        Assert.Contains("'apexins.sql'", provisionScript, StringComparison.Ordinal);

        Assert.Contains("oracle_sql_run_file 'Creating Sample Application' sqlcl /nolog sqlcl-command-script 'sql/hello-apexlang/generate-hello-apexlang.sql'", helloWorldScript, StringComparison.Ordinal);
        Assert.Contains("sql/hello-apexlang/validate-hello-apexlang.sql", helloWorldScript, StringComparison.Ordinal);
        Assert.Contains("sql/hello-apexlang/import-hello-apexlang.sql", helloWorldScript, StringComparison.Ordinal);
        Assert.Contains("sql/hello-apexlang/export-hello-apexlang.sql", helloWorldScript, StringComparison.Ordinal);
        Assert.Contains("[oracle-sql] output_begin", helloWorldScript, StringComparison.Ordinal);
    }

    [Fact]
    public void OracleProvisioningScript_UsesBoundedXdbPollingDiagnosticsAndRepair()
    {
        var provider = new BuiltInCatalogProvider(TestPaths.CatalogRoot);
        var resolver = new WorkspaceResolver(provider.LoadFeatures(), provider.LoadServices(), provider.LoadCapabilities(), provider.LoadKnowledgePacks());
        var expander = new TemplateExpander();
        var resolved = resolver.Resolve(expander.Expand("oracle-apexlang", provider.LoadTemplates().Single(item => item.Id == "oracle-apexlang-demo")));

        var script = new OracleWorkspaceProvisioningScriptGenerator().Generate(resolved);

        Assert.Contains("oracle_xdb_wait_timeout_seconds=90", script, StringComparison.Ordinal);
        Assert.Contains("oracle_xdb_poll_interval_seconds=5", script, StringComparison.Ordinal);
        Assert.Contains("oracle_pdb_ready_timeout_seconds=180", script, StringComparison.Ordinal);
        Assert.Contains("oracle_root_container='CDB$ROOT'", script, StringComparison.Ordinal);
        Assert.Contains("oracle_target_container=${ORACLE_SERVICE_NAME}", script, StringComparison.Ordinal);
        Assert.Contains("query_xdb_registry_in_container", script, StringComparison.Ordinal);
        Assert.Contains("query_xdb_invalid_objects_in_container", script, StringComparison.Ordinal);
        Assert.Contains("query_xdb_errors_in_container", script, StringComparison.Ordinal);
        Assert.Contains("query_cdb_xdb_registry", script, StringComparison.Ordinal);
        Assert.Contains("query_xdb_sqlpatch", script, StringComparison.Ordinal);
        Assert.Contains("query_xdb_dbms_registry_status_in_container", script, StringComparison.Ordinal);
        Assert.Contains("query_xdb_functional_probe_in_container", script, StringComparison.Ordinal);
        Assert.Contains("query_pdb_plugin_violations", script, StringComparison.Ordinal);
        Assert.Contains("ensure_apex_xdb_registry_valid", script, StringComparison.Ordinal);
        Assert.Contains("Oracle APEX prerequisite validation failed.", script, StringComparison.Ordinal);
        Assert.Contains($"oracle_apex_recommended_database_image='{OracleDatabaseImageCatalog.DefaultDatabaseImage}'", script, StringComparison.Ordinal);
        Assert.Contains("Use Oracle image ${oracle_apex_recommended_database_image} for APEX workspaces", script, StringComparison.Ordinal);
        Assert.Contains("XDB was initially INVALID but became VALID while Oracle initialization completed.", script, StringComparison.Ordinal);
        Assert.Contains("query-script", script, StringComparison.Ordinal);
        Assert.Contains("PROMPT __OPENCODE_RESULT_BEGIN__", script, StringComparison.Ordinal);
        Assert.Contains("wait_for_xdb_ready || oracle_allow_invalid_xdb_if_functional", script, StringComparison.Ordinal);
        Assert.Contains("registry remained INVALID but XMLType/DBMS_XDB probes succeeded in root and pdb; continuing.", script, StringComparison.Ordinal);
        Assert.Contains("volume_state=${oracle_volume_state}", script, StringComparison.Ordinal);
        Assert.Contains("pdb_plug_in_violations=${violations:-none}", script, StringComparison.Ordinal);
        Assert.Contains("Investigate the Oracle XDB compilation errors or restore a known-good backup.", script, StringComparison.Ordinal);
        Assert.True(script.IndexOf("wait_for_xdb_ready || oracle_allow_invalid_xdb_if_functional", StringComparison.Ordinal) < script.IndexOf("oracle_set_stage 'Installing APEX'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScriptOracleWorkspaceProvisioner_PassesVolumeMetadataForXdbClassification()
    {
        var runtime = new RecordingContainerRuntime();
        var provisioner = new ScriptOracleWorkspaceProvisioner(runtime);
        var root = Path.Combine(Path.GetTempPath(), $"xdb-provisioner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var snapshot = new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = "oracle-apexlang-demo",
                RootPath = root,
                RepositoryPath = root,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            },
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "oracle-apexlang-demo", Image = "ubuntu:24.04" },
                Services = ["oracle-demo", "oracle-ords"],
            },
            Paths = WorkspacePathBuilder.Build(root),
            ConfigurationPath = "workspace.yaml",
            RuntimeState = WorkspaceRuntimeState.Running,
            Safety = new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.Protected,
                Headline = "Protected",
                Message = "Protected",
                LocalRecovery = new WorkspaceLocalRecoverySnapshot(),
                Backup = new WorkspaceBackupSnapshot(),
                IgnorePolicy = new WorkspaceIgnorePolicyReview(),
                AdvancedGit = new WorkspaceAdvancedGitSnapshot(),
            },
            Session = new WorkspaceSessionSnapshot { SessionName = "oracle-apexlang-demo", State = WorkspaceSessionState.Unknown },
            LocalRuntimeState = new WorkspaceRuntimeStateRecord
            {
                Resources = new WorkspaceManagedRuntimeResources(),
            },
        };

        await provisioner.ProvisionAsync(snapshot);

        Assert.Contains(runtime.LastArguments, item => string.Equals(item, "-e", StringComparison.Ordinal));
        Assert.Contains(runtime.LastArguments, item => string.Equals(item, "OPENCODE_ORACLE_VOLUME_STATE=new", StringComparison.Ordinal));
        Assert.Contains(runtime.LastArguments, item => string.Equals(item, "OPENCODE_ORACLE_VOLUME_RESET_ALLOWED=true", StringComparison.Ordinal));
        Assert.Contains(runtime.LastArguments, item => string.Equals(item, "OPENCODE_ORACLE_VOLUME_SCOPE=managed-workspace-exclusive", StringComparison.Ordinal));
        Assert.Contains(runtime.LastArguments, item => string.Equals(item, $"OPENCODE_ORACLE_DATABASE_IMAGE={OracleDatabaseImageCatalog.DefaultDatabaseImage}", StringComparison.Ordinal));
        Assert.Contains(runtime.LastArguments, item => string.Equals(item, "/opt/opencode-workspace/config/oracle-provision.sh", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScriptOracleWorkspaceProvisioner_UsesConfiguredOracleDatabaseImage()
    {
        var runtime = new RecordingContainerRuntime
        {
            ProvisionResults = [SuccessResult()],
        };
        var provisioner = new ScriptOracleWorkspaceProvisioner(runtime);
        var snapshot = CreateSnapshot(databaseImage: "gvenzl/oracle-free:23-slim-faststart");

        await provisioner.ProvisionAsync(snapshot);

        Assert.Contains(runtime.LastArguments, item => string.Equals(item, "OPENCODE_ORACLE_DATABASE_IMAGE=gvenzl/oracle-free:23-slim-faststart", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScriptOracleWorkspaceProvisioner_RunsUtlrpInOracleDemoServiceAndRetriesProvisioning()
    {
        var runtime = new RecordingContainerRuntime
        {
            ProvisionResults =
            [
                FailureResult($"Workspace provisioning stopped.{Environment.NewLine}Stage: Provisioning Oracle{Environment.NewLine}{XdbReasonLine}{Environment.NewLine}Evidence: XDB status=INVALID{Environment.NewLine}Recommended action: Investigate the Oracle XDB compilation errors or restore a known-good backup.{Environment.NewLine}Confidence: high"),
                SuccessResult(),
            ],
            ServiceContainerResult = SuccessResult("[oracle-server-maintenance] target_container=CDB$ROOT status=completed exit_code=0"),
        };
        var provisioner = new ScriptOracleWorkspaceProvisioner(runtime);

        await provisioner.ProvisionAsync(CreateSnapshot());

        Assert.Equal(2, runtime.WorkspaceProvisionCallCount);
        Assert.Equal(1, runtime.ServiceCommandCallCount);
        Assert.Equal(OracleWorkspaceFamily.OracleDatabaseServiceId, runtime.LastServiceName);
        Assert.Equal(DockerService.GetServiceContainerName(CreateSnapshot().Definition, OracleWorkspaceFamily.OracleDatabaseServiceId), runtime.LastServiceContainerName);
        Assert.Contains(runtime.LastServiceCommand, item => string.Equals(item, "bash", StringComparison.Ordinal));
        var commandText = runtime.LastServiceCommand.Last();
        Assert.Contains("target_project='oracle-apexlang-demo'", commandText, StringComparison.Ordinal);
        Assert.Contains("target_service='oracle-demo'", commandText, StringComparison.Ordinal);
        Assert.Contains("target_pdb='FREEPDB1'", commandText, StringComparison.Ordinal);
        Assert.Contains("run_utlrp 'CDB$ROOT'", commandText, StringComparison.Ordinal);
        Assert.Contains("run_utlrp \"$target_pdb\"", commandText, StringComparison.Ordinal);
        Assert.Contains("${ORACLE_HOME:-}", commandText, StringComparison.Ordinal);
        Assert.Contains("command -v sqlplus", commandText, StringComparison.Ordinal);
        Assert.Contains("$oracle_home/rdbms/admin/utlrp.sql", commandText, StringComparison.Ordinal);
        Assert.DoesNotContain("/opt/oracle/product", commandText, StringComparison.Ordinal);
        Assert.DoesNotContain("change-on-first-demo", commandText, StringComparison.Ordinal);
        Assert.DoesNotContain(runtime.LastServiceCommand, item => item.Contains("OPENCODE_ORACLE_DATABASE_IMAGE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScriptOracleWorkspaceProvisioner_MissingServerScript_ProducesPreciseFailure()
    {
        var runtime = new RecordingContainerRuntime
        {
            ProvisionResults =
            [
                FailureResult($"Workspace provisioning stopped.{Environment.NewLine}Stage: Provisioning Oracle{Environment.NewLine}{XdbReasonLine}{Environment.NewLine}Evidence: XDB status=INVALID{Environment.NewLine}Recommended action: Investigate the Oracle XDB compilation errors or restore a known-good backup.{Environment.NewLine}Confidence: high"),
            ],
            ServiceContainerResult = FailureResult("[oracle-server-maintenance] project=oracle-apexlang-demo service=oracle-demo container=oracle-apexlang-demo-oracle-demo-1 error=missing_utlrp target=/fake/rdbms/admin/utlrp.sql"),
        };
        var provisioner = new ScriptOracleWorkspaceProvisioner(runtime);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.ProvisionAsync(CreateSnapshot()));

        Assert.Contains("Reason: Oracle XML Database (XDB) recompilation could not start.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("error=missing_utlrp", exception.Message, StringComparison.Ordinal);
        Assert.Contains("service=oracle-demo", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("change-on-first-demo", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScriptOracleWorkspaceProvisioner_PersistentInvalidXdb_ReportsPostRepairFailure()
    {
        var runtime = new RecordingContainerRuntime
        {
            ProvisionResults =
            [
                FailureResult($"Workspace provisioning stopped.{Environment.NewLine}Stage: Provisioning Oracle{Environment.NewLine}{XdbReasonLine}{Environment.NewLine}Evidence: XDB status=INVALID{Environment.NewLine}Recommended action: Investigate the Oracle XDB compilation errors or restore a known-good backup.{Environment.NewLine}Confidence: high"),
                FailureResult($"Workspace provisioning stopped.{Environment.NewLine}Stage: Provisioning Oracle{Environment.NewLine}{XdbReasonLine}{Environment.NewLine}Evidence: containers=CDB$ROOT,FREEPDB1; invalid_object_count=2; invalid_objects=XDB|OBJ1|PACKAGE BODY|INVALID; dba_errors=XDB|OBJ1|PACKAGE BODY|1|1|PLS-00302; pdb_plug_in_violations=none; volume_state=new{Environment.NewLine}Recommended action: Reset Runtime.{Environment.NewLine}Confidence: high"),
            ],
            ServiceContainerResult = SuccessResult("[oracle-server-maintenance] target_container=FREEPDB1 status=completed exit_code=0"),
        };
        var provisioner = new ScriptOracleWorkspaceProvisioner(runtime);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.ProvisionAsync(CreateSnapshot()));

        Assert.Equal(2, runtime.WorkspaceProvisionCallCount);
        Assert.Contains("invalid_object_count=2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("PLS-00302", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Recommended action: Reset Runtime.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScriptOracleWorkspaceProvisioner_InvalidRegistryWithSuccessfulFunctionalProbes_DoesNotRunRepair()
    {
        var runtime = new RecordingContainerRuntime
        {
            ProvisionResults =
            [
                FailureResult($"Workspace provisioning stopped.{Environment.NewLine}Stage: Provisioning Oracle{Environment.NewLine}{XdbReasonLine}{Environment.NewLine}Evidence: root_registry=XDB|Oracle XML Database|23.0.0.0.0|INVALID|2026-07-14; pdb_registry=XDB|Oracle XML Database|23.0.0.0.0|INVALID|2026-07-14; invalid_object_count=0; dba_errors=none; pdb_plug_in_violations=none; root_functional_probe=XMLTYPE=ok|HTTPPORT=0; pdb_functional_probe=XMLTYPE=ok|HTTPPORT=0{Environment.NewLine}Recommended action: Investigate the Oracle XDB compilation errors or restore a known-good backup.{Environment.NewLine}Confidence: high"),
            ],
        };
        var provisioner = new ScriptOracleWorkspaceProvisioner(runtime);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.ProvisionAsync(CreateSnapshot()));

        Assert.Equal(1, runtime.WorkspaceProvisionCallCount);
        Assert.Equal(0, runtime.ServiceCommandCallCount);
        Assert.Contains("root_functional_probe=XMLTYPE=ok|HTTPPORT=0", exception.Message, StringComparison.Ordinal);
    }

    private const string XdbReasonLine = "Reason: Oracle XML Database (XDB) is invalid.";

    private static WorkspaceSnapshot CreateSnapshot(string? databaseImage = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"xdb-provisioner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return new WorkspaceSnapshot
        {
            Record = new WorkspaceRecord
            {
                Name = "oracle-apexlang-demo",
                RootPath = root,
                RepositoryPath = root,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastOpenedUtc = DateTimeOffset.UtcNow,
            },
            Definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "oracle-apexlang-demo", Image = "ubuntu:24.04" },
                Services = ["oracle-demo", "oracle-ords"],
                Oracle = new OracleWorkspacePreferences { DatabaseImage = databaseImage },
            },
            Paths = WorkspacePathBuilder.Build(root),
            ConfigurationPath = "workspace.yaml",
            RuntimeState = WorkspaceRuntimeState.Running,
            Safety = new WorkspaceSafetySnapshot
            {
                OverallStatus = WorkspaceSafetyLevel.Protected,
                Headline = "Protected",
                Message = "Protected",
                LocalRecovery = new WorkspaceLocalRecoverySnapshot(),
                Backup = new WorkspaceBackupSnapshot(),
                IgnorePolicy = new WorkspaceIgnorePolicyReview(),
                AdvancedGit = new WorkspaceAdvancedGitSnapshot(),
            },
            Session = new WorkspaceSessionSnapshot { SessionName = "oracle-apexlang-demo", State = WorkspaceSessionState.Unknown },
            LocalRuntimeState = new WorkspaceRuntimeStateRecord
            {
                Resources = new WorkspaceManagedRuntimeResources(),
            },
        };
    }

    private static ProcessResult SuccessResult(string output = "")
        => new()
        {
            Command = "docker exec",
            ExitCode = 0,
            StandardOutput = output,
            StandardError = string.Empty,
            StandardOutputLines = string.IsNullOrWhiteSpace(output) ? Array.Empty<string>() : output.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            StandardErrorLines = Array.Empty<string>(),
            Duration = TimeSpan.Zero,
        };

    private static ProcessResult FailureResult(string error)
        => new()
        {
            Command = "docker exec",
            ExitCode = 1,
            StandardOutput = string.Empty,
            StandardError = error,
            StandardOutputLines = Array.Empty<string>(),
            StandardErrorLines = error.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            Duration = TimeSpan.Zero,
        };

    private sealed class RecordingContainerRuntime : IContainerRuntime
    {
        public string RuntimeId => "docker";

        public List<string> LastArguments { get; } = [];
        public List<ProcessResult> ProvisionResults { get; init; } = [];
        public ProcessResult? ServiceContainerResult { get; set; }
        public int WorkspaceProvisionCallCount { get; private set; }
        public int ServiceCommandCallCount { get; private set; }
        public string LastServiceName { get; private set; } = string.Empty;
        public string LastServiceContainerName { get; private set; } = string.Empty;
        public List<string> LastServiceCommand { get; } = [];

        public string GetWorkspaceContainerName(WorkspaceDefinition definition) => "workspace-container";
        public string GetServiceContainerName(WorkspaceDefinition definition, string serviceName) => DockerService.GetServiceContainerName(definition, serviceName);

        public IReadOnlyList<string> CreatePermissionRepairArguments(string workspaceRootPath) => [];

        public Task<ProcessResult> RunSimpleDockerCommandAsync(IEnumerable<string> arguments, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        {
            LastArguments.Clear();
            LastArguments.AddRange(arguments);
            WorkspaceProvisionCallCount++;
            if (ProvisionResults.Count >= WorkspaceProvisionCallCount)
            {
                return Task.FromResult(ProvisionResults[WorkspaceProvisionCallCount - 1]);
            }

            return Task.FromResult(SuccessResult());
        }

        public Task<ProcessResult> StartAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotSupportedException();
        public Task<ProcessResult> ValidateAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotSupportedException();
        public Task<ProcessResult> StopAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> RemoveAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotSupportedException();
        public Task<ProcessResult> ResetAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default, Func<CancellationToken, Task<bool>>? repairComposeAsync = null) => throw new NotSupportedException();
        public Task<ProcessResult?> ValidateVolatileEnvironmentAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> GetPsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> GetComposePsAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> GetServiceLogsAsync(WorkspacePaths paths, WorkspaceDefinition definition, string serviceName, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> RestartServiceAsync(WorkspacePaths paths, WorkspaceDefinition definition, string serviceName, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> RunProvisionScriptAsync(WorkspaceDefinition definition, WorkspacePaths paths, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> RepairOracleOrdsGatewayAsync(WorkspacePaths paths, WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> ProbeHttpGetFromWorkspaceAsync(WorkspaceDefinition definition, string url, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> InspectContainerImageAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> InspectImageRepoTagsAsync(string imageId, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> GetNodeToolDiagnosticsAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> GetNodeAptPolicyAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> GetOsReleaseAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> CheckOpencodeUserAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> EnsureOpencodeUserDirectoriesAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> NormalizeWorkspaceFilePermissionsAsync(string workspaceRootPath, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> RunCommandInServiceContainerAsync(WorkspaceDefinition definition, string serviceName, IEnumerable<string> commandArguments, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
        {
            ServiceCommandCallCount++;
            LastServiceName = serviceName;
            LastServiceContainerName = GetServiceContainerName(definition, serviceName);
            LastServiceCommand.Clear();
            LastServiceCommand.AddRange(commandArguments);
            return Task.FromResult(ServiceContainerResult ?? SuccessResult());
        }
        public Task<ProcessResult> ListOpenCodeSessionsAsync(WorkspaceDefinition definition, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> ExportOpenCodeSessionAsync(WorkspaceDefinition definition, string sessionId, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
