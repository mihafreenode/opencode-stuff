using System.Security.Cryptography;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class OracleApexWorkspaceSynchronizationProvider : IWorkspaceSynchronizationProvider
{
    private readonly WorkspaceSynchronizationStateService _stateService;
    private readonly IContainerRuntime _containerRuntime;
    private readonly IProcessRunner _processRunner;

    public OracleApexWorkspaceSynchronizationProvider(
        WorkspaceSynchronizationStateService stateService,
        IContainerRuntime containerRuntime,
        IProcessRunner processRunner)
    {
        _stateService = stateService;
        _containerRuntime = containerRuntime;
        _processRunner = processRunner;
    }

    public string ProviderId => "oracle-apex";

    public bool CanHandle(WorkspaceDefinition definition)
        => OracleWorkspaceFamily.HasApex(definition) && definition.Oracle.Apex.Environments.Count > 0;

    public Task<WorkspaceSynchronizationStatusResult> GetStatusAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default)
    {
        var snapshot = BuildSnapshot(request.Snapshot, ReadState(request.Snapshot.Paths));
        return Task.FromResult(new WorkspaceSynchronizationStatusResult { Snapshot = snapshot });
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
        state = UpdateEnvironmentState(state, environment.EnvironmentName, current => current with
        {
            LastValidation = CreateOperationState(result, request.Snapshot),
            SynchronizationState = result.IsSuccess ? current.SynchronizationState : WorkspaceSynchronizationState.ValidationFailed.ToString(),
        });
        _stateService.Write(request.Snapshot.Paths.ApexMetadataPath, state);

        return new WorkspaceSynchronizationOperationResult
        {
            Snapshot = BuildSnapshot(request.Snapshot, state),
            Message = result.IsSuccess ? $"Validated APEX source for environment '{environment.EnvironmentName}'." : $"APEX validation failed for environment '{environment.EnvironmentName}'.",
            ProcessResult = result,
        };
    }

    public async Task<WorkspaceSynchronizationOperationResult> ExportAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default)
    {
        var environment = ResolveEnvironment(request.Snapshot.Definition, request.EnvironmentName);
        var command = BuildWorkspaceScriptCommand("scripts/export-apex.sh", environment.SourcePath);
        var result = await RunInWorkspaceAsync(request.Snapshot, command, cancellationToken).ConfigureAwait(false);
        var newState = ReadState(request.Snapshot.Paths);
        var sourceRevision = request.Snapshot.Safety.AdvancedGit.LatestCommitSha;
        newState = UpdateEnvironmentState(newState, environment.EnvironmentName, current => current with
        {
            LastExport = CreateOperationState(result, request.Snapshot),
            ExportedRevision = sourceRevision,
            SynchronizationState = result.IsSuccess ? WorkspaceSynchronizationState.DeploymentAhead.ToString() : current.SynchronizationState,
            DriftSummary = result.IsSuccess ? "Builder changes were exported into the workspace source tree." : current.DriftSummary,
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
        var command = BuildWorkspaceScriptCommand("scripts/import-apex.sh", environment.SourcePath);
        var result = await RunInWorkspaceAsync(request.Snapshot, command, cancellationToken).ConfigureAwait(false);
        var newState = ReadState(request.Snapshot.Paths);
        var sourceRevision = request.Snapshot.Safety.AdvancedGit.LatestCommitSha;
        newState = UpdateEnvironmentState(newState, environment.EnvironmentName, current => current with
        {
            LastImport = CreateOperationState(result, request.Snapshot),
            ImportedRevision = sourceRevision,
            LastSynchronizedGitRevision = result.IsSuccess ? sourceRevision : current.LastSynchronizedGitRevision,
            SynchronizationState = result.IsSuccess ? WorkspaceSynchronizationState.InSync.ToString() : current.SynchronizationState,
            DriftSummary = result.IsSuccess ? string.Empty : current.DriftSummary,
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
        var command = BuildWorkspaceScriptCommand("scripts/export-apex.sh", GetWorkspaceRelativePath(request.Snapshot.Paths.RootPath, diffRoot));
        var exportResult = await RunInWorkspaceAsync(request.Snapshot, command, cancellationToken).ConfigureAwait(false);
        var sourcePath = ResolveSourcePath(request.Snapshot.Paths.RootPath, environment.SourcePath);
        var diffText = exportResult.IsSuccess
            ? BuildDirectoryDiff(sourcePath, diffRoot)
            : string.Join(Environment.NewLine, exportResult.StandardErrorLines.Concat(exportResult.StandardOutputLines));
        var status = BuildSnapshot(request.Snapshot, ReadState(request.Snapshot.Paths));
        return new WorkspaceSynchronizationDiffResult
        {
            Snapshot = status,
            Summary = string.IsNullOrWhiteSpace(diffText) ? "No differences were detected between workspace source and exported Oracle APEX source." : "Differences were detected between workspace source and exported Oracle APEX source.",
            DiffText = diffText,
        };
    }

    public Task<WorkspaceSynchronizationOperationResult> PullAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default)
        => ExportAsync(request, cancellationToken);

    public async Task<WorkspaceSynchronizationOperationResult> PushAsync(WorkspaceSynchronizationRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (validation.ProcessResult?.IsSuccess == false)
        {
            return validation;
        }

        return await ImportAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private WorkspaceSynchronizationStateDocument ReadState(WorkspacePaths paths)
        => _stateService.Read(paths.ApexMetadataPath) ?? new WorkspaceSynchronizationStateDocument();

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
            SqlclProfile = environment.SqlclProfile ?? string.Empty,
            SyncMode = WorkspaceSynchronizationModes.Normalize(environment.SyncMode),
            SourcePath = environment.SourcePath ?? "src/apex",
            State = effectiveState,
            Summary = BuildEnvironmentSummary(effectiveState, environmentName),
            DriftSummary = state.DriftSummary,
            LastValidationUtc = state.LastValidation?.TimestampUtc,
            LastImportUtc = state.LastImport?.TimestampUtc,
            LastExportUtc = state.LastExport?.TimestampUtc,
            LastSynchronizedGitRevision = state.LastSynchronizedGitRevision,
            ImportedRevision = state.ImportedRevision,
            ExportedRevision = state.ExportedRevision,
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

    private static string BuildWorkspaceScriptCommand(string scriptPath, string sourcePath)
        => $"{scriptPath} '{sourcePath.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private static string ResolveSourcePath(string rootPath, string sourcePath)
        => Path.Combine(rootPath, sourcePath.Replace('/', Path.DirectorySeparatorChar));

    private static string GetWorkspaceRelativePath(string rootPath, string path)
        => Path.GetRelativePath(rootPath, path).Replace(Path.DirectorySeparatorChar, '/');

    private static WorkspaceSynchronizationState ParseState(string? value)
        => Enum.TryParse<WorkspaceSynchronizationState>(value, ignoreCase: true, out var state) ? state : WorkspaceSynchronizationState.Unknown;

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
        string ImportedRevision,
        string ExportedRevision,
        string LastSynchronizedGitRevision)
    {
        public WorkspaceSynchronizationEnvironmentStateRecord(WorkspaceSynchronizationEnvironmentState state)
            : this(
                state.SynchronizationState,
                state.DriftSummary,
                state.LastValidation,
                state.LastImport,
                state.LastExport,
                state.ImportedRevision,
                state.ExportedRevision,
                state.LastSynchronizedGitRevision)
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
                ImportedRevision = ImportedRevision,
                ExportedRevision = ExportedRevision,
                LastSynchronizedGitRevision = LastSynchronizedGitRevision,
            };
    }
}
