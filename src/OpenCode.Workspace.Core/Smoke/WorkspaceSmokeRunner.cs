using System.Text;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Smoke;

public sealed class WorkspaceSmokeRunner
{
    private readonly IWorkspaceSmokeWorkspaceServiceFactory _workspaceServiceFactory;
    private readonly IWorkspaceSmokeValidatorProvider _validatorProvider;
    private readonly IContainerRuntime _containerRuntime;
    private readonly RuntimeOwnershipService _runtimeOwnershipService;
    private readonly global::OpenCode.Workspace.Core.Runtime.SmokeRuntimeOwnershipService _smokeOwnershipService;
    private readonly TemplateExpander _templateExpander = new();
    private readonly WorkspaceSmokeLockService _lockService = new();

    public WorkspaceSmokeRunner(
        IWorkspaceSmokeWorkspaceServiceFactory workspaceServiceFactory,
        IWorkspaceSmokeValidatorProvider validatorProvider,
        IContainerRuntime containerRuntime,
        RuntimeOwnershipService runtimeOwnershipService,
        global::OpenCode.Workspace.Core.Runtime.SmokeRuntimeOwnershipService smokeOwnershipService)
    {
        _workspaceServiceFactory = workspaceServiceFactory;
        _validatorProvider = validatorProvider;
        _containerRuntime = containerRuntime;
        _runtimeOwnershipService = runtimeOwnershipService;
        _smokeOwnershipService = smokeOwnershipService;
    }

    public async Task<WorkspaceSmokeResult> RunAsync(WorkspaceSmokeDefinition definition, WorkspaceSmokeRunnerOptions options, CancellationToken cancellationToken = default)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var runId = $"{CreateRunDirectoryName(startedUtc)}-{Guid.NewGuid():N}";
        var matrixRunId = string.IsNullOrWhiteSpace(options.MatrixRunId) ? CreateRunDirectoryName(startedUtc) : options.MatrixRunId;
        var workspaceRoot = string.IsNullOrWhiteSpace(options.WorkspaceRoot)
            ? Path.Combine(Path.GetTempPath(), $"workspace-smoke-{WorkspacePathBuilder.Slugify(definition.TemplateId)}-{CreateRunDirectoryName(startedUtc)}")
            : options.WorkspaceRoot!;
        var autoCreatedWorkspace = string.IsNullOrWhiteSpace(options.WorkspaceRoot);
        var artifactDirectory = Path.Combine(options.ArtifactsRoot, definition.TemplateId, runId);
        var validationArtifactDirectory = Path.Combine(artifactDirectory, "validation");
        var cleanupArtifactDirectory = Path.Combine(artifactDirectory, "cleanup");
        var dockerArtifactDirectory = Path.Combine(artifactDirectory, "docker");
        Directory.CreateDirectory(validationArtifactDirectory);
        Directory.CreateDirectory(cleanupArtifactDirectory);
        Directory.CreateDirectory(dockerArtifactDirectory);

        global::OpenCode.Workspace.Core.Runtime.SmokeCleanupResult? cleanupResult = null;
        RuntimeResourceInventory inventoryBefore = new();
        RuntimeResourceInventory inventoryActive = new();
        RuntimeResourceInventory inventoryAfter = new();
        var validators = new List<WorkspaceSmokeValidatorResult>();
        var warnings = new List<string>();
        var phase = WorkspaceSmokePhase.Discovery;
        var status = WorkspaceSmokeStatus.Passed;
        WorkspaceSmokeFailureClassification failureClassification = WorkspaceSmokeFailureClassification.None;
        var failureMessage = string.Empty;
        WorkspaceSnapshot? snapshot = null;
        var workspaceService = _workspaceServiceFactory.Create();
        IDisposable? lockHandle = null;
        var skipReason = string.Empty;

