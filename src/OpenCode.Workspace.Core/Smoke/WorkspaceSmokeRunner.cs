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
        WorkspaceSmokeFailureClassification originalFailureClassification = WorkspaceSmokeFailureClassification.None;
        var originalFailureMessage = string.Empty;
        WorkspaceSmokeFailureClassification cleanupFailureClassification = WorkspaceSmokeFailureClassification.None;
        var cleanupFailureMessage = string.Empty;
        WorkspaceSnapshot? snapshot = null;
        var workspaceService = _workspaceServiceFactory.Create();
        IDisposable? lockHandle = null;
        var skipReason = string.Empty;
        var runTimeout = options.Timeout ?? WorkspaceSmokeTimeouts.Resolve(definition.TimeoutClass);

        void Report(string phaseName, string message)
            => options.Progress?.Report(new WorkspaceSmokeProgressUpdate
            {
                RunId = runId,
                MatrixRunId = matrixRunId,
                TemplateId = definition.TemplateId,
                Phase = phaseName,
                Message = message,
            });

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
            Report("preflightCleanup", "Cleaning stale smoke resources before the run starts.");
            inventoryBefore = await _runtimeOwnershipService.BuildInventoryAsync(new RuntimeOwnershipQuery { OwnerKind = "smoke" }, cancellationToken);
            await WorkspaceSmokeArtifacts.WriteRuntimeInventoryArtifactsAsync(artifactDirectory, "before", inventoryBefore, cancellationToken);

            var preflightCleanup = await _smokeOwnershipService.CleanupAsync(new global::OpenCode.Workspace.Core.Runtime.SmokeCleanupOptions(DryRun: false, IncludeAll: true, RunId: null, OutputFormat: "json"), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(cleanupArtifactDirectory, "host-preflight-cleanup.json"), System.Text.Json.JsonSerializer.Serialize(preflightCleanup, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            if (!preflightCleanup.Succeeded || !preflightCleanup.VerificationSucceeded)
            {
                throw new InvalidOperationException("Runtime resource exhaustion: stale smoke-owned Docker resources could not be cleaned before starting the smoke run.");
            }

            phase = WorkspaceSmokePhase.Creation;
            Report("creatingWorkspace", "Creating the temporary smoke workspace.");
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
            Report("provisioning", "Provisioning workspace runtime and services.");
            await WorkspaceSmokeTimeouts.RunWithTimeoutAsync(
                token => workspaceService.ProvisionAsync(snapshot, Log, token),
                runTimeout,
                WorkspaceSmokeFailureClassification.ProvisioningTimeout,
                $"Smoke provisioning timed out after {runTimeout}.",
                cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "provisioning.log"), provisioningLog.ToString(), cancellationToken);

            inventoryActive = await _runtimeOwnershipService.BuildInventoryAsync(new RuntimeOwnershipQuery { OwnerKind = "smoke", RunId = runId }, cancellationToken);
            await WorkspaceSmokeArtifacts.WriteRuntimeInventoryArtifactsAsync(artifactDirectory, "active", inventoryActive, cancellationToken);
            await CaptureDockerArtifactsAsync(snapshot.Paths, expanded, dockerArtifactDirectory, cancellationToken);

            phase = WorkspaceSmokePhase.Validation;
            Report("validating", "Running smoke validators.");
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
                Report("validating", $"Running validator '{validator.ValidatorId}'.");
                var validatorResult = await WorkspaceSmokeTimeouts.RunWithTimeoutAsync(
                    token => validator.ValidateAsync(context, token),
                    runTimeout,
                    WorkspaceSmokeFailureClassification.ValidationTimeout,
                    $"Smoke validation timed out after {runTimeout}.",
                    cancellationToken);
                validators.Add(validatorResult);
                await File.WriteAllTextAsync(Path.Combine(validationArtifactDirectory, $"{validator.ValidatorId}.json"), System.Text.Json.JsonSerializer.Serialize(validatorResult, WorkspaceSmokeContract.JsonOptions), cancellationToken);
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
            Report("completed", failures.Length == 0 ? "Smoke run completed successfully." : "Smoke run completed with validation failures.");
            status = failures.Length == 0 ? WorkspaceSmokeStatus.Passed : WorkspaceSmokeStatus.Failed;
        }
        catch (Exception exception)
        {
            status = WorkspaceSmokeExecutionOutcomeClassifier.ClassifyException(exception, cancellationToken);
            if (status == WorkspaceSmokeStatus.Cancelled)
            {
                Report("cleaningUp", "Smoke run was cancelled. Cleaning up owned resources.");
                failureClassification = WorkspaceSmokeFailureClassification.Cancelled;
                failureMessage = "Smoke run was cancelled.";
            }
            else
            {
                failureClassification = WorkspaceSmokeFailureClassifier.Classify(exception);
                failureMessage = exception.Message;
                warnings.Add(exception.ToString());
            }
            originalFailureClassification = failureClassification;
            originalFailureMessage = failureMessage;
        }
        finally
        {
            try
            {
                phase = WorkspaceSmokePhase.Cleanup;
                Report("cleaningUp", "Cleaning up smoke-owned runtime resources.");
                var shouldRetainRuntime = options.KeepRuntimeOnFailure && !string.IsNullOrWhiteSpace(failureMessage);
                using var cleanupSource = new CancellationTokenSource(WorkspaceSmokeTimeouts.CleanupTimeout);
                if (!shouldRetainRuntime)
                {
                    for (var attempt = 1; attempt <= 3; attempt++)
                    {
                        cleanupResult = await WorkspaceSmokeTimeouts.RunWithTimeoutAsync(
                            token => _smokeOwnershipService.CleanupAsync(new global::OpenCode.Workspace.Core.Runtime.SmokeCleanupOptions(DryRun: false, IncludeAll: false, RunId: runId, OutputFormat: "json"), token),
                            WorkspaceSmokeTimeouts.CleanupTimeout,
                            WorkspaceSmokeFailureClassification.CleanupTimeout,
                            $"Smoke cleanup timed out after {WorkspaceSmokeTimeouts.CleanupTimeout}.",
                            cleanupSource.Token);
                        if (cleanupResult.VerificationSucceeded || attempt == 3)
                        {
                            break;
                        }

                        warnings.Add($"Cleanup verification for run '{runId}' was incomplete on attempt {attempt}; retrying the same run-scoped cleanup.");
                        await Task.Delay(TimeSpan.FromMilliseconds(500), cleanupSource.Token);
                    }
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

                await File.WriteAllTextAsync(Path.Combine(cleanupArtifactDirectory, "run-cleanup.json"), System.Text.Json.JsonSerializer.Serialize(cleanupResult, WorkspaceSmokeContract.JsonOptions), cleanupSource.Token);
                Report("verifyingCleanup", "Verifying smoke resource cleanup.");
                inventoryAfter = await _runtimeOwnershipService.BuildInventoryAsync(new RuntimeOwnershipQuery { OwnerKind = "smoke" }, cleanupSource.Token);
                await WorkspaceSmokeArtifacts.WriteRuntimeInventoryArtifactsAsync(artifactDirectory, "after", inventoryAfter, cleanupSource.Token);
            }
            catch (Exception cleanupException)
            {
                warnings.Add(cleanupException.ToString());
                cleanupFailureClassification = WorkspaceSmokeFailureClassifier.Classify(cleanupException);
                cleanupFailureMessage = cleanupException.Message;
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
                        using var deleteSource = new CancellationTokenSource(WorkspaceSmokeTimeouts.CleanupTimeout);
                        await _containerRuntime.NormalizeWorkspaceFilePermissionsAsync(snapshot.Paths.RootPath, cancellationToken: deleteSource.Token);
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
        var terminalStatus = WorkspaceSmokeExecutionOutcomeClassifier.ResolveTerminalStatus(status, cleanupResult?.VerificationSucceeded ?? false);
        if (terminalStatus != status)
        {
            status = terminalStatus;
            failureClassification = WorkspaceSmokeFailureClassification.CleanupFailure;
            finalMessage = "Run cleanup verification failed.";
            cleanupFailureClassification = WorkspaceSmokeFailureClassification.CleanupFailure;
            cleanupFailureMessage = finalMessage;
        }

        var finalPhase = status == WorkspaceSmokeStatus.Passed ? WorkspaceSmokePhase.Completed : phase;
        Report(status == WorkspaceSmokeStatus.Cancelled ? "completed" : finalPhase.ToString().ToLowerInvariant(), status == WorkspaceSmokeStatus.Cancelled ? "Smoke run finished in cancelled state." : "Smoke run finished.");
        var summaryJsonPath = Path.Combine(artifactDirectory, "summary.json");
        var summaryTextPath = Path.Combine(artifactDirectory, "summary.txt");
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
            OriginalFailureClassification = originalFailureClassification,
            OriginalFailureMessage = originalFailureMessage,
            CleanupFailureClassification = cleanupFailureClassification,
            CleanupFailureMessage = cleanupFailureMessage,
            Validators = validators.ToArray(),
            ResourceCountsBefore = WorkspaceSmokeResourceCounts.FromInventory(inventoryBefore),
            ResourceCountsActive = WorkspaceSmokeResourceCounts.FromInventory(inventoryActive),
            ResourceCountsAfter = WorkspaceSmokeResourceCounts.FromInventory(inventoryAfter),
            CleanupResult = cleanupResult,
            CleanupVerificationSucceeded = cleanupResult?.VerificationSucceeded ?? false,
            ArtifactDirectory = artifactDirectory,
            SummaryJsonPath = summaryJsonPath,
            SummaryTextPath = summaryTextPath,
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
        var status = WorkspaceSmokeStatus.Passed;
        WorkspaceSmokeFailureClassification failureClassification = WorkspaceSmokeFailureClassification.None;
        var failureMessage = string.Empty;
        var selectedTemplates = definitions.Select(item => item.TemplateId).ToArray();
        var executionToken = cancellationToken;
        CancellationTokenSource? matrixTimeoutSource = null;
        CancellationTokenSource? linkedSource = null;

        void Report(string phaseName, string message, string templateId = "")
            => options.Progress?.Report(new WorkspaceSmokeProgressUpdate
            {
                MatrixRunId = matrixRunId,
                TemplateId = templateId,
                Phase = phaseName,
                Message = message,
            });

        if (options.MatrixTimeout is not null)
        {
            matrixTimeoutSource = new CancellationTokenSource(options.MatrixTimeout.Value);
            linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, matrixTimeoutSource.Token);
            executionToken = linkedSource.Token;
        }

        try
        {
            if (definitions.Any(item => item.ResourceClass == WorkspaceSmokeResourceClass.OracleExclusive))
            {
                lockHandle = _lockService.AcquireOracleExclusiveLock();
            }

            Report("preflightCleanup", "Cleaning stale smoke resources before the matrix starts.");
            var preflight = await _smokeOwnershipService.CleanupAsync(new global::OpenCode.Workspace.Core.Runtime.SmokeCleanupOptions(DryRun: false, IncludeAll: true, RunId: null, OutputFormat: "json"), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(hostBeforeDirectory, "cleanup.json"), System.Text.Json.JsonSerializer.Serialize(preflight, WorkspaceSmokeContract.JsonOptions), executionToken);
            if (!preflight.Succeeded || !preflight.VerificationSucceeded)
            {
                throw new InvalidOperationException("Runtime resource exhaustion: stale smoke-owned Docker resources could not be cleaned before starting the matrix.");
            }

            var initialInventory = await _runtimeOwnershipService.BuildInventoryAsync(new RuntimeOwnershipQuery { OwnerKind = "smoke" }, executionToken);
            await WorkspaceSmokeArtifacts.WriteRuntimeInventoryArtifactsAsync(hostBeforeDirectory, "host-before", initialInventory, executionToken);
            if (initialInventory.Resources.Count > 0)
            {
                throw new InvalidOperationException("Runtime resource exhaustion: smoke-owned Docker resources are still active after matrix preflight cleanup.");
            }

            foreach (var definition in OrderDefinitions(definitions))
            {
                Report("preparing", $"Starting smoke run for '{definition.TemplateId}'.", definition.TemplateId);
                var result = await _runner.RunAsync(definition, new WorkspaceSmokeRunnerOptions
                {
                    MatrixRunId = matrixRunId,
                    ArtifactsRoot = artifactDirectory,
                    OracleLockAlreadyHeld = definition.ResourceClass == WorkspaceSmokeResourceClass.OracleExclusive && lockHandle is not null,
                    KeepRuntimeOnFailure = options.KeepRuntimeOnFailure,
                    KeepWorkspace = options.KeepWorkspace,
                    Timeout = options.RunTimeoutOverride,
                    Progress = options.Progress,
                }, executionToken);
                results.Add(result);

                var unsafeToContinue = result.CleanupResult is { VerificationSucceeded: false } && !options.KeepRuntimeOnFailure
                    || result.FailureClassification is WorkspaceSmokeFailureClassification.RuntimeResourceExhaustion or WorkspaceSmokeFailureClassification.LockAcquisitionFailure;
                if (unsafeToContinue)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (matrixTimeoutSource is { IsCancellationRequested: true } && !cancellationToken.IsCancellationRequested)
        {
            status = WorkspaceSmokeStatus.Failed;
            failureClassification = WorkspaceSmokeFailureClassification.MatrixTimeout;
            failureMessage = $"Smoke matrix timed out after {options.MatrixTimeout}.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = WorkspaceSmokeStatus.Cancelled;
            failureClassification = WorkspaceSmokeFailureClassification.Cancelled;
            failureMessage = "Smoke matrix was cancelled.";
        }
        catch (Exception exception)
        {
            status = WorkspaceSmokeStatus.Failed;
            failureClassification = WorkspaceSmokeFailureClassifier.Classify(exception);
            failureMessage = exception.Message;
        }
        finally
        {
            try
            {
                using var cleanupSource = new CancellationTokenSource(WorkspaceSmokeTimeouts.CleanupTimeout);
                Report("cleaningUp", "Cleaning up matrix-owned smoke resources.");
                finalCleanup = await _smokeOwnershipService.CleanupAsync(new global::OpenCode.Workspace.Core.Runtime.SmokeCleanupOptions(DryRun: false, IncludeAll: true, RunId: null, OutputFormat: "json"), cleanupSource.Token);
                finalInventory = await _runtimeOwnershipService.BuildInventoryAsync(new RuntimeOwnershipQuery { OwnerKind = "smoke" }, cleanupSource.Token);
                await File.WriteAllTextAsync(Path.Combine(hostAfterDirectory, "cleanup.json"), System.Text.Json.JsonSerializer.Serialize(finalCleanup, WorkspaceSmokeContract.JsonOptions), cleanupSource.Token);
                await WorkspaceSmokeArtifacts.WriteRuntimeInventoryArtifactsAsync(hostAfterDirectory, "host-after", finalInventory, cleanupSource.Token);
            }
            catch (Exception cleanupException)
            {
                finalCleanup = new global::OpenCode.Workspace.Core.Runtime.SmokeCleanupResult
                {
                    Succeeded = false,
                    DryRun = false,
                    VerificationSucceeded = false,
                    Errors = [cleanupException.Message],
                };
                if (failureClassification == WorkspaceSmokeFailureClassification.None)
                {
                    failureClassification = WorkspaceSmokeFailureClassifier.Classify(cleanupException);
                    failureMessage = cleanupException.Message;
                    status = WorkspaceSmokeStatus.Failed;
                }
            }

            lockHandle?.Dispose();
            linkedSource?.Dispose();
            matrixTimeoutSource?.Dispose();
        }

        finalInventory ??= new RuntimeResourceInventory();
        status = status == WorkspaceSmokeStatus.Cancelled
            ? WorkspaceSmokeStatus.Cancelled
            : results.Any(item => item.Status == WorkspaceSmokeStatus.Failed)
                || finalCleanup is { VerificationSucceeded: false }
                || finalInventory.Resources.Count > 0
                || failureClassification != WorkspaceSmokeFailureClassification.None
                    ? WorkspaceSmokeStatus.Failed
                    : results.Count > 0 && results.All(item => item.Status == WorkspaceSmokeStatus.Skipped)
                        ? WorkspaceSmokeStatus.Skipped
                        : WorkspaceSmokeStatus.Passed;
        if (status == WorkspaceSmokeStatus.Failed && failureClassification == WorkspaceSmokeFailureClassification.None && finalCleanup is { VerificationSucceeded: false })
        {
            failureClassification = WorkspaceSmokeFailureClassification.CleanupFailure;
            failureMessage = "Final host cleanup verification failed.";
        }

        var summaryJsonPath = Path.Combine(artifactDirectory, "matrix-summary.json");
        var summaryTextPath = Path.Combine(artifactDirectory, "matrix-summary.txt");
        Report("completed", status == WorkspaceSmokeStatus.Cancelled ? "Smoke matrix finished in cancelled state." : "Smoke matrix finished.");
        var matrixResult = new WorkspaceSmokeMatrixResult
        {
            MatrixRunId = matrixRunId,
            SelectedTemplates = selectedTemplates,
            StartedUtc = startedUtc,
            FinishedUtc = DateTimeOffset.UtcNow,
            Results = results.ToArray(),
            PassedCount = results.Count(item => item.Status == WorkspaceSmokeStatus.Passed),
            FailedCount = results.Count(item => item.Status == WorkspaceSmokeStatus.Failed) + ((finalCleanup is { VerificationSucceeded: false } || finalInventory.Resources.Count > 0 || failureClassification == WorkspaceSmokeFailureClassification.MatrixTimeout) ? 1 : 0),
            SkippedCount = results.Count(item => item.Status == WorkspaceSmokeStatus.Skipped),
            FinalHostCleanupResult = finalCleanup,
            FinalRuntimeInventory = finalInventory,
            Status = status,
            FailureClassification = failureClassification,
            FailureMessage = failureMessage,
            ArtifactDirectory = artifactDirectory,
            SummaryJsonPath = summaryJsonPath,
            SummaryTextPath = summaryTextPath,
        };
        WorkspaceSmokeArtifacts.WriteMatrixSummary(artifactDirectory, matrixResult);
        return matrixResult;
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
