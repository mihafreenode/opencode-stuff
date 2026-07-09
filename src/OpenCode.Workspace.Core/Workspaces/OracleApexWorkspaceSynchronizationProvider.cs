using System.Security.Cryptography;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class OracleApexWorkspaceSynchronizationProvider : IWorkspaceSynchronizationProvider
    , IOracleApexWorkspaceConnectionProvider
{
    private readonly WorkspaceSynchronizationStateService _stateService;
    private readonly IContainerRuntime _containerRuntime;
    private readonly IProcessRunner _processRunner;
    private readonly WorkspaceYamlService _workspaceYamlService;

    public OracleApexWorkspaceSynchronizationProvider(
        WorkspaceSynchronizationStateService stateService,
        IContainerRuntime containerRuntime,
        IProcessRunner processRunner,
        WorkspaceYamlService workspaceYamlService)
    {
        _stateService = stateService;
        _containerRuntime = containerRuntime;
        _processRunner = processRunner;
        _workspaceYamlService = workspaceYamlService;
    }

    public string ProviderId => "oracle-apex";

    public bool CanHandle(WorkspaceDefinition definition)
        => OracleWorkspaceFamily.HasApex(definition) && definition.Oracle.Apex.Environments.Count > 0;

    public Task<WorkspaceSynchronizationStatusResult> GetStatusAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default)
    {
        var snapshot = BuildSnapshot(request.Snapshot, ReadState(request.Snapshot.Paths));
        return Task.FromResult(new WorkspaceSynchronizationStatusResult { Snapshot = snapshot });
    }

    public async Task<OracleApexApplicationDiscoveryResult> DiscoverApplicationsAsync(OracleApexApplicationDiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        var environment = NormalizeEnvironment(request.EnvironmentName, request.WorkspaceName, request.ParsingSchema, request.SqlclProfile, request.SourcePath);
        await EnsureSqlclAvailableAsync(request.Snapshot, cancellationToken).ConfigureAwait(false);
        await EnsureApexAvailableAsync(request.Snapshot, environment, cancellationToken).ConfigureAwait(false);
        await EnsureSchemaExistsAsync(request.Snapshot, environment, cancellationToken).ConfigureAwait(false);
        await EnsureWorkspaceMappingExistsAsync(request.Snapshot, environment, cancellationToken).ConfigureAwait(false);

        var query = $"""
SET HEADING OFF
SET FEEDBACK OFF
SET PAGESIZE 0
SET VERIFY OFF
SET TRIMSPOOL ON
SELECT application_id || '|' || application_name || '|' || NVL(alias, '')
FROM apex_applications
WHERE workspace = '{EscapeSqlLiteral(environment.Workspace)}'
ORDER BY application_id;
EXIT
""";

        var result = await RunSqlclAsync(request.Snapshot, environment, query, cancellationToken).ConfigureAwait(false);
        EnsureSqlSuccess(result, $"Listing Oracle APEX applications failed for workspace '{environment.Workspace}'.");

        var applications = result.StandardOutputLines
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line) && line.Contains('|', StringComparison.Ordinal))
            .Select(ParseApplicationInfo)
            .Where(item => item is not null)
            .Cast<OracleApexApplicationInfo>()
            .ToList();

        return new OracleApexApplicationDiscoveryResult
        {
            EnvironmentName = environment.EnvironmentName,
            WorkspaceName = environment.Workspace,
            ParsingSchema = environment.ParsingSchema,
            SqlclProfile = environment.SqlclProfile,
            SourcePath = environment.SourcePath,
            Applications = applications,
            Summary = applications.Count == 0
                ? $"No Oracle APEX applications were found in workspace '{environment.Workspace}'."
                : $"Found {applications.Count} Oracle APEX application(s) in workspace '{environment.Workspace}'.",
        };
    }

    public async Task<OracleApexConnectExistingApplicationResult> ConnectExistingApplicationAsync(OracleApexConnectExistingApplicationRequest request, CancellationToken cancellationToken = default)
    {
        var discovery = await DiscoverApplicationsAsync(new OracleApexApplicationDiscoveryRequest
        {
            Snapshot = request.Snapshot,
            EnvironmentName = request.EnvironmentName,
            WorkspaceName = request.WorkspaceName,
            ParsingSchema = request.ParsingSchema,
            SqlclProfile = request.SqlclProfile,
            SourcePath = request.SourcePath,
        }, cancellationToken).ConfigureAwait(false);

        var application = discovery.Applications.FirstOrDefault(item => item.ApplicationId == request.ApplicationId);
        if (application is null)
        {
            throw new InvalidOperationException($"Oracle APEX application '{request.ApplicationId}' was not found in workspace '{discovery.WorkspaceName}'.");
        }

        var updatedDefinition = BuildConnectedDefinition(request.Snapshot.Definition, discovery, application);
        _workspaceYamlService.WriteToFile(request.Snapshot.Paths.WorkspaceYamlPath, updatedDefinition);
        Directory.CreateDirectory(ResolveSourcePath(request.Snapshot.Paths.RootPath, discovery.SourcePath));

        var state = UpdateEnvironmentState(new WorkspaceSynchronizationStateDocument(), discovery.EnvironmentName, current => current with
        {
            SynchronizationState = WorkspaceSynchronizationState.Unknown.ToString(),
            DriftSummary = string.Empty,
            LastValidation = null,
            LastImport = null,
            LastExport = null,
            LastPull = null,
            LastPush = null,
            ImportedRevision = string.Empty,
            ExportedRevision = string.Empty,
            LastSynchronizedGitRevision = string.Empty,
            ApplicationName = application.ApplicationName,
            LastPushResult = string.Empty,
            LastImportedRevision = string.Empty,
            LastExportedRevision = string.Empty,
            OperationHistory = [],
        });
        _stateService.Write(request.Snapshot.Paths.ApexMetadataPath, state);

        var connectedSnapshot = WithDefinition(request.Snapshot, updatedDefinition, BuildSnapshot(WithDefinition(request.Snapshot, updatedDefinition, new WorkspaceSynchronizationSnapshot()), state));
        var exportResult = await ExportAsync(new WorkspaceSynchronizationRequest { Snapshot = connectedSnapshot, EnvironmentName = discovery.EnvironmentName }, cancellationToken).ConfigureAwait(false);
        EnsureOperationSuccess(exportResult, $"Export failed after connecting Oracle APEX application '{application.ApplicationName}'.");

        var validateResult = await ValidateAsync(new WorkspaceSynchronizationRequest { Snapshot = WithDefinition(connectedSnapshot, updatedDefinition, exportResult.Snapshot), EnvironmentName = discovery.EnvironmentName }, cancellationToken).ConfigureAwait(false);
        EnsureOperationSuccess(validateResult, $"Validation failed after connecting Oracle APEX application '{application.ApplicationName}'.");

        return new OracleApexConnectExistingApplicationResult
        {
            Snapshot = WithDefinition(request.Snapshot, updatedDefinition, validateResult.Snapshot),
            Message = $"Connected Oracle APEX application '{application.ApplicationName}' ({application.ApplicationId}) for environment '{discovery.EnvironmentName}', exported it to '{discovery.SourcePath}', and validated the exported source.",
            ProcessResults = [exportResult.ProcessResult!, validateResult.ProcessResult!],
        };
    }

    public async Task<WorkspaceSynchronizationOperationResult> ValidateAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default)
    {
        var environment = ResolveEnvironment(request.Snapshot.Definition, request.EnvironmentName);
        var sourcePath = ResolveSourcePath(request.Snapshot.Paths.RootPath, environment.SourcePath);
        var applicationFile = Path.Combine(sourcePath, "application.apx");
        var result = await _processRunner.RunAsync(
            "bash",
            [Path.Combine(request.Snapshot.Paths.RootPath, "scripts", "validate-apex.sh"), applicationFile],
            request.Snapshot.Paths.RootPath,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var state = ReadState(request.Snapshot.Paths);
        if (!result.IsSuccess)
        {
            state = UpdateEnvironmentState(state, environment.EnvironmentName, current => current with
            {
                LastValidation = CreateOperationState(result, request.Snapshot),
                SynchronizationState = WorkspaceSynchronizationState.ValidationFailed.ToString(),
            });
            _stateService.Write(request.Snapshot.Paths.ApexMetadataPath, state);

            return new WorkspaceSynchronizationOperationResult
            {
                Snapshot = BuildSnapshot(request.Snapshot, state),
                Message = $"APEX validation failed for environment '{environment.EnvironmentName}'.",
                ProcessResult = result,
            };
        }

        var validationExportRoot = Path.Combine(request.Snapshot.Paths.OpencodePath, "apex", "validate", environment.EnvironmentName);
        var remoteResult = await ExportEnvironmentAsync(request.Snapshot, environment, cancellationToken, validationExportRoot).ConfigureAwait(false);
        var updatedState = state;
        if (!remoteResult.IsSuccess)
        {
            updatedState = UpdateEnvironmentState(updatedState, environment.EnvironmentName, current => current with
            {
                LastValidation = CreateOperationState(remoteResult, request.Snapshot),
                SynchronizationState = WorkspaceSynchronizationState.Unknown.ToString(),
            });
            _stateService.Write(request.Snapshot.Paths.ApexMetadataPath, updatedState);

            return new WorkspaceSynchronizationOperationResult
            {
                Snapshot = BuildSnapshot(request.Snapshot, updatedState),
                Message = $"Oracle APEX validation could not determine deployment state for environment '{environment.EnvironmentName}'.",
                ProcessResult = remoteResult,
            };
        }

        var remotePath = NormalizeExportRoot(validationExportRoot);
        var sourceSignature = Directory.Exists(sourcePath) ? ComputeDirectorySignature(sourcePath) : string.Empty;
        var remoteSignature = Directory.Exists(remotePath) ? ComputeDirectorySignature(remotePath) : string.Empty;
        var baselineState = state.Environments.TryGetValue(environment.EnvironmentName, out var storedState) ? storedState : new WorkspaceSynchronizationEnvironmentState();
        var syncState = DetermineSyncState(sourceSignature, remoteSignature, baselineState);
        var driftSummary = BuildDriftSummary(syncState, environment.EnvironmentName);
        updatedState = UpdateEnvironmentState(updatedState, environment.EnvironmentName, current => current with
        {
            LastValidation = CreateOperationState(result, request.Snapshot),
            SynchronizationState = syncState.ToString(),
            DriftSummary = driftSummary,
            WorkspaceSourceSignature = sourceSignature,
            RemoteSourceSignature = remoteSignature,
        });
        if (syncState == WorkspaceSynchronizationState.InSync)
        {
            updatedState = UpdateEnvironmentState(updatedState, environment.EnvironmentName, current => current with
            {
                SynchronizedSourceSignature = sourceSignature,
            });
        }
        _stateService.Write(request.Snapshot.Paths.ApexMetadataPath, updatedState);

        return new WorkspaceSynchronizationOperationResult
        {
            Snapshot = BuildSnapshot(request.Snapshot, updatedState),
            Message = $"Validated APEX source for environment '{environment.EnvironmentName}'. Current state: {syncState}.",
            ProcessResult = result,
        };
    }

    public async Task<WorkspaceSynchronizationOperationResult> ExportAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default)
    {
        var environment = ResolveEnvironment(request.Snapshot.Definition, request.EnvironmentName);
        var result = await ExportEnvironmentAsync(request.Snapshot, environment, cancellationToken).ConfigureAwait(false);
        var newState = ReadState(request.Snapshot.Paths);
        var sourceRevision = request.Snapshot.Safety.AdvancedGit.LatestCommitSha;
        var exportedRevision = result.IsSuccess ? ComputeDirectorySignature(ResolveSourcePath(request.Snapshot.Paths.RootPath, environment.SourcePath)) : string.Empty;
        newState = UpdateEnvironmentState(newState, environment.EnvironmentName, current => current with
        {
            LastExport = CreateOperationState(result, request.Snapshot),
            ExportedRevision = sourceRevision,
            SynchronizationState = result.IsSuccess ? WorkspaceSynchronizationState.InSync.ToString() : current.SynchronizationState,
            DriftSummary = result.IsSuccess ? "Oracle APEX export refreshed the workspace source tree." : current.DriftSummary,
            LastExportedRevision = result.IsSuccess ? exportedRevision : current.LastExportedRevision,
            SynchronizedSourceSignature = result.IsSuccess ? exportedRevision : current.SynchronizedSourceSignature,
            WorkspaceSourceSignature = result.IsSuccess ? exportedRevision : current.WorkspaceSourceSignature,
            RemoteSourceSignature = result.IsSuccess ? exportedRevision : current.RemoteSourceSignature,
            OperationHistory = result.IsSuccess
                ? AppendHistory(current.OperationHistory, "Export", "Succeeded", WorkspaceSynchronizationState.InSync, request.Snapshot.Safety.AdvancedGit.LatestCommitSha, exportedRevision, "Exported Oracle APEX changes into workspace source.")
                : current.OperationHistory,
        });
        _stateService.Write(request.Snapshot.Paths.ApexMetadataPath, newState);

        return new WorkspaceSynchronizationOperationResult
        {
            Snapshot = BuildSnapshot(request.Snapshot, newState),
            Message = result.IsSuccess ? $"Exported Oracle APEX application for environment '{environment.EnvironmentName}'." : $"Oracle APEX export failed for environment '{environment.EnvironmentName}'.",
            ProcessResult = result,
        };
    }

    public async Task<WorkspaceSynchronizationOperationResult> ImportAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default)
    {
        var environment = ResolveEnvironment(request.Snapshot.Definition, request.EnvironmentName);
        var result = await ImportEnvironmentAsync(request.Snapshot, environment, cancellationToken).ConfigureAwait(false);
        var newState = ReadState(request.Snapshot.Paths);
        var sourceRevision = request.Snapshot.Safety.AdvancedGit.LatestCommitSha;
        var importedRevision = result.IsSuccess ? ComputeDirectorySignature(ResolveSourcePath(request.Snapshot.Paths.RootPath, environment.SourcePath)) : string.Empty;
        newState = UpdateEnvironmentState(newState, environment.EnvironmentName, current => current with
        {
            LastImport = CreateOperationState(result, request.Snapshot),
            ImportedRevision = sourceRevision,
            LastSynchronizedGitRevision = result.IsSuccess ? sourceRevision : current.LastSynchronizedGitRevision,
            SynchronizationState = result.IsSuccess ? WorkspaceSynchronizationState.InSync.ToString() : current.SynchronizationState,
            DriftSummary = result.IsSuccess ? string.Empty : current.DriftSummary,
            LastImportedRevision = result.IsSuccess ? importedRevision : current.LastImportedRevision,
            SynchronizedSourceSignature = result.IsSuccess ? importedRevision : current.SynchronizedSourceSignature,
            WorkspaceSourceSignature = result.IsSuccess ? importedRevision : current.WorkspaceSourceSignature,
            RemoteSourceSignature = result.IsSuccess ? importedRevision : current.RemoteSourceSignature,
        });
        _stateService.Write(request.Snapshot.Paths.ApexMetadataPath, newState);

        return new WorkspaceSynchronizationOperationResult
        {
            Snapshot = BuildSnapshot(request.Snapshot, newState),
            Message = result.IsSuccess ? $"Imported workspace source into Oracle APEX for environment '{environment.EnvironmentName}'." : $"Oracle APEX import failed for environment '{environment.EnvironmentName}'.",
            ProcessResult = result,
        };
    }

    public async Task<WorkspaceSynchronizationDiffResult> DiffAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default)
    {
        var environment = ResolveEnvironment(request.Snapshot.Definition, request.EnvironmentName);
        var diffRoot = Path.Combine(request.Snapshot.Paths.OpencodePath, "apex", "diff", environment.EnvironmentName);
        if (Directory.Exists(diffRoot))
        {
            Directory.Delete(diffRoot, recursive: true);
        }

        Directory.CreateDirectory(diffRoot);
        var exportResult = await ExportEnvironmentAsync(request.Snapshot, environment, cancellationToken, diffRoot).ConfigureAwait(false);
        var sourcePath = ResolveSourcePath(request.Snapshot.Paths.RootPath, environment.SourcePath);
        var remoteSource = NormalizeExportRoot(diffRoot);
        var diffText = exportResult.IsSuccess
            ? await BuildDetailedDiff(request.Snapshot, environment, sourcePath, remoteSource).ConfigureAwait(false)
            : string.Join(Environment.NewLine, exportResult.StandardErrorLines.Concat(exportResult.StandardOutputLines));
        var status = BuildSnapshot(request.Snapshot, ReadState(request.Snapshot.Paths));
        return new WorkspaceSynchronizationDiffResult
        {
            Snapshot = status,
            Summary = string.IsNullOrWhiteSpace(diffText) ? "No differences were detected between workspace source and exported Oracle APEX source." : "Differences were detected between workspace source and exported Oracle APEX source.",
            DiffText = diffText,
        };
    }

    public async Task<WorkspaceSynchronizationOperationResult> PullAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default)
    {
        var result = await ExportAsync(request, cancellationToken).ConfigureAwait(false);
        var environmentName = string.IsNullOrWhiteSpace(request.EnvironmentName) ? ResolveEnvironment(request.Snapshot.Definition, null).EnvironmentName : request.EnvironmentName!;
        var contentRevision = result.Snapshot.DefaultEnvironment?.WorkspaceSourceSignature ?? string.Empty;
        var document = ReadState(request.Snapshot.Paths);
        document = UpdateEnvironmentState(document, environmentName, current => current with
        {
            LastPull = new WorkspaceSynchronizationOperationState
            {
                Status = result.ProcessResult?.IsSuccess == false ? "Failed" : "Succeeded",
                Revision = request.Snapshot.Safety.AdvancedGit.LatestCommitSha,
                TimestampUtc = DateTimeOffset.UtcNow,
                Summary = SanitizeSummary(result.Message),
            },
            OperationHistory = result.ProcessResult?.IsSuccess == false
                ? current.OperationHistory
                : AppendHistory(current.OperationHistory, "Pull", "Succeeded", result.Snapshot.State, request.Snapshot.Safety.AdvancedGit.LatestCommitSha, contentRevision, result.Message),
        });
        _stateService.Write(request.Snapshot.Paths.ApexMetadataPath, document);
        var finalSnapshot = BuildSnapshot(request.Snapshot, document);
        return new WorkspaceSynchronizationOperationResult
        {
            Snapshot = finalSnapshot,
            Message = request.EnvironmentName is { Length: > 0 }
                ? $"Pulled Oracle APEX changes into workspace source for environment '{request.EnvironmentName}'."
                : "Pulled Oracle APEX changes into workspace source.",
            ProcessResult = result.ProcessResult,
        };
    }

    public async Task<WorkspaceSynchronizationOperationResult> PushAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default)
    {
        var environmentName = string.IsNullOrWhiteSpace(request.EnvironmentName) ? ResolveEnvironment(request.Snapshot.Definition, null).EnvironmentName : request.EnvironmentName!;
        var startingState = ReadState(request.Snapshot.Paths).Environments.TryGetValue(environmentName, out var existingState)
            ? ParseState(existingState.SynchronizationState)
            : WorkspaceSynchronizationState.Unknown;

        var validation = await ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        var validatedState = validation.Snapshot.DefaultEnvironment?.State ?? validation.Snapshot.State;
        if (validation.ProcessResult?.IsSuccess == false || validatedState == WorkspaceSynchronizationState.ValidationFailed)
        {
            var failedState = RecordPushAttempt(request.Snapshot.Paths, environmentName, request.Snapshot.Safety.AdvancedGit.LatestCommitSha, validatedState, false, validation.Message, validation.Snapshot.DefaultEnvironment?.WorkspaceSourceSignature ?? string.Empty);
            return new WorkspaceSynchronizationOperationResult
            {
                Snapshot = BuildSnapshot(request.Snapshot, failedState),
                Message = string.Join(Environment.NewLine,
                [
                    "Validation started",
                    validation.Message,
                    "Push aborted",
                    "Synchronization metadata updated",
                    $"Final sync state: {validatedState}",
                ]),
                ProcessResult = validation.ProcessResult,
            };
        }

        if (validatedState == WorkspaceSynchronizationState.DeploymentAhead || validatedState == WorkspaceSynchronizationState.Unknown)
        {
            var blockedState = ForceValidationFailedState(request.Snapshot.Paths, environmentName, request.Snapshot.Safety.AdvancedGit.LatestCommitSha, validatedState == WorkspaceSynchronizationState.DeploymentAhead
                ? "Oracle APEX contains newer Builder changes. Pull Changes before pushing Git-managed source."
                : "Synchronization state is unknown. Validate and review drift before pushing Git-managed source.");
            return new WorkspaceSynchronizationOperationResult
            {
                Snapshot = BuildSnapshot(request.Snapshot, blockedState),
                Message = string.Join(Environment.NewLine,
                [
                    "Validation started",
                    "Validation succeeded",
                    validatedState == WorkspaceSynchronizationState.DeploymentAhead
                        ? "Push aborted because Oracle APEX is ahead. Pull Changes first."
                        : "Push aborted because synchronization state is unknown.",
                    "Synchronization metadata updated",
                    "Final sync state: ValidationFailed",
                ]),
                ProcessResult = validation.ProcessResult,
            };
        }

        var importResult = await ImportAsync(request, cancellationToken).ConfigureAwait(false);
        var importedSignature = importResult.Snapshot.DefaultEnvironment?.WorkspaceSourceSignature ?? string.Empty;
        var finalStateDocument = RecordPushAttempt(request.Snapshot.Paths, environmentName, request.Snapshot.Safety.AdvancedGit.LatestCommitSha, importResult.Snapshot.State, importResult.ProcessResult?.IsSuccess != false, importResult.Message, importedSignature);
        var finalSnapshot = BuildSnapshot(request.Snapshot, finalStateDocument);
        return new WorkspaceSynchronizationOperationResult
        {
            Snapshot = finalSnapshot,
            Message = string.Join(Environment.NewLine,
            [
                "Validation started",
                "Validation succeeded",
                $"Importing application into Oracle APEX for environment '{environmentName}'",
                importResult.ProcessResult?.IsSuccess == true ? "Import completed" : "Import failed",
                "Synchronization metadata updated",
                $"Final sync state: {finalSnapshot.State}",
            ]),
            ProcessResult = importResult.ProcessResult,
        };
    }

    private WorkspaceSynchronizationStateDocument ReadState(WorkspacePaths paths)
        => _stateService.Read(paths.ApexMetadataPath) ?? new WorkspaceSynchronizationStateDocument();

    private async Task<ProcessResult> ExportEnvironmentAsync(WorkspaceSnapshot snapshot, OracleApexEnvironmentContext environment, CancellationToken cancellationToken, string? targetPathOverride = null)
    {
        await EnsureSqlclAvailableAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await EnsureApexAvailableAsync(snapshot, environment, cancellationToken).ConfigureAwait(false);
        await EnsureSchemaExistsAsync(snapshot, environment, cancellationToken).ConfigureAwait(false);
        await EnsureWorkspaceMappingExistsAsync(snapshot, environment, cancellationToken).ConfigureAwait(false);

        var exportRoot = targetPathOverride ?? ResolveSourcePath(snapshot.Paths.RootPath, environment.SourcePath);
        var tempExportParent = targetPathOverride ?? Path.Combine(snapshot.Paths.OpencodePath, "apex", "export", environment.EnvironmentName);
        ResetDirectory(tempExportParent);

        var sql = $"""
apex export -applicationid {environment.ApplicationId} -split -exptype APEXLANG -dir /workspace/{GetWorkspaceRelativePath(snapshot.Paths.RootPath, tempExportParent)} -force
exit
""";
        var result = await RunSqlclAsync(snapshot, environment, sql, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return result;
        }

        var normalizedExportRoot = NormalizeExportRoot(tempExportParent);
        if (!Directory.Exists(normalizedExportRoot) || !File.Exists(Path.Combine(normalizedExportRoot, "application.apx")))
        {
            throw new InvalidOperationException($"Oracle APEX export completed but no valid APEXlang package was produced for application '{environment.ApplicationId}'.");
        }

        if (targetPathOverride is null)
        {
            ReplaceDirectoryContents(normalizedExportRoot, exportRoot);
        }

        return result;
    }

    private async Task<ProcessResult> ImportEnvironmentAsync(WorkspaceSnapshot snapshot, OracleApexEnvironmentContext environment, CancellationToken cancellationToken)
    {
        await EnsureSqlclAvailableAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await EnsureApexAvailableAsync(snapshot, environment, cancellationToken).ConfigureAwait(false);
        await EnsureSchemaExistsAsync(snapshot, environment, cancellationToken).ConfigureAwait(false);
        await EnsureWorkspaceMappingExistsAsync(snapshot, environment, cancellationToken).ConfigureAwait(false);

        var sourcePath = ResolveSourcePath(snapshot.Paths.RootPath, environment.SourcePath);
        if (!File.Exists(Path.Combine(sourcePath, "application.apx")))
        {
            throw new InvalidOperationException($"Oracle APEX source path '{environment.SourcePath}' does not contain application.apx. Export or create source before importing.");
        }

        var sql = $"""
apex import -workspace {environment.Workspace} -schema {environment.ParsingSchema} -id {environment.ApplicationId} -input /workspace/{GetWorkspaceRelativePath(snapshot.Paths.RootPath, sourcePath)}
exit
""";
        return await RunSqlclAsync(snapshot, environment, sql, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureSqlclAvailableAsync(WorkspaceSnapshot snapshot, CancellationToken cancellationToken)
    {
        var result = await RunInWorkspaceAsync(snapshot, "scripts/sqlcl.sh -version", cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException("SQLcl is unavailable in this workspace. Run scripts/sqlcl.sh -version for diagnostics.");
        }
    }

    private async Task EnsureApexAvailableAsync(WorkspaceSnapshot snapshot, OracleApexEnvironmentContext environment, CancellationToken cancellationToken)
    {
        var result = await RunSqlclAsync(snapshot, environment, "select version_no from apex_release;\nexit", cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.StandardOutputLines.All(line => string.IsNullOrWhiteSpace(line) || !char.IsDigit(line.Trim().FirstOrDefault())))
        {
            throw new InvalidOperationException("Oracle APEX is unavailable in this workspace. Verify ORDS/APEX provisioning before connecting an application.");
        }
    }

    private async Task EnsureSchemaExistsAsync(WorkspaceSnapshot snapshot, OracleApexEnvironmentContext environment, CancellationToken cancellationToken)
    {
        var query = $"""
SET HEADING OFF
SET FEEDBACK OFF
SET PAGESIZE 0
SELECT username FROM all_users WHERE username = UPPER('{EscapeSqlLiteral(environment.ParsingSchema)}');
EXIT
""";
        var result = await RunSqlclAsync(snapshot, environment, query, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.StandardOutputLines.All(line => !string.Equals(line.Trim(), environment.ParsingSchema, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Parsing schema '{environment.ParsingSchema}' was not found in Oracle.");
        }
    }

    private async Task EnsureWorkspaceMappingExistsAsync(WorkspaceSnapshot snapshot, OracleApexEnvironmentContext environment, CancellationToken cancellationToken)
    {
        var query = $"""
SET HEADING OFF
SET FEEDBACK OFF
SET PAGESIZE 0
SELECT workspace_name || '|' || schema
FROM apex_workspace_schemas
WHERE workspace_name = '{EscapeSqlLiteral(environment.Workspace)}'
  AND schema = '{EscapeSqlLiteral(environment.ParsingSchema)}';
EXIT
""";
        var result = await RunSqlclAsync(snapshot, environment, query, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.StandardOutputLines.All(line => !string.Equals(line.Trim(), $"{environment.Workspace}|{environment.ParsingSchema}", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Oracle APEX workspace '{environment.Workspace}' is missing or is not mapped to parsing schema '{environment.ParsingSchema}'.");
        }
    }

    private async Task<ProcessResult> RunSqlclAsync(WorkspaceSnapshot snapshot, OracleApexEnvironmentContext environment, string sql, CancellationToken cancellationToken)
    {
        var queriesPath = Path.Combine(snapshot.Paths.OpencodePath, "apex", "queries");
        Directory.CreateDirectory(queriesPath);
        var fileName = $"{environment.EnvironmentName}-{Guid.NewGuid():N}.sql";
        var hostSqlPath = Path.Combine(queriesPath, fileName);
        var workspaceSqlPath = $"/workspace/.opencode/apex/queries/{fileName}";
        File.WriteAllText(hostSqlPath, sql.Replace("\r\n", "\n", StringComparison.Ordinal));

        try
        {
            var command = $"connection=\"{BuildConnectionString(environment)}\"; scripts/sqlcl.sh -S \"$connection\" @\"{workspaceSqlPath}\"";
            return await RunInWorkspaceAsync(snapshot, command, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(hostSqlPath);
        }
    }

    private WorkspaceSynchronizationSnapshot BuildSnapshot(WorkspaceSnapshot workspaceSnapshot, WorkspaceSynchronizationStateDocument state)
    {
        var environments = workspaceSnapshot.Definition.Oracle.Apex.Environments
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => BuildEnvironmentSnapshot(workspaceSnapshot, pair.Key, pair.Value, state.Environments.TryGetValue(pair.Key, out var environmentState) ? environmentState : null))
            .ToList();
        var defaultEnvironmentName = string.IsNullOrWhiteSpace(workspaceSnapshot.Definition.Oracle.Apex.DefaultEnvironment)
            ? state.DefaultEnvironment
            : workspaceSnapshot.Definition.Oracle.Apex.DefaultEnvironment;
        var defaultEnvironment = environments.FirstOrDefault(item => string.Equals(item.EnvironmentName, defaultEnvironmentName, StringComparison.OrdinalIgnoreCase))
            ?? environments.FirstOrDefault();
        var overallState = environments.Select(item => item.State).DefaultIfEmpty(WorkspaceSynchronizationState.Unknown).MaxBy(GetStateRank);

        return new WorkspaceSynchronizationSnapshot
        {
            IsSupported = environments.Count > 0,
            State = overallState,
            Summary = BuildSynchronizationSummary(overallState, defaultEnvironment),
            RequiresExplicitDecision = overallState == WorkspaceSynchronizationState.Diverged,
            HasDrift = overallState is WorkspaceSynchronizationState.GitAhead or WorkspaceSynchronizationState.DeploymentAhead or WorkspaceSynchronizationState.Diverged,
            Environments = environments,
            DefaultEnvironment = defaultEnvironment,
        };
    }

    private WorkspaceSynchronizationEnvironmentSnapshot BuildEnvironmentSnapshot(WorkspaceSnapshot workspaceSnapshot, string environmentName, OracleApexEnvironmentPreferences environment, WorkspaceSynchronizationEnvironmentState? state)
    {
        state ??= new WorkspaceSynchronizationEnvironmentState();
        var currentGitRevision = workspaceSnapshot.Safety.AdvancedGit.LatestCommitSha;
        var hasUncommittedChanges = workspaceSnapshot.Safety.LocalRecovery.HasUncommittedChanges || workspaceSnapshot.Safety.LocalRecovery.UntrackedFileCount > 0;
        var persistedState = ParseState(state.SynchronizationState);
        var effectiveState = persistedState;

        if (persistedState == WorkspaceSynchronizationState.ValidationFailed)
        {
            effectiveState = WorkspaceSynchronizationState.ValidationFailed;
        }
        else if (!string.IsNullOrWhiteSpace(state.LastSynchronizedGitRevision) && !string.Equals(state.LastSynchronizedGitRevision, currentGitRevision, StringComparison.Ordinal))
        {
            effectiveState = hasUncommittedChanges
                ? WorkspaceSynchronizationState.Diverged
                : WorkspaceSynchronizationState.GitAhead;
        }
        else if (persistedState == WorkspaceSynchronizationState.DeploymentAhead || (hasUncommittedChanges && state.LastExport?.TimestampUtc is not null && (state.LastImport?.TimestampUtc is null || state.LastExport.TimestampUtc > state.LastImport.TimestampUtc)))
        {
            effectiveState = WorkspaceSynchronizationState.DeploymentAhead;
        }
        else if (!string.IsNullOrWhiteSpace(state.LastSynchronizedGitRevision))
        {
            effectiveState = WorkspaceSynchronizationState.InSync;
        }

        return new WorkspaceSynchronizationEnvironmentSnapshot
        {
            EnvironmentName = environmentName,
            WorkspaceName = environment.Workspace ?? string.Empty,
            ParsingSchema = environment.ParsingSchema ?? string.Empty,
            ApplicationId = environment.ApplicationId,
            ApplicationName = state.ApplicationName,
            SqlclProfile = environment.SqlclProfile ?? string.Empty,
            SyncMode = WorkspaceSynchronizationModes.Normalize(environment.SyncMode),
            SourcePath = environment.SourcePath ?? "src/apex",
            State = effectiveState,
            Summary = BuildEnvironmentSummary(effectiveState, environmentName),
            DriftSummary = state.DriftSummary,
            LastValidationUtc = state.LastValidation?.TimestampUtc,
            LastImportUtc = state.LastImport?.TimestampUtc,
            LastExportUtc = state.LastExport?.TimestampUtc,
            LastPullUtc = state.LastPull?.TimestampUtc,
            LastPushUtc = state.LastPush?.TimestampUtc,
            LastSynchronizedGitRevision = state.LastSynchronizedGitRevision,
            ImportedRevision = state.ImportedRevision,
            ExportedRevision = state.ExportedRevision,
            LastImportedRevision = state.LastImportedRevision,
            LastExportedRevision = state.LastExportedRevision,
            LastPushResult = state.LastPushResult,
            SynchronizedSourceSignature = state.SynchronizedSourceSignature,
            WorkspaceSourceSignature = state.WorkspaceSourceSignature,
            RemoteSourceSignature = state.RemoteSourceSignature,
        };
    }

    private OracleApexEnvironmentContext ResolveEnvironment(WorkspaceDefinition definition, string? requestedEnvironmentName)
    {
        var environmentName = string.IsNullOrWhiteSpace(requestedEnvironmentName)
            ? definition.Oracle.Apex.DefaultEnvironment
            : requestedEnvironmentName;
        if (string.IsNullOrWhiteSpace(environmentName))
        {
            environmentName = definition.Oracle.Apex.Environments.Keys.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(environmentName)
            || !definition.Oracle.Apex.Environments.TryGetValue(environmentName, out var environment))
        {
            throw new InvalidOperationException("Oracle APEX synchronization is not configured. Add oracle.apex.environments to workspace.yaml.");
        }

        return new OracleApexEnvironmentContext
        {
            EnvironmentName = environmentName,
            Workspace = environment.Workspace ?? string.Empty,
            ParsingSchema = environment.ParsingSchema ?? string.Empty,
            ApplicationId = environment.ApplicationId,
            ApplicationName = string.Empty,
            SqlclProfile = environment.SqlclProfile ?? string.Empty,
            SyncMode = WorkspaceSynchronizationModes.Normalize(environment.SyncMode),
            SourcePath = string.IsNullOrWhiteSpace(environment.SourcePath) ? "src/apex" : environment.SourcePath!,
        };
    }

    private async Task<ProcessResult> RunInWorkspaceAsync(WorkspaceSnapshot snapshot, string command, CancellationToken cancellationToken)
    {
        var containerName = _containerRuntime.GetWorkspaceContainerName(snapshot.Definition);
        return await _containerRuntime.RunSimpleDockerCommandAsync(
            ["exec", containerName, "bash", "-lc", $"cd /workspace && {command}"],
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static WorkspaceSynchronizationStateDocument UpdateEnvironmentState(WorkspaceSynchronizationStateDocument state, string environmentName, Func<WorkspaceSynchronizationEnvironmentStateRecord, WorkspaceSynchronizationEnvironmentStateRecord> transform)
    {
        var existing = state.Environments.TryGetValue(environmentName, out var environmentState)
            ? new WorkspaceSynchronizationEnvironmentStateRecord(environmentState)
            : new WorkspaceSynchronizationEnvironmentStateRecord(new WorkspaceSynchronizationEnvironmentState());
        var updated = transform(existing);
        var environments = new Dictionary<string, WorkspaceSynchronizationEnvironmentState>(state.Environments, StringComparer.OrdinalIgnoreCase)
        {
            [environmentName] = updated.ToDocument(),
        };
        return new WorkspaceSynchronizationStateDocument
        {
            DefaultEnvironment = string.IsNullOrWhiteSpace(state.DefaultEnvironment) ? environmentName : state.DefaultEnvironment,
            Environments = environments,
        };
    }

    private static WorkspaceSynchronizationOperationState CreateOperationState(ProcessResult result, WorkspaceSnapshot snapshot)
        => new()
        {
            Status = result.IsSuccess ? "Succeeded" : "Failed",
            Revision = snapshot.Safety.AdvancedGit.LatestCommitSha,
            TimestampUtc = DateTimeOffset.UtcNow,
            Summary = result.IsSuccess
                ? result.StandardOutputLines.LastOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? "Operation completed."
                : result.StandardErrorLines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? "Operation failed.",
        };

    private WorkspaceSynchronizationStateDocument RecordPushAttempt(WorkspacePaths paths, string environmentName, string gitRevision, WorkspaceSynchronizationState finalState, bool succeeded, string summary, string contentRevision)
    {
        var document = ReadState(paths);
        document = UpdateEnvironmentState(document, environmentName, current => current with
        {
            LastPush = new WorkspaceSynchronizationOperationState
            {
                Status = succeeded ? "Succeeded" : "Failed",
                Revision = gitRevision,
                TimestampUtc = DateTimeOffset.UtcNow,
                Summary = SanitizeSummary(summary),
            },
            LastPushResult = succeeded ? "Succeeded" : "Failed",
            SynchronizationState = finalState.ToString(),
            OperationHistory = AppendHistory(current.OperationHistory, "Push", succeeded ? "Succeeded" : "Failed", finalState, gitRevision, contentRevision, summary),
        });
        _stateService.Write(paths.ApexMetadataPath, document);
        return document;
    }

    private WorkspaceSynchronizationStateDocument ForceValidationFailedState(WorkspacePaths paths, string environmentName, string gitRevision, string summary)
    {
        var document = ReadState(paths);
        document = UpdateEnvironmentState(document, environmentName, current => current with
        {
            SynchronizationState = WorkspaceSynchronizationState.ValidationFailed.ToString(),
            DriftSummary = SanitizeSummary(summary),
            LastPush = new WorkspaceSynchronizationOperationState
            {
                Status = "Failed",
                Revision = gitRevision,
                TimestampUtc = DateTimeOffset.UtcNow,
                Summary = SanitizeSummary(summary),
            },
            LastPushResult = "Failed",
            OperationHistory = AppendHistory(current.OperationHistory, "Push", "Failed", WorkspaceSynchronizationState.ValidationFailed, gitRevision, current.WorkspaceSourceSignature, summary),
        });
        _stateService.Write(paths.ApexMetadataPath, document);
        return document;
    }

    private static List<WorkspaceSynchronizationHistoryEntry> AppendHistory(IReadOnlyList<WorkspaceSynchronizationHistoryEntry> existingHistory, string operation, string result, WorkspaceSynchronizationState state, string revision, string contentRevision, string summary)
    {
        var items = (existingHistory ?? Array.Empty<WorkspaceSynchronizationHistoryEntry>()).ToList();
        items.Insert(0, new WorkspaceSynchronizationHistoryEntry
        {
            Operation = operation,
            Result = result,
            State = state.ToString(),
            Revision = revision,
            ContentRevision = contentRevision,
            TimestampUtc = DateTimeOffset.UtcNow,
            Summary = SanitizeSummary(summary),
        });
        return items.Take(10).ToList();
    }

    private static string SanitizeSummary(string summary)
        => string.IsNullOrWhiteSpace(summary)
            ? string.Empty
            : summary.Replace("${ORACLE_DEMO_PASSWORD:-demo_password}", "[redacted]", StringComparison.Ordinal).Trim();

    private static string BuildWorkspaceScriptCommand(string scriptPath, string sourcePath)
        => $"{scriptPath} '{sourcePath.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private static string ResolveSourcePath(string rootPath, string sourcePath)
        => Path.Combine(rootPath, sourcePath.Replace('/', Path.DirectorySeparatorChar));

    private static string GetWorkspaceRelativePath(string rootPath, string path)
        => Path.GetRelativePath(rootPath, path).Replace(Path.DirectorySeparatorChar, '/');

    private static WorkspaceSynchronizationState ParseState(string? value)
        => Enum.TryParse<WorkspaceSynchronizationState>(value, ignoreCase: true, out var state) ? state : WorkspaceSynchronizationState.Unknown;

    private static OracleApexEnvironmentContext NormalizeEnvironment(string environmentName, string workspaceName, string parsingSchema, string sqlclProfile, string sourcePath)
        => new()
        {
            EnvironmentName = string.IsNullOrWhiteSpace(environmentName) ? "dev" : environmentName.Trim(),
            Workspace = string.IsNullOrWhiteSpace(workspaceName) ? "TEST" : workspaceName.Trim().ToUpperInvariant(),
            ParsingSchema = string.IsNullOrWhiteSpace(parsingSchema) ? "TESTSCHEMA" : parsingSchema.Trim().ToUpperInvariant(),
            ApplicationName = string.Empty,
            SqlclProfile = string.IsNullOrWhiteSpace(sqlclProfile) ? "local-apex-dev" : sqlclProfile.Trim(),
            SyncMode = WorkspaceSynchronizationModes.Manual,
            SourcePath = string.IsNullOrWhiteSpace(sourcePath) ? "src/apex" : sourcePath.Trim().Replace('\\', '/'),
        };

    private static WorkspaceDefinition BuildConnectedDefinition(WorkspaceDefinition definition, OracleApexApplicationDiscoveryResult discovery, OracleApexApplicationInfo application)
    {
        var environments = new Dictionary<string, OracleApexEnvironmentPreferences>(definition.Oracle.Apex.Environments, StringComparer.OrdinalIgnoreCase)
        {
            [discovery.EnvironmentName] = new OracleApexEnvironmentPreferences
            {
                Workspace = discovery.WorkspaceName,
                ParsingSchema = discovery.ParsingSchema,
                ApplicationId = application.ApplicationId,
                SqlclProfile = discovery.SqlclProfile,
                SyncMode = WorkspaceSynchronizationModes.Manual,
                SourcePath = discovery.SourcePath,
            },
        };

        return new WorkspaceDefinition
        {
            Workspace = definition.Workspace,
            Provider = definition.Provider,
            Runtime = definition.Runtime,
            Features = definition.Features.ToList(),
            Skills = definition.Skills.ToList(),
            Services = definition.Services.ToList(),
            Mcp = definition.Mcp.ToList(),
            Terminal = definition.Terminal,
            Agent = definition.Agent,
            Oracle = new OracleWorkspacePreferences
            {
                HostPort = definition.Oracle.HostPort,
                OrdsPort = definition.Oracle.OrdsPort,
                Apex = new OracleApexWorkspacePreferences
                {
                    DefaultEnvironment = discovery.EnvironmentName,
                    Environments = environments,
                },
            },
            Analytics = definition.Analytics,
            KnowledgePacks = definition.KnowledgePacks.ToList(),
        };
    }

    private static WorkspaceSnapshot WithDefinition(WorkspaceSnapshot snapshot, WorkspaceDefinition definition, WorkspaceSynchronizationSnapshot synchronization)
        => new()
        {
            Record = snapshot.Record,
            Definition = definition,
            Paths = snapshot.Paths,
            ConfigurationPath = snapshot.ConfigurationPath,
            RuntimeState = snapshot.RuntimeState,
            Safety = snapshot.Safety,
            Session = snapshot.Session,
            AppliedState = snapshot.AppliedState,
            LocalRuntimeState = snapshot.LocalRuntimeState,
            ResolvedRuntimePlan = snapshot.ResolvedRuntimePlan,
            UpdateRequired = snapshot.UpdateRequired,
            Synchronization = synchronization,
            Health = snapshot.Health,
            Readiness = snapshot.Readiness,
            AvailableServices = snapshot.AvailableServices,
        };

    private static void EnsureSqlSuccess(ProcessResult result, string message)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"{message} {FirstProcessFailureLine(result)}".Trim());
        }
    }

    private static void EnsureOperationSuccess(WorkspaceSynchronizationOperationResult result, string message)
    {
        if (result.ProcessResult?.IsSuccess == false)
        {
            throw new InvalidOperationException($"{message} {FirstProcessFailureLine(result.ProcessResult)}".Trim());
        }
    }

    private static OracleApexApplicationInfo? ParseApplicationInfo(string line)
    {
        var parts = line.Split('|');
        if (parts.Length < 2 || !int.TryParse(parts[0].Trim(), out var applicationId))
        {
            return null;
        }

        return new OracleApexApplicationInfo
        {
            ApplicationId = applicationId,
            ApplicationName = parts[1].Trim(),
            Alias = parts.Length > 2 ? parts[2].Trim() : string.Empty,
        };
    }

    private static string EscapeSqlLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static string EscapeSqlIdentifier(string value)
        => value.Replace("\"", "\"\"", StringComparison.Ordinal);

    private static string BuildConnectionString(OracleApexEnvironmentContext environment)
        => $"{environment.ParsingSchema.ToLowerInvariant()}/${{ORACLE_DEMO_PASSWORD:-demo_password}}@//oracle-demo:1521/FREEPDB1";

    private static string NormalizeExportRoot(string exportParent)
    {
        if (File.Exists(Path.Combine(exportParent, "application.apx")))
        {
            return exportParent;
        }

        var childDirectories = Directory.Exists(exportParent)
            ? Directory.GetDirectories(exportParent)
            : Array.Empty<string>();
        var directoryWithApplication = childDirectories.FirstOrDefault(path => File.Exists(Path.Combine(path, "application.apx")));
        return directoryWithApplication ?? exportParent;
    }

    private static void ResetDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }

    private static void ReplaceDirectoryContents(string sourcePath, string destinationPath)
    {
        if (Directory.Exists(destinationPath))
        {
            Directory.Delete(destinationPath, recursive: true);
        }

        Directory.CreateDirectory(destinationPath);
        foreach (var directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destinationPath, Path.GetRelativePath(sourcePath, directory)));
        }

        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, file);
            var destinationFile = Path.Combine(destinationPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile, overwrite: true);
        }
    }

    private static string FirstProcessFailureLine(ProcessResult result)
        => result.StandardErrorLines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
            ?? result.StandardOutputLines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
            ?? "Operation failed.";

    private static int GetStateRank(WorkspaceSynchronizationState state)
        => state switch
        {
            WorkspaceSynchronizationState.ValidationFailed => 5,
            WorkspaceSynchronizationState.Diverged => 4,
            WorkspaceSynchronizationState.DeploymentAhead => 3,
            WorkspaceSynchronizationState.GitAhead => 2,
            WorkspaceSynchronizationState.InSync => 1,
            _ => 0,
        };

    private static string BuildSynchronizationSummary(WorkspaceSynchronizationState state, WorkspaceSynchronizationEnvironmentSnapshot? environment)
        => state switch
        {
            WorkspaceSynchronizationState.InSync => $"Oracle APEX is synchronized for '{environment?.EnvironmentName ?? "default"}'.",
            WorkspaceSynchronizationState.GitAhead => $"Git changes are ahead of Oracle APEX for '{environment?.EnvironmentName ?? "default"}'.",
            WorkspaceSynchronizationState.DeploymentAhead => $"Oracle APEX changes need to be pulled back into Git for '{environment?.EnvironmentName ?? "default"}'.",
            WorkspaceSynchronizationState.Diverged => $"Git and Oracle APEX have both changed for '{environment?.EnvironmentName ?? "default"}'. Choose pull or push explicitly.",
            WorkspaceSynchronizationState.ValidationFailed => $"Oracle APEX validation failed for '{environment?.EnvironmentName ?? "default"}'.",
            _ => "Oracle APEX synchronization has not been established yet.",
        };

    private static WorkspaceSynchronizationState DetermineSyncState(string sourceSignature, string remoteSignature, WorkspaceSynchronizationEnvironmentState state)
    {
        if (string.IsNullOrWhiteSpace(sourceSignature) || string.IsNullOrWhiteSpace(remoteSignature))
        {
            return WorkspaceSynchronizationState.Unknown;
        }

        if (string.Equals(sourceSignature, remoteSignature, StringComparison.Ordinal))
        {
            return WorkspaceSynchronizationState.InSync;
        }

        if (string.IsNullOrWhiteSpace(state.SynchronizedSourceSignature))
        {
            return WorkspaceSynchronizationState.Unknown;
        }

        var sourceChanged = !string.Equals(sourceSignature, state.SynchronizedSourceSignature, StringComparison.Ordinal);
        var remoteChanged = !string.Equals(remoteSignature, state.SynchronizedSourceSignature, StringComparison.Ordinal);
        if (sourceChanged && remoteChanged)
        {
            return WorkspaceSynchronizationState.Diverged;
        }

        if (sourceChanged)
        {
            return WorkspaceSynchronizationState.GitAhead;
        }

        if (remoteChanged)
        {
            return WorkspaceSynchronizationState.DeploymentAhead;
        }

        return WorkspaceSynchronizationState.Unknown;
    }

    private static string BuildDriftSummary(WorkspaceSynchronizationState state, string environmentName)
        => state switch
        {
            WorkspaceSynchronizationState.InSync => $"Environment '{environmentName}' matches the latest Oracle APEX export.",
            WorkspaceSynchronizationState.GitAhead => $"Workspace source changed since the last synchronized Oracle APEX export for '{environmentName}'.",
            WorkspaceSynchronizationState.DeploymentAhead => $"Oracle APEX contains Builder changes that are not in workspace source for '{environmentName}'.",
            WorkspaceSynchronizationState.Diverged => $"Workspace source and Oracle APEX both changed since the last synchronized baseline for '{environmentName}'.",
            WorkspaceSynchronizationState.ValidationFailed => $"Validation failed for '{environmentName}'.",
            _ => $"Synchronization baseline for '{environmentName}' is not known yet.",
        };

    private static string BuildEnvironmentSummary(WorkspaceSynchronizationState state, string environmentName)
        => state switch
        {
            WorkspaceSynchronizationState.InSync => $"Environment '{environmentName}' is in sync.",
            WorkspaceSynchronizationState.GitAhead => $"Git is ahead of environment '{environmentName}'.",
            WorkspaceSynchronizationState.DeploymentAhead => $"Environment '{environmentName}' has unapplied Builder changes.",
            WorkspaceSynchronizationState.Diverged => $"Environment '{environmentName}' diverged from Git.",
            WorkspaceSynchronizationState.ValidationFailed => $"Environment '{environmentName}' failed validation.",
            _ => $"Environment '{environmentName}' synchronization is not known yet.",
        };

    private async Task<string> BuildDetailedDiff(WorkspaceSnapshot snapshot, OracleApexEnvironmentContext environment, string sourcePath, string remotePath)
    {
        var gitStatus = await _processRunner.RunAsync(
            "git",
            ["status", "--short", "--", environment.SourcePath],
            snapshot.Paths.RootPath).ConfigureAwait(false);
        var statusLines = gitStatus.IsSuccess
            ? gitStatus.StandardOutputLines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList()
            : new List<string> { "git status unavailable for source path." };
        var fileDiff = BuildDirectoryDiff(sourcePath, remotePath);
        return string.Join(Environment.NewLine,
        [
            $"Synchronization state: {DetermineSyncState(ComputeDirectorySignature(sourcePath), ComputeDirectorySignature(remotePath), ReadState(snapshot.Paths).Environments.TryGetValue(environment.EnvironmentName, out var state) ? state : new WorkspaceSynchronizationEnvironmentState())}",
            $"Source path: {environment.SourcePath}",
            "Git status:",
            statusLines.Count == 0 ? "(clean)" : string.Join(Environment.NewLine, statusLines),
            string.Empty,
            "APEX export differences:",
            string.IsNullOrWhiteSpace(fileDiff) ? "No file-level differences detected." : fileDiff,
        ]);
    }

    private static string ComputeDirectorySignature(string root)
    {
        if (!Directory.Exists(root))
        {
            return string.Empty;
        }

        using var sha = SHA256.Create();
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            var pathBytes = System.Text.Encoding.UTF8.GetBytes(relativePath + "\n");
            sha.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);
            var contentBytes = File.ReadAllBytes(file);
            sha.TransformBlock(contentBytes, 0, contentBytes.Length, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    private static string BuildDirectoryDiff(string sourcePath, string exportedPath)
    {
        var sourceFiles = Directory.Exists(sourcePath)
            ? Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(sourcePath, path).Replace(Path.DirectorySeparatorChar, '/'), ComputeHash, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var exportedFiles = Directory.Exists(exportedPath)
            ? Directory.GetFiles(exportedPath, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(exportedPath, path).Replace(Path.DirectorySeparatorChar, '/'), ComputeHash, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var allPaths = sourceFiles.Keys.Concat(exportedFiles.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>();
        foreach (var path in allPaths)
        {
            var inSource = sourceFiles.TryGetValue(path, out var sourceHash);
            var inExport = exportedFiles.TryGetValue(path, out var exportHash);
            if (inSource && inExport && string.Equals(sourceHash, exportHash, StringComparison.Ordinal))
            {
                continue;
            }

            lines.Add(!inSource
                ? $"Only in export: {path}"
                : !inExport
                    ? $"Only in source: {path}"
                    : $"Changed: {path}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private sealed class OracleApexEnvironmentContext
    {
        public required string EnvironmentName { get; init; }
        public string Workspace { get; init; } = string.Empty;
        public string ParsingSchema { get; init; } = string.Empty;
        public int? ApplicationId { get; init; }
        public string ApplicationName { get; init; } = string.Empty;
        public string SqlclProfile { get; init; } = string.Empty;
        public string SyncMode { get; init; } = WorkspaceSynchronizationModes.Manual;
        public string SourcePath { get; init; } = "src/apex";
    }

    private readonly record struct WorkspaceSynchronizationEnvironmentStateRecord(
        string SynchronizationState,
        string DriftSummary,
        WorkspaceSynchronizationOperationState? LastValidation,
        WorkspaceSynchronizationOperationState? LastImport,
        WorkspaceSynchronizationOperationState? LastExport,
        WorkspaceSynchronizationOperationState? LastPull,
        WorkspaceSynchronizationOperationState? LastPush,
        string ImportedRevision,
        string ExportedRevision,
        string LastSynchronizedGitRevision,
        string ApplicationName,
        string LastPushResult,
        string LastImportedRevision,
        string LastExportedRevision,
        string SynchronizedSourceSignature,
        string WorkspaceSourceSignature,
        string RemoteSourceSignature,
        List<WorkspaceSynchronizationHistoryEntry> OperationHistory)
    {
        public WorkspaceSynchronizationEnvironmentStateRecord(WorkspaceSynchronizationEnvironmentState state)
            : this(
                state.SynchronizationState,
                state.DriftSummary,
                state.LastValidation,
                state.LastImport,
                state.LastExport,
                state.LastPull,
                state.LastPush,
                state.ImportedRevision,
                state.ExportedRevision,
                state.LastSynchronizedGitRevision,
                state.ApplicationName,
                state.LastPushResult,
                state.LastImportedRevision,
                state.LastExportedRevision,
                state.SynchronizedSourceSignature,
                state.WorkspaceSourceSignature,
                state.RemoteSourceSignature,
                state.OperationHistory.ToList())
        {
        }

        public WorkspaceSynchronizationEnvironmentState ToDocument()
            => new()
            {
                SynchronizationState = SynchronizationState,
                DriftSummary = DriftSummary,
                LastValidation = LastValidation,
                LastImport = LastImport,
                LastExport = LastExport,
                LastPull = LastPull,
                LastPush = LastPush,
                ImportedRevision = ImportedRevision,
                ExportedRevision = ExportedRevision,
                LastSynchronizedGitRevision = LastSynchronizedGitRevision,
                ApplicationName = ApplicationName,
                LastPushResult = LastPushResult,
                LastImportedRevision = LastImportedRevision,
                LastExportedRevision = LastExportedRevision,
                SynchronizedSourceSignature = SynchronizedSourceSignature,
                WorkspaceSourceSignature = WorkspaceSourceSignature,
                RemoteSourceSignature = RemoteSourceSignature,
                OperationHistory = OperationHistory.ToList(),
            };
    }
}