        try
        {
            if (!definition.Supported)
            {
                status = WorkspaceSmokeStatus.Skipped;
                failureClassification = WorkspaceSmokeFailureClassification.UnsupportedSmokeTemplate;
                skipReason = definition.UnsupportedReason;
                goto Finalize;
            }

            if (definition.ResourceClass == WorkspaceSmokeResourceClass.OracleExclusive && !options.OracleLockAlreadyHeld)
            {
                lockHandle = _lockService.AcquireOracleExclusiveLock();
            }

            phase = WorkspaceSmokePhase.Preflight;
            inventoryBefore = await _runtimeOwnershipService.BuildInventoryAsync(new RuntimeOwnershipQuery { OwnerKind = "smoke" }, cancellationToken);
            await WorkspaceSmokeArtifacts.WriteRuntimeInventoryArtifactsAsync(artifactDirectory, "before", inventoryBefore, cancellationToken);

            var preflightCleanup = await _smokeOwnershipService.CleanupAsync(new global::OpenCode.Workspace.Core.Runtime.SmokeCleanupOptions(DryRun: false, IncludeAll: true, RunId: null, OutputFormat: "json"), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(cleanupArtifactDirectory, "host-preflight-cleanup.json"), System.Text.Json.JsonSerializer.Serialize(preflightCleanup, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            if (!preflightCleanup.Succeeded || !preflightCleanup.VerificationSucceeded)
            {
                throw new InvalidOperationException("Runtime resource exhaustion: stale smoke-owned Docker resources could not be cleaned before starting the smoke run.");
            }

            phase = WorkspaceSmokePhase.Creation;
            var expanded = _templateExpander.Expand($"{definition.TemplateId}-runtime-smoke-{CreateRunDirectoryName(startedUtc)}", definition.Template);
            var provisioningLog = new StringBuilder();
            void Log(CommandLogEntry entry)
            {
                provisioningLog.AppendLine($"[{entry.Source}] {entry.Message}");
            }

            Directory.CreateDirectory(workspaceRoot);
            snapshot = workspaceService.CreateWorkspace(workspaceRoot, expanded, Log);
            WorkspaceSmokeOwnershipLabelWriter.Apply(snapshot.Paths.ComposePath, expanded, definition.TemplateId, runId, workspaceRoot);
            WorkspaceSmokeArtifacts.CaptureGeneratedArtifacts(snapshot.Paths, artifactDirectory);
            await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "provisioning.log"), provisioningLog.ToString(), cancellationToken);

            if (options.DryRun)
            {
                phase = WorkspaceSmokePhase.Completed;
                status = WorkspaceSmokeStatus.Passed;
                goto Finalize;
            }

            phase = WorkspaceSmokePhase.Provisioning;
            await workspaceService.ProvisionAsync(snapshot, Log, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "provisioning.log"), provisioningLog.ToString(), cancellationToken);

            inventoryActive = await _runtimeOwnershipService.BuildInventoryAsync(new RuntimeOwnershipQuery { OwnerKind = "smoke", RunId = runId }, cancellationToken);
            await WorkspaceSmokeArtifacts.WriteRuntimeInventoryArtifactsAsync(artifactDirectory, "active", inventoryActive, cancellationToken);
            await CaptureDockerArtifactsAsync(snapshot.Paths, expanded, dockerArtifactDirectory, cancellationToken);

