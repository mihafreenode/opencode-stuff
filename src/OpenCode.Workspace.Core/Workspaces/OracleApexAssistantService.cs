using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public interface IOracleApexAssistantSynchronizationService
{
    Task<WorkspaceSynchronizationStatusResult> GetStatusAsync(WorkspaceSnapshot snapshot, string? environmentName = null, CancellationToken cancellationToken = default);
    Task<WorkspaceSynchronizationOperationResult> ValidateAsync(WorkspaceSnapshot snapshot, string? environmentName = null, CancellationToken cancellationToken = default);
    Task<WorkspaceSynchronizationOperationResult> ImportAsync(WorkspaceSnapshot snapshot, string? environmentName = null, CancellationToken cancellationToken = default);
}

public sealed class OracleApexAssistantService
{
    private readonly OracleApexIntentPlanner _intentPlanner;
    private readonly OracleApexWorkspaceIndexBuilder _workspaceIndexBuilder;
    private readonly OracleApexCodeActionService _codeActionService;
    private readonly IOracleApexSemanticEditor _semanticEditor;
    private readonly OracleApexComponentCatalog _componentCatalog;
    private readonly IOracleApexAssistantSynchronizationService _synchronizationService;
    private readonly OracleApexValidationFeedbackService _validationFeedbackService;
    private readonly OracleApexSemanticRepairService _repairService;

    public OracleApexAssistantService(
        IOracleApexAssistantSynchronizationService synchronizationService,
        OracleApexIntentPlanner? intentPlanner = null,
        OracleApexWorkspaceIndexBuilder? workspaceIndexBuilder = null,
        OracleApexCodeActionService? codeActionService = null,
        IOracleApexSemanticEditor? semanticEditor = null,
        OracleApexComponentCatalog? componentCatalog = null,
        OracleApexValidationFeedbackService? validationFeedbackService = null,
        OracleApexSemanticRepairService? repairService = null)
    {
        _componentCatalog = componentCatalog ?? OracleApexComponentCatalog.Default;
        _workspaceIndexBuilder = workspaceIndexBuilder ?? new OracleApexWorkspaceIndexBuilder();
        _semanticEditor = semanticEditor ?? new OracleApexSemanticEditor(_workspaceIndexBuilder, _componentCatalog);
        _codeActionService = codeActionService ?? new OracleApexCodeActionService(_workspaceIndexBuilder, _semanticEditor);
        _intentPlanner = intentPlanner ?? new OracleApexIntentPlanner(_workspaceIndexBuilder, _componentCatalog, _codeActionService, _semanticEditor);
        _synchronizationService = synchronizationService;
        _validationFeedbackService = validationFeedbackService ?? new OracleApexValidationFeedbackService();
        _repairService = repairService ?? new OracleApexSemanticRepairService(_workspaceIndexBuilder, _codeActionService);
    }

    public OracleApexAssistantPlanResponse CreatePlan(WorkspaceSnapshot snapshot, OracleApexAssistantRequest request)
    {
        var environment = ResolveEnvironment(snapshot, request.EnvironmentName);
        var environmentName = ResolveEnvironmentName(snapshot, request.EnvironmentName);
        var currentIndex = _workspaceIndexBuilder.Build(snapshot.Paths.RootPath, environment, environmentName);
        var planResult = _intentPlanner.CreatePlan(snapshot.Paths.RootPath, environment, environmentName, request.Prompt);
        var warnings = new List<string>(planResult.Plan.Warnings);

        AppendWorkspaceWarnings(snapshot, currentIndex, environmentName, warnings);

        var review = BuildReview(planResult.Plan, warnings);
        return new OracleApexAssistantPlanResponse
        {
            Request = request,
            Plan = planResult.Plan,
            Review = review,
            Classification = planResult.Plan.Classification,
            Warnings = warnings,
            Assumptions = planResult.Plan.Assumptions,
            UnresolvedQuestions = planResult.Plan.UnresolvedQuestions,
            ConfirmationRequired = planResult.Plan.RequiresConfirmation,
            WorkspaceIndex = currentIndex,
            PostEditBehavior = ResolvePostEditBehavior(request, snapshot.Synchronization.DefaultEnvironment),
            SafeToContinueDeployment = planResult.Validation.IsValid && warnings.Count == 0,
        };
    }

