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

    public OracleApexAssistantService(
        IOracleApexAssistantSynchronizationService synchronizationService,
        OracleApexIntentPlanner? intentPlanner = null,
        OracleApexWorkspaceIndexBuilder? workspaceIndexBuilder = null,
        OracleApexCodeActionService? codeActionService = null,
        IOracleApexSemanticEditor? semanticEditor = null,
        OracleApexComponentCatalog? componentCatalog = null)
    {
        _componentCatalog = componentCatalog ?? OracleApexComponentCatalog.Default;
        _workspaceIndexBuilder = workspaceIndexBuilder ?? new OracleApexWorkspaceIndexBuilder();
        _semanticEditor = semanticEditor ?? new OracleApexSemanticEditor(_workspaceIndexBuilder, _componentCatalog);
        _codeActionService = codeActionService ?? new OracleApexCodeActionService(_workspaceIndexBuilder, _semanticEditor);
        _intentPlanner = intentPlanner ?? new OracleApexIntentPlanner(_workspaceIndexBuilder, _componentCatalog, _codeActionService, _semanticEditor);
        _synchronizationService = synchronizationService;
    }

    public OracleApexAssistantPlanResponse CreatePlan(WorkspaceSnapshot snapshot, OracleApexAssistantRequest request)
    {
        var environment = ResolveEnvironment(snapshot, request.EnvironmentName);
        var environmentName = request.EnvironmentName ?? snapshot.Synchronization.DefaultEnvironment?.EnvironmentName ?? snapshot.Definition.Oracle.Apex.DefaultEnvironment ?? "dev";
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
        var environmentName = request.EnvironmentName ?? snapshot.Synchronization.DefaultEnvironment?.EnvironmentName ?? snapshot.Definition.Oracle.Apex.DefaultEnvironment ?? "dev";
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
        var deploymentSafe = true;

        if (postEditBehavior != OracleApexAssistantPostEditBehavior.SourceOnly)
        {
            validationResult = await _synchronizationService.ValidateAsync(snapshot, environmentName, cancellationToken).ConfigureAwait(false);
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
                    SafeToContinueDeployment = false,
                };
            }
        }

        if (postEditBehavior == OracleApexAssistantPostEditBehavior.ValidateAndImport)
        {
            var syncState = snapshot.Synchronization.DefaultEnvironment?.State ?? WorkspaceSynchronizationState.Unknown;
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
                    SafeToContinueDeployment = false,
                    Warnings = [$"Synchronization state '{syncState}' blocks automatic import."],
                };
            }

            importResult = await _synchronizationService.ImportAsync(snapshot, environmentName, cancellationToken).ConfigureAwait(false);
            deploymentSafe = importResult.ProcessResult?.IsSuccess != false;
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
            ImportResult = importResult,
            SafeToContinueDeployment = deploymentSafe,
        };
    }

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
            $"Summary: {plan.Intent}",
            $"Classification: {plan.Classification}",
            $"Confirmation required: {(plan.RequiresConfirmation ? "Yes" : "No")}",
            "Operations:",
        };

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
}

public sealed class OracleApexAssistantRequest
{
    public string Prompt { get; init; } = string.Empty;
    public string EnvironmentName { get; init; } = string.Empty;
    public OracleApexAssistantPostEditBehavior PostEditBehavior { get; init; } = OracleApexAssistantPostEditBehavior.Auto;
    public bool ConfirmPlan { get; init; }
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
    public WorkspaceSynchronizationOperationResult? ImportResult { get; init; }
    public bool SafeToContinueDeployment { get; init; }
    public bool ConfirmationRequired { get; init; }
    public IReadOnlyList<string> UnresolvedQuestions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public enum OracleApexAssistantPostEditBehavior
{
    Auto,
    SourceOnly,
    ValidateOnly,
    ValidateAndImport,
}