            phase = WorkspaceSmokePhase.Validation;
            var context = new WorkspaceSmokeContext
            {
                MatrixRunId = matrixRunId,
                RunId = runId,
                SmokeDefinition = definition,
                WorkspaceDefinition = expanded,
                Snapshot = snapshot,
                WorkspaceService = workspaceService,
                ContainerRuntime = _containerRuntime,
                RuntimeOwnershipService = _runtimeOwnershipService,
                ArtifactDirectory = artifactDirectory,
                ValidationArtifactDirectory = validationArtifactDirectory,
            };
            foreach (var validator in _validatorProvider.ResolveValidators(definition))
            {
                var validatorResult = await validator.ValidateAsync(context, cancellationToken);
                validators.Add(validatorResult);
                await File.WriteAllTextAsync(Path.Combine(validationArtifactDirectory, $"{validator.ValidatorId}.json"), System.Text.Json.JsonSerializer.Serialize(validatorResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            }

            var failures = validators.Where(item => !item.Succeeded).ToArray();
            if (failures.Length > 0)
            {
                phase = WorkspaceSmokePhase.Validation;
                status = WorkspaceSmokeStatus.Failed;
                failureClassification = WorkspaceSmokeFailureClassification.SmokeValidationFailure;
                failureMessage = string.Join(" | ", failures.Select(item => $"{item.ValidatorId}: {item.Message}"));
            }

            phase = WorkspaceSmokePhase.Completed;
            status = failures.Length == 0 ? WorkspaceSmokeStatus.Passed : WorkspaceSmokeStatus.Failed;
        }
        catch (Exception exception)
        {
            status = WorkspaceSmokeStatus.Failed;
            failureClassification = WorkspaceSmokeFailureClassifier.Classify(exception);
            failureMessage = exception.Message;
            warnings.Add(exception.ToString());
        }
        finally
        {
            try
            {
                phase = WorkspaceSmokePhase.Cleanup;
                var shouldRetainRuntime = options.KeepRuntimeOnFailure && !string.IsNullOrWhiteSpace(failureMessage);
                if (!shouldRetainRuntime)
                {
                    cleanupResult = await _smokeOwnershipService.CleanupAsync(new global::OpenCode.Workspace.Core.Runtime.SmokeCleanupOptions(DryRun: false, IncludeAll: false, RunId: runId, OutputFormat: "json"), cancellationToken);
                }
                else
                {
                    cleanupResult = new global::OpenCode.Workspace.Core.Runtime.SmokeCleanupResult
                    {
                        Succeeded = true,
                        DryRun = false,
                        VerificationSucceeded = false,
                        Warnings = ["Runtime retention was explicitly requested after failure."],
                    };
                }

                await File.WriteAllTextAsync(Path.Combine(cleanupArtifactDirectory, "run-cleanup.json"), System.Text.Json.JsonSerializer.Serialize(cleanupResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), cancellationToken);
                inventoryAfter = await _runtimeOwnershipService.BuildInventoryAsync(new RuntimeOwnershipQuery { OwnerKind = "smoke" }, cancellationToken);
                await WorkspaceSmokeArtifacts.WriteRuntimeInventoryArtifactsAsync(artifactDirectory, "after", inventoryAfter, cancellationToken);
            }
            catch (Exception cleanupException)
            {
                warnings.Add(cleanupException.ToString());
                cleanupResult = new global::OpenCode.Workspace.Core.Runtime.SmokeCleanupResult
                {
                    Succeeded = false,
                    DryRun = false,
                    VerificationSucceeded = false,
                    Errors = [cleanupException.Message],
                };
            }

            try
            {
                if (snapshot is not null && autoCreatedWorkspace)
                {
                    workspaceService.DeleteWorkspaceRegistration(snapshot.Paths.RootPath);
                    if (Directory.Exists(snapshot.Paths.RootPath) && !options.KeepWorkspace)
                    {
                        await _containerRuntime.NormalizeWorkspaceFilePermissionsAsync(snapshot.Paths.RootPath, cancellationToken: cancellationToken);
                        Directory.Delete(snapshot.Paths.RootPath, recursive: true);
                    }
                }
            }
            catch (Exception deleteException)
            {
                warnings.Add(deleteException.ToString());
            }

            lockHandle?.Dispose();
        }

Finalize:
        var finalMessage = string.IsNullOrWhiteSpace(failureMessage) ? skipReason : failureMessage;
        if (cleanupResult is { VerificationSucceeded: false } && status == WorkspaceSmokeStatus.Passed)
        {
            status = WorkspaceSmokeStatus.Failed;
            failureClassification = WorkspaceSmokeFailureClassification.CleanupFailure;
            finalMessage = "Run cleanup verification failed.";
        }

        var finalPhase = status == WorkspaceSmokeStatus.Passed ? WorkspaceSmokePhase.Completed : phase;
        var result = new WorkspaceSmokeResult
        {
            TemplateId = definition.TemplateId,
            RunId = runId,
            WorkspacePath = workspaceRoot,
            ComposeProject = snapshot is null ? string.Empty : WorkspacePathBuilder.Slugify(snapshot.Definition.Workspace.Name),
            StartedUtc = startedUtc,
            FinishedUtc = DateTimeOffset.UtcNow,
            Duration = DateTimeOffset.UtcNow - startedUtc,
            Status = status,
            Phase = finalPhase,
            FailureClassification = failureClassification,
            FailureMessage = finalMessage,
            Validators = validators.ToArray(),
            ResourceCountsBefore = WorkspaceSmokeResourceCounts.FromInventory(inventoryBefore),
            ResourceCountsActive = WorkspaceSmokeResourceCounts.FromInventory(inventoryActive),
            ResourceCountsAfter = WorkspaceSmokeResourceCounts.FromInventory(inventoryAfter),
            CleanupResult = cleanupResult,
            CleanupVerificationSucceeded = cleanupResult?.VerificationSucceeded ?? false,
            ArtifactDirectory = artifactDirectory,
            Warnings = warnings.ToArray(),
        };
        WorkspaceSmokeArtifacts.WriteResultSummary(artifactDirectory, result);
        return result;
    }