    public async Task<OracleApexAssistantExecutionResponse> ExecutePlanAsync(WorkspaceSnapshot snapshot, OracleApexAssistantRequest request, OracleApexEditPlan plan, CancellationToken cancellationToken = default)
    {
        var environment = ResolveEnvironment(snapshot, request.EnvironmentName);
        var environmentName = ResolveEnvironmentName(snapshot, request.EnvironmentName);
        var postEditBehavior = ResolvePostEditBehavior(request, snapshot.Synchronization.DefaultEnvironment);

        if (plan.UnresolvedQuestions.Count > 0)
        {
            return new OracleApexAssistantExecutionResponse
            {
                IsSuccess = false,
                Summary = "Plan execution is blocked until unresolved questions are answered.",
                UnresolvedQuestions = plan.UnresolvedQuestions,
                WorkspaceIndex = _workspaceIndexBuilder.Build(snapshot.Paths.RootPath, environment, environmentName),
                PostEditBehavior = postEditBehavior,
            };
        }

        if (plan.RequiresConfirmation && !request.ConfirmPlan)
        {
            return new OracleApexAssistantExecutionResponse
            {
                IsSuccess = false,
                Summary = "Plan execution requires explicit approval.",
                WorkspaceIndex = _workspaceIndexBuilder.Build(snapshot.Paths.RootPath, environment, environmentName),
                PostEditBehavior = postEditBehavior,
                ConfirmationRequired = true,
            };
        }

        var execution = _intentPlanner.ExecutePlan(snapshot.Paths.RootPath, environment, environmentName, plan, confirmDestructive: request.ConfirmPlan);
        if (!execution.IsSuccess)
        {
            return new OracleApexAssistantExecutionResponse
            {
                IsSuccess = false,
                Summary = execution.Summary,
                ChangedFiles = execution.ChangedFiles,
                Diagnostics = execution.Diagnostics,
                WorkspaceIndex = execution.WorkspaceIndex,
                PostEditBehavior = postEditBehavior,
            };
        }

        WorkspaceSynchronizationOperationResult? validationResult = null;
        WorkspaceSynchronizationOperationResult? importResult = null;
        WorkspaceSynchronizationStatusResult? refreshedStatus = null;
        OracleApexValidationResult? compilerValidation = null;
        OracleApexEditPlan? repairPlan = null;
        var deploymentSafe = true;
        var workingSnapshot = snapshot;

        if (postEditBehavior != OracleApexAssistantPostEditBehavior.SourceOnly)
        {
            validationResult = await _synchronizationService.ValidateAsync(workingSnapshot, environmentName, cancellationToken).ConfigureAwait(false);
            compilerValidation = NormalizeCompilerValidation(validationResult.Validation, validationResult.ProcessResult, execution.WorkspaceIndex, plan);
            _validationFeedbackService.PersistEvidence(snapshot, compilerValidation);
            if (validationResult.Snapshot.DefaultEnvironment?.State == WorkspaceSynchronizationState.ValidationFailed || validationResult.ProcessResult?.IsSuccess == false)
            {
                repairPlan = _repairService.CreateRepairPlan(snapshot.Paths.RootPath, environment, environmentName, plan, compilerValidation);
                if (ShouldAutoRepair(snapshot, request, repairPlan))
                {
                    var repairExecution = _intentPlanner.ExecutePlan(snapshot.Paths.RootPath, environment, environmentName, repairPlan, confirmDestructive: true);
                    if (repairExecution.IsSuccess)
                    {
                        _validationFeedbackService.PersistEvidence(snapshot, compilerValidation, repairPlan);
                        workingSnapshot = CloneWithSynchronization(snapshot, validationResult.Snapshot);
                        validationResult = await _synchronizationService.ValidateAsync(workingSnapshot, environmentName, cancellationToken).ConfigureAwait(false);
                        compilerValidation = NormalizeCompilerValidation(validationResult.Validation, validationResult.ProcessResult, repairExecution.WorkspaceIndex, repairPlan);
                        _validationFeedbackService.PersistEvidence(snapshot, compilerValidation);
                        if (validationResult.Snapshot.DefaultEnvironment?.State != WorkspaceSynchronizationState.ValidationFailed && validationResult.ProcessResult?.IsSuccess != false)
                        {
                            execution = new OracleApexEditPlanExecutionResult
                            {
                                IsSuccess = true,
                                Summary = repairExecution.Summary,
                                ChangedFiles = execution.ChangedFiles.Concat(repairExecution.ChangedFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                                Diagnostics = repairExecution.Diagnostics,
                                WorkspaceIndex = repairExecution.WorkspaceIndex,
                            };
                        }
                    }
                }

                if (validationResult.Snapshot.DefaultEnvironment?.State == WorkspaceSynchronizationState.ValidationFailed || validationResult.ProcessResult?.IsSuccess == false)
                {
                    deploymentSafe = false;
                    return new OracleApexAssistantExecutionResponse
                    {
                        IsSuccess = true,
                        Summary = "Plan applied, but validation failed. Import was blocked.",
                        ChangedFiles = execution.ChangedFiles,
                        Diagnostics = execution.Diagnostics,
                        WorkspaceIndex = execution.WorkspaceIndex,
                        PostEditBehavior = postEditBehavior,
                        ValidationResult = validationResult,
                        CompilerValidation = compilerValidation,
                        SuggestedRepairPlan = repairPlan,
                        RepairReview = repairPlan is null ? string.Empty : BuildReview(repairPlan, repairPlan.Warnings),
                        SafeToContinueDeployment = false,
                        Stage = OracleApexAssistantStage.SqlclValidation,
                        GitStatusSummary = snapshot.Safety.AdvancedGit.StatusSummary,
                    };
                }
            }
        }

        if (postEditBehavior == OracleApexAssistantPostEditBehavior.ValidateAndImport)
        {
            var syncState = snapshot.Synchronization.DefaultEnvironment?.State is WorkspaceSynchronizationState.Diverged or WorkspaceSynchronizationState.DeploymentAhead
                ? snapshot.Synchronization.DefaultEnvironment.State
                : validationResult?.Snapshot.DefaultEnvironment?.State ?? snapshot.Synchronization.DefaultEnvironment?.State ?? WorkspaceSynchronizationState.Unknown;
            if (syncState is WorkspaceSynchronizationState.Diverged or WorkspaceSynchronizationState.DeploymentAhead)
            {
                return new OracleApexAssistantExecutionResponse
                {
                    IsSuccess = true,
                    Summary = "Plan applied and validated, but import was blocked because the workspace synchronization state is unsafe for automatic deployment.",
                    ChangedFiles = execution.ChangedFiles,
                    Diagnostics = execution.Diagnostics,
                    WorkspaceIndex = execution.WorkspaceIndex,
                    PostEditBehavior = postEditBehavior,
                    ValidationResult = validationResult,
                    CompilerValidation = compilerValidation,
                    SafeToContinueDeployment = false,
                    Warnings = [$"Synchronization state '{syncState}' blocks automatic import."],
                    Stage = OracleApexAssistantStage.Import,
                    GitStatusSummary = snapshot.Safety.AdvancedGit.StatusSummary,
                };
            }

            if (!IsDevelopmentEnvironment(environmentName) && !request.AllowNonDevelopmentDeployment)
            {
                return new OracleApexAssistantExecutionResponse
                {
                    IsSuccess = true,
                    Summary = "Plan applied and validated, but import was blocked because the target environment is not a development environment.",
                    ChangedFiles = execution.ChangedFiles,
                    Diagnostics = execution.Diagnostics,
                    WorkspaceIndex = execution.WorkspaceIndex,
                    PostEditBehavior = postEditBehavior,
                    ValidationResult = validationResult,
                    CompilerValidation = compilerValidation,
                    SafeToContinueDeployment = false,
                    Warnings = [$"Environment '{environmentName}' requires explicit override before deployment."],
                    Stage = OracleApexAssistantStage.Import,
                    GitStatusSummary = snapshot.Safety.AdvancedGit.StatusSummary,
                };
            }

            importResult = await _synchronizationService.ImportAsync(workingSnapshot, environmentName, cancellationToken).ConfigureAwait(false);
            deploymentSafe = importResult.ProcessResult?.IsSuccess != false;
            refreshedStatus = await _synchronizationService.GetStatusAsync(workingSnapshot, environmentName, cancellationToken).ConfigureAwait(false);
        }

        return new OracleApexAssistantExecutionResponse
        {
            IsSuccess = true,
            Summary = BuildExecutionSummary(postEditBehavior, execution, validationResult, importResult),
            ChangedFiles = execution.ChangedFiles,
            Diagnostics = execution.Diagnostics,
            WorkspaceIndex = execution.WorkspaceIndex,
            PostEditBehavior = postEditBehavior,
            ValidationResult = validationResult,
            CompilerValidation = compilerValidation,
            ImportResult = importResult,
            SafeToContinueDeployment = deploymentSafe,
            SuggestedRepairPlan = repairPlan,
            RepairReview = repairPlan is null ? string.Empty : BuildReview(repairPlan, repairPlan.Warnings),
            Synchronization = refreshedStatus?.Snapshot ?? importResult?.Snapshot ?? validationResult?.Snapshot ?? snapshot.Synchronization,
            Stage = postEditBehavior == OracleApexAssistantPostEditBehavior.ValidateAndImport ? OracleApexAssistantStage.Preview : postEditBehavior == OracleApexAssistantPostEditBehavior.ValidateOnly ? OracleApexAssistantStage.SqlclValidation : OracleApexAssistantStage.SemanticGeneration,
            GitStatusSummary = snapshot.Safety.AdvancedGit.StatusSummary,
        };
    }

    public OracleApexAssistantRepairPlanResponse CreateRepairPlan(WorkspaceSnapshot snapshot, OracleApexAssistantRequest request, OracleApexEditPlan sourcePlan, OracleApexValidationResult validation)
    {
        var environment = ResolveEnvironment(snapshot, request.EnvironmentName);
        var environmentName = ResolveEnvironmentName(snapshot, request.EnvironmentName);
        var repairPlan = _repairService.CreateRepairPlan(snapshot.Paths.RootPath, environment, environmentName, sourcePlan, validation);
        return new OracleApexAssistantRepairPlanResponse
        {
            Plan = repairPlan,
            Review = BuildReview(repairPlan, repairPlan.Warnings),
            CompilerValidation = validation,
        };
    }

    public Task<OracleApexAssistantExecutionResponse> ExecuteRepairPlanAsync(WorkspaceSnapshot snapshot, OracleApexAssistantRequest request, OracleApexEditPlan repairPlan, CancellationToken cancellationToken = default)
        => ExecutePlanAsync(snapshot, new OracleApexAssistantRequest
        {
            Prompt = request.Prompt,
            EnvironmentName = request.EnvironmentName,
            ConfirmPlan = true,
            EnableSafeAutomaticRepair = request.EnableSafeAutomaticRepair,
            AllowNonDevelopmentDeployment = request.AllowNonDevelopmentDeployment,
            PostEditBehavior = request.PostEditBehavior == OracleApexAssistantPostEditBehavior.Auto ? OracleApexAssistantPostEditBehavior.ValidateOnly : request.PostEditBehavior,
        }, repairPlan, cancellationToken);

    private static OracleApexEnvironmentPreferences ResolveEnvironment(WorkspaceSnapshot snapshot, string? environmentName)
    {
        if (snapshot.Definition.Oracle.Apex.Environments.Count == 0)
        {
            throw new InvalidOperationException("Workspace does not define an Oracle APEX environment.");
        }

        var resolved = string.IsNullOrWhiteSpace(environmentName)
            ? snapshot.Definition.Oracle.Apex.DefaultEnvironment ?? snapshot.Definition.Oracle.Apex.Environments.Keys.First()
            : environmentName;
        return snapshot.Definition.Oracle.Apex.Environments[resolved!];
    }

    private static string ResolveEnvironmentName(WorkspaceSnapshot snapshot, string? environmentName)
        => string.IsNullOrWhiteSpace(environmentName)
            ? snapshot.Synchronization.DefaultEnvironment?.EnvironmentName ?? snapshot.Definition.Oracle.Apex.DefaultEnvironment ?? "dev"
            : environmentName;

    private static OracleApexAssistantPostEditBehavior ResolvePostEditBehavior(OracleApexAssistantRequest request, WorkspaceSynchronizationEnvironmentSnapshot? environment)
    {
        if (request.PostEditBehavior != OracleApexAssistantPostEditBehavior.Auto)
        {
            return request.PostEditBehavior;
        }

        return environment?.SyncMode == WorkspaceSynchronizationModes.WatchLive
            ? OracleApexAssistantPostEditBehavior.ValidateAndImport
            : OracleApexAssistantPostEditBehavior.ValidateOnly;
    }

    private static void AppendWorkspaceWarnings(WorkspaceSnapshot snapshot, OracleApexWorkspaceIndex index, string environmentName, ICollection<string> warnings)
    {
        var defaultEnvironment = snapshot.Synchronization.DefaultEnvironment;
        if (defaultEnvironment is not null)
        {
            if (defaultEnvironment.State == WorkspaceSynchronizationState.DeploymentAhead)
            {
                warnings.Add("Oracle APEX is ahead of the workspace source. Review Builder-side changes before planning automatic deployment.");
            }

            if (defaultEnvironment.State == WorkspaceSynchronizationState.Diverged)
            {
                warnings.Add("Workspace synchronization is diverged. Automatic deployment should not continue until drift is resolved.");
            }
        }

        var atlasStatePath = Path.Combine(snapshot.Paths.OpencodePath, "knowledge", "apexlang-atlas", "state.json");
        if (!File.Exists(atlasStatePath))
        {
            warnings.Add("Workspace index may be stale because Atlas state metadata is missing.");
        }

        if (!string.Equals(environmentName, "dev", StringComparison.OrdinalIgnoreCase) && !string.Equals(environmentName, "development", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Target environment '{environmentName}' is not a development environment.");
        }

        var application = index.SemanticModel.Application;
        var declaredCatalogVersion = application?.GetProperty("apexlang-version") ?? application?.GetProperty("apex-version");
        var catalogVersion = OracleApexComponentCatalog.Default.Components.Values.SelectMany(item => item.SupportedApexVersions).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).LastOrDefault() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(declaredCatalogVersion) && !string.Equals(declaredCatalogVersion, catalogVersion, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Component catalog version '{catalogVersion}' does not match project metadata version '{declaredCatalogVersion}'.");
        }
    }

    private static string BuildReview(OracleApexEditPlan plan, IReadOnlyCollection<string> warnings)
    {
        var lines = new List<string>
        {
            $"Summary: {plan.Summary}",
            $"Classification: {plan.Classification}",
            $"Confirmation required: {(plan.RequiresConfirmation ? "Yes" : "No")}",
            $"Estimated complexity: {plan.EstimatedComplexity}",
        };

        if (plan.NewPages.Count > 0)
        {
            lines.Add($"New pages: {string.Join(", ", plan.NewPages)}");
        }

        if (plan.NewSharedComponents.Count > 0)
        {
            lines.Add($"New shared components: {string.Join(", ", plan.NewSharedComponents)}");
        }

        if (plan.NewNavigationEntries.Count > 0)
        {
            lines.Add($"New navigation entries: {string.Join(", ", plan.NewNavigationEntries)}");
        }

        if (plan.SecurityChanges.Count > 0)
        {
            lines.Add("Security changes:");
            foreach (var change in plan.SecurityChanges)
            {
                lines.Add($"- {change}");
            }
        }

        if (plan.ValidationChanges.Count > 0)
        {
            lines.Add("Validation changes:");
            foreach (var change in plan.ValidationChanges)
            {
                lines.Add($"- {change}");
            }
        }

        if (plan.DeploymentTargets.Count > 0)
        {
            lines.Add($"Deployment targets: {string.Join(", ", plan.DeploymentTargets)}");
        }

        if (plan.Alternatives.Count > 0)
        {
            lines.Add("Alternatives:");
            foreach (var alternative in plan.Alternatives)
            {
                var recommended = alternative.IsRecommended ? " (recommended)" : string.Empty;
                lines.Add($"- {alternative.Label}{recommended}: {alternative.Description}");
                lines.Add($"  Trade-offs: {alternative.TradeOffs}");
            }
        }

        lines.Add("Operations:");

        foreach (var operation in plan.Operations.OrderBy(item => item.Sequence))
        {
            lines.Add($"- {operation.Sequence}. {operation.Title}");
            lines.Add($"  Target: {operation.TargetComponentType} {operation.TargetIdentifier}".TrimEnd());
            if (operation.Properties.Count > 0)
            {
                lines.Add($"  Properties: {string.Join(", ", operation.Properties.Select(pair => $"{pair.Key}={pair.Value}"))}");
            }

            if (operation.References.Count > 0)
            {
                lines.Add($"  References: {string.Join(", ", operation.References)}");
            }

            if (operation.ExpectedChangedFiles.Count > 0)
            {
                lines.Add($"  Files: {string.Join(", ", operation.ExpectedChangedFiles)}");
            }
        }

        if (plan.Assumptions.Count > 0)
        {
            lines.Add("Assumptions:");
            foreach (var assumption in plan.Assumptions)
            {
                lines.Add($"- {assumption}");
            }
        }

        if (warnings.Count > 0)
        {
            lines.Add("Warnings:");
            foreach (var warning in warnings)
            {
                lines.Add($"- {warning}");
            }
        }

        if (plan.UnresolvedQuestions.Count > 0)
        {
            lines.Add("Unresolved Questions:");
            foreach (var question in plan.UnresolvedQuestions)
            {
                lines.Add($"- {question}");
            }
        }

        if (plan.ExpectedChangedFiles.Count > 0)
        {
            lines.Add($"Expected files: {string.Join(", ", plan.ExpectedChangedFiles)}");
        }

        return string.Join("\n", lines);
    }

    private static string BuildExecutionSummary(OracleApexAssistantPostEditBehavior behavior, OracleApexEditPlanExecutionResult execution, WorkspaceSynchronizationOperationResult? validationResult, WorkspaceSynchronizationOperationResult? importResult)
    {
        return behavior switch
        {
            OracleApexAssistantPostEditBehavior.SourceOnly => $"Plan applied successfully. Changed files: {execution.ChangedFiles.Count}.",
            OracleApexAssistantPostEditBehavior.ValidateOnly => $"Plan applied successfully and validation passed. Changed files: {execution.ChangedFiles.Count}.",
            OracleApexAssistantPostEditBehavior.ValidateAndImport when importResult?.ProcessResult?.IsSuccess == false => $"Plan applied and validated, but import failed.",
            OracleApexAssistantPostEditBehavior.ValidateAndImport => $"Plan applied successfully, validation passed, and import completed.",
            _ => execution.Summary,
        };
    }

    private OracleApexValidationResult NormalizeCompilerValidation(OracleApexValidationResult? validation, ProcessResult? processResult, OracleApexWorkspaceIndex index, OracleApexEditPlan plan)
    {
        if (validation is null)
        {
            return _validationFeedbackService.BuildValidationResult(processResult, index, plan);
        }

        return validation.Mappings.Count == 0
            ? _validationFeedbackService.MapValidationResult(validation, index, plan)
            : validation;
    }

    private bool ShouldAutoRepair(WorkspaceSnapshot snapshot, OracleApexAssistantRequest request, OracleApexEditPlan repairPlan)
    {
        var settings = _validationFeedbackService.ReadWorkspaceSettings(snapshot);
        return settings.SafeAutomaticRepairEnabled
            && request.EnableSafeAutomaticRepair
            && repairPlan.UnresolvedQuestions.Count == 0
            && repairPlan.Operations.Count > 0
            && repairPlan.Classification == OracleApexPlanClassification.Additive;
    }

    private static bool IsDevelopmentEnvironment(string environmentName)
        => string.Equals(environmentName, "dev", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "development", StringComparison.OrdinalIgnoreCase);

    private static WorkspaceSnapshot CloneWithSynchronization(WorkspaceSnapshot snapshot, WorkspaceSynchronizationSnapshot synchronization)
        => new()
        {
            Record = snapshot.Record,
            Definition = snapshot.Definition,
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
            Assistant = snapshot.Assistant,
            Health = snapshot.Health,
            Readiness = snapshot.Readiness,
            AvailableServices = snapshot.AvailableServices,
        };
}

public sealed class OracleApexAssistantRequest
{
    public string Prompt { get; init; } = string.Empty;
    public string EnvironmentName { get; init; } = string.Empty;
    public OracleApexAssistantPostEditBehavior PostEditBehavior { get; init; } = OracleApexAssistantPostEditBehavior.Auto;
    public bool ConfirmPlan { get; init; }
    public bool EnableSafeAutomaticRepair { get; init; }
    public bool AllowNonDevelopmentDeployment { get; init; }
}

public sealed class OracleApexAssistantPlanResponse
{
    public OracleApexAssistantRequest Request { get; init; } = new();
    public OracleApexEditPlan Plan { get; init; } = new();
    public string Review { get; init; } = string.Empty;
    public OracleApexPlanClassification Classification { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Assumptions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnresolvedQuestions { get; init; } = Array.Empty<string>();
    public bool ConfirmationRequired { get; init; }
    public OracleApexWorkspaceIndex WorkspaceIndex { get; init; } = new();
    public OracleApexAssistantPostEditBehavior PostEditBehavior { get; init; }
    public bool SafeToContinueDeployment { get; init; }
}

public sealed class OracleApexAssistantExecutionResponse
{
    public bool IsSuccess { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> ChangedFiles { get; init; } = Array.Empty<string>();
    public OracleApexSemanticEditDiagnostics Diagnostics { get; init; } = new();
    public OracleApexWorkspaceIndex WorkspaceIndex { get; init; } = new();
    public OracleApexAssistantPostEditBehavior PostEditBehavior { get; init; }
    public WorkspaceSynchronizationOperationResult? ValidationResult { get; init; }
    public OracleApexValidationResult? CompilerValidation { get; init; }
    public WorkspaceSynchronizationOperationResult? ImportResult { get; init; }
    public bool SafeToContinueDeployment { get; init; }
    public bool ConfirmationRequired { get; init; }
    public IReadOnlyList<string> UnresolvedQuestions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public OracleApexEditPlan? SuggestedRepairPlan { get; init; }
    public string RepairReview { get; init; } = string.Empty;
    public WorkspaceSynchronizationSnapshot? Synchronization { get; init; }
    public OracleApexAssistantStage Stage { get; init; }
    public string GitStatusSummary { get; init; } = string.Empty;
}

public sealed class OracleApexAssistantRepairPlanResponse
{
    public OracleApexEditPlan Plan { get; init; } = new();
    public string Review { get; init; } = string.Empty;
    public OracleApexValidationResult CompilerValidation { get; init; } = new();
}

public enum OracleApexAssistantStage
{
    SemanticGeneration,
    SemanticValidation,
    SqlclValidation,
    RepairPlanning,
    RepairExecution,
    Import,
    Preview,
}

public enum OracleApexAssistantPostEditBehavior
{
    Auto,
    SourceOnly,
    ValidateOnly,
    ValidateAndImport,
}