    public static string CreateRunDirectoryName(DateTimeOffset timestamp)
        => timestamp.ToString("yyyyMMdd-HHmmss");

    private async Task CaptureDockerArtifactsAsync(WorkspacePaths paths, WorkspaceDefinition definition, string dockerArtifactDirectory, CancellationToken cancellationToken)
    {
        var inspection = ComposeProjectInspector.InspectFile(paths.ComposePath);
        var profiles = inspection.Profiles.SelectMany(profile => new[] { "--profile", profile }).ToArray();
        var composePrefix = new List<string> { "compose", "--project-name", WorkspacePathBuilder.Slugify(definition.Workspace.Name), "--file", paths.ComposePath };
        composePrefix.AddRange(profiles);

        async Task WriteDockerOutputAsync(string fileName, IReadOnlyList<string> arguments)
        {
            var result = await _containerRuntime.RunSimpleDockerCommandAsync(arguments, cancellationToken: cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(dockerArtifactDirectory, fileName), result.IsSuccess ? result.StandardOutput : result.StandardError, cancellationToken);
        }

        await WriteDockerOutputAsync("compose-config.txt", [.. composePrefix, "config"]);
        await WriteDockerOutputAsync("compose-ps.txt", [.. composePrefix, "ps"]);
        await WriteDockerOutputAsync("docker-system-df.txt", ["system", "df"]);
        await WriteDockerOutputAsync("docker-stats.txt", ["stats", "--no-stream", "--format", "table {{.Name}}\t{{.MemUsage}}\t{{.CPUPerc}}"]);
    }
}

public sealed class WorkspaceSmokeMatrixRunner
{
    private readonly WorkspaceSmokeRunner _runner;
    private readonly global::OpenCode.Workspace.Core.Runtime.SmokeRuntimeOwnershipService _smokeOwnershipService;
    private readonly RuntimeOwnershipService _runtimeOwnershipService;
    private readonly WorkspaceSmokeLockService _lockService = new();

    public WorkspaceSmokeMatrixRunner(WorkspaceSmokeRunner runner, global::OpenCode.Workspace.Core.Runtime.SmokeRuntimeOwnershipService smokeOwnershipService, RuntimeOwnershipService runtimeOwnershipService)
    {
        _runner = runner;
        _smokeOwnershipService = smokeOwnershipService;
        _runtimeOwnershipService = runtimeOwnershipService;
    }

    public async Task<WorkspaceSmokeMatrixResult> RunAsync(IReadOnlyList<WorkspaceSmokeDefinition> definitions, WorkspaceSmokeMatrixRunnerOptions options, CancellationToken cancellationToken = default)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var matrixRunId = $"{WorkspaceSmokeRunner.CreateRunDirectoryName(startedUtc)}-{Guid.NewGuid():N}";
        var artifactDirectory = Path.Combine(options.ArtifactsRoot, matrixRunId);
        Directory.CreateDirectory(artifactDirectory);
        var hostBeforeDirectory = Path.Combine(artifactDirectory, "host-before");
        var hostAfterDirectory = Path.Combine(artifactDirectory, "host-after");
        Directory.CreateDirectory(hostBeforeDirectory);
        Directory.CreateDirectory(hostAfterDirectory);
        IDisposable? lockHandle = null;
        global::OpenCode.Workspace.Core.Runtime.SmokeCleanupResult? finalCleanup = null;
        RuntimeResourceInventory? finalInventory = null;
        var results = new List<WorkspaceSmokeResult>();

        try
        {
            if (definitions.Any(item => item.ResourceClass == WorkspaceSmokeResourceClass.OracleExclusive))
            {
                lockHandle = _lockService.AcquireOracleExclusiveLock();
            }

            var preflight = await _smokeOwnershipService.CleanupAsync(new global::OpenCode.Workspace.Core.Runtime.SmokeCleanupOptions(DryRun: false, IncludeAll: true, RunId: null, OutputFormat: "json"), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(hostBeforeDirectory, "cleanup.json"), System.Text.Json.JsonSerializer.Serialize(preflight, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            if (!preflight.Succeeded || !preflight.VerificationSucceeded)
            {
                throw new InvalidOperationException("Runtime resource exhaustion: stale smoke-owned Docker resources could not be cleaned before starting the matrix.");
            }

            var initialInventory = await _runtimeOwnershipService.BuildInventoryAsync(new RuntimeOwnershipQuery { OwnerKind = "smoke" }, cancellationToken);
            await WorkspaceSmokeArtifacts.WriteRuntimeInventoryArtifactsAsync(hostBeforeDirectory, "host-before", initialInventory, cancellationToken);
            if (initialInventory.Resources.Count > 0)
            {
                throw new InvalidOperationException("Runtime resource exhaustion: smoke-owned Docker resources are still active after matrix preflight cleanup.");
            }

            foreach (var definition in OrderDefinitions(definitions))
            {
                var result = await _runner.RunAsync(definition, new WorkspaceSmokeRunnerOptions
                {
                    MatrixRunId = matrixRunId,
                    ArtifactsRoot = artifactDirectory,
                    OracleLockAlreadyHeld = definition.ResourceClass == WorkspaceSmokeResourceClass.OracleExclusive && lockHandle is not null,
                    KeepRuntimeOnFailure = options.KeepRuntimeOnFailure,
                    KeepWorkspace = options.KeepWorkspace,
                }, cancellationToken);
                results.Add(result);

                var unsafeToContinue = result.CleanupResult is { VerificationSucceeded: false } && !options.KeepRuntimeOnFailure
                    || result.FailureClassification is WorkspaceSmokeFailureClassification.RuntimeResourceExhaustion or WorkspaceSmokeFailureClassification.LockAcquisitionFailure;
                if (unsafeToContinue)
                {
                    break;
                }
            }

            finalCleanup = await _smokeOwnershipService.CleanupAsync(new global::OpenCode.Workspace.Core.Runtime.SmokeCleanupOptions(DryRun: false, IncludeAll: true, RunId: null, OutputFormat: "json"), cancellationToken);
            finalInventory = await _runtimeOwnershipService.BuildInventoryAsync(new RuntimeOwnershipQuery { OwnerKind = "smoke" }, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(hostAfterDirectory, "cleanup.json"), System.Text.Json.JsonSerializer.Serialize(finalCleanup, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            await WorkspaceSmokeArtifacts.WriteRuntimeInventoryArtifactsAsync(hostAfterDirectory, "host-after", finalInventory, cancellationToken);

            var matrixStatus = results.Any(item => item.Status == WorkspaceSmokeStatus.Failed)
                || finalCleanup is { VerificationSucceeded: false }
                || finalInventory.Resources.Count > 0
                    ? WorkspaceSmokeStatus.Failed
                    : results.All(item => item.Status == WorkspaceSmokeStatus.Skipped)
                        ? WorkspaceSmokeStatus.Skipped
                        : WorkspaceSmokeStatus.Passed;

            var matrixResult = new WorkspaceSmokeMatrixResult
            {
                MatrixRunId = matrixRunId,
                SelectedTemplates = definitions.Select(item => item.TemplateId).ToArray(),
                StartedUtc = startedUtc,
                FinishedUtc = DateTimeOffset.UtcNow,
                Results = results.ToArray(),
                PassedCount = results.Count(item => item.Status == WorkspaceSmokeStatus.Passed),
                FailedCount = results.Count(item => item.Status == WorkspaceSmokeStatus.Failed) + ((finalCleanup is { VerificationSucceeded: false } || finalInventory.Resources.Count > 0) ? 1 : 0),
                SkippedCount = results.Count(item => item.Status == WorkspaceSmokeStatus.Skipped),
                FinalHostCleanupResult = finalCleanup,
                FinalRuntimeInventory = finalInventory,
                Status = matrixStatus,
                ArtifactDirectory = artifactDirectory,
            };
            WorkspaceSmokeArtifacts.WriteMatrixSummary(artifactDirectory, matrixResult);
            return matrixResult;
        }
        finally
        {
            lockHandle?.Dispose();
        }
    }

    private static IReadOnlyList<WorkspaceSmokeDefinition> OrderDefinitions(IEnumerable<WorkspaceSmokeDefinition> definitions)
        => definitions.OrderBy(item => item.ResourceClass switch
            {
                WorkspaceSmokeResourceClass.Lightweight => 0,
                WorkspaceSmokeResourceClass.DocumentProcessing => 1,
                WorkspaceSmokeResourceClass.Analytics => 2,
                WorkspaceSmokeResourceClass.Database => 3,
                WorkspaceSmokeResourceClass.OracleExclusive => 4,
                _ => 99,
            })
            .ThenBy(item => item.TemplateId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
