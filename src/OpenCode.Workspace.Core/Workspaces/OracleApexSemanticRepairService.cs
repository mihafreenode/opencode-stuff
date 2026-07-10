using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class OracleApexSemanticRepairService
{
    private readonly OracleApexWorkspaceIndexBuilder _workspaceIndexBuilder;
    private readonly OracleApexCodeActionService _codeActionService;

    public OracleApexSemanticRepairService(OracleApexWorkspaceIndexBuilder? workspaceIndexBuilder = null, OracleApexCodeActionService? codeActionService = null)
    {
        _workspaceIndexBuilder = workspaceIndexBuilder ?? new OracleApexWorkspaceIndexBuilder();
        _codeActionService = codeActionService ?? new OracleApexCodeActionService(_workspaceIndexBuilder);
    }

    public OracleApexEditPlan CreateRepairPlan(string rootPath, OracleApexEnvironmentPreferences environment, string environmentName, OracleApexEditPlan sourcePlan, OracleApexValidationResult validation)
    {
        var index = _workspaceIndexBuilder.Build(rootPath, environment, environmentName);
        var availableActions = _codeActionService.GetAvailableActions(rootPath, environment, environmentName);
        var plan = new OracleApexEditPlan
        {
            Intent = $"Repair compiler diagnostics for {sourcePlan.Intent}",
            Summary = "Review semantic repair plan before applying compiler-driven fixes.",
            EnvironmentName = environmentName,
            SourcePath = environment.SourcePath ?? "src/apex",
        };

        foreach (var mapping in validation.Mappings)
        {
            switch (mapping.Diagnostic.Category)
            {
                case "missing-required-property":
                case "invalid-property-value":
                    AddPropertyRepair(index, sourcePlan, plan, mapping);
                    break;
                case "invalid-parent-child-placement":
                    AddParentPlacementRepair(availableActions, plan, mapping);
                    break;
                case "unresolved-component-reference":
                    AddReferenceRepair(index, plan, mapping);
                    break;
                case "invalid-page-target":
                    AddPageTargetRepair(index, plan, mapping);
                    break;
                case "duplicate-identifier":
                    AddDuplicateRepair(index, availableActions, plan, mapping);
                    break;
                case "malformed-generated-components":
                    plan.UnresolvedQuestions.Add($"Malformed generated component requires manual review: {mapping.Diagnostic.Message}");
                    break;
            }
        }

        plan.ExpectedChangedFiles = plan.Operations.SelectMany(operation => operation.ExpectedChangedFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        plan.AffectedSymbols = plan.Operations.SelectMany(operation => operation.AffectedSymbols).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        plan.EstimatedComplexity = plan.Operations.Count <= 2 ? "Low" : plan.Operations.Count <= 5 ? "Medium" : "High";
        plan.Classification = plan.UnresolvedQuestions.Count > 0 ? OracleApexPlanClassification.PotentiallyConflicting : OracleApexPlanClassification.Additive;
        plan.RequiresConfirmation = plan.Classification != OracleApexPlanClassification.Additive;
        return plan;
    }

    private static void AddPropertyRepair(OracleApexWorkspaceIndex index, OracleApexEditPlan sourcePlan, OracleApexEditPlan repairPlan, OracleApexDiagnosticMapping mapping)
    {
        var entry = index.Entries.FirstOrDefault(item => string.Equals(item.NodeId, mapping.SemanticNodeId, StringComparison.OrdinalIgnoreCase))
            ?? index.Entries.FirstOrDefault(item => string.Equals(item.Identifier, mapping.WorkspaceIdentifier, StringComparison.OrdinalIgnoreCase));
        if (entry is null || string.IsNullOrWhiteSpace(mapping.Diagnostic.Property))
        {
            repairPlan.UnresolvedQuestions.Add($"Repair needs a target property for diagnostic: {mapping.Diagnostic.Message}");
            return;
        }

        var inferredValue = InferPropertyValue(index, sourcePlan, mapping, entry);
        if (string.IsNullOrWhiteSpace(inferredValue))
        {
            repairPlan.UnresolvedQuestions.Add($"Provide a valid value for '{mapping.Diagnostic.Property}' on '{entry.Identifier}'.");
            return;
        }

        repairPlan.Operations.Add(new OracleApexPlannedOperation
        {
            Sequence = repairPlan.Operations.Count + 1,
            Title = $"Set {mapping.Diagnostic.Property} on '{entry.Identifier}'",
            ExecutionMode = OracleApexPlannedExecutionMode.SemanticEditor,
            SemanticOperations = [OracleApexSemanticEditOperation.UpdateProperties(entry.SemanticType, entry.Identifier, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [mapping.Diagnostic.Property] = inferredValue }, ResolveParentIdentifier(index, entry))],
            TargetComponentType = entry.SemanticType,
            TargetIdentifier = entry.Identifier,
            Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [mapping.Diagnostic.Property] = inferredValue },
            AffectedSymbols = [entry.Identifier],
            ExpectedChangedFiles = [entry.SourceFile],
        });
    }

    private static void AddParentPlacementRepair(IReadOnlyList<OracleApexCodeAction> availableActions, OracleApexEditPlan repairPlan, OracleApexDiagnosticMapping mapping)
    {
        var action = availableActions.FirstOrDefault(item => item.Kind == OracleApexCodeActionKind.FixInvalidParentPlacement && string.Equals(item.TargetNodeId, mapping.SemanticNodeId, StringComparison.OrdinalIgnoreCase));
        if (action is null)
        {
            repairPlan.UnresolvedQuestions.Add($"No semantic repair action was found for invalid parent placement on '{mapping.WorkspaceIdentifier}'.");
            return;
        }

        repairPlan.Operations.Add(new OracleApexPlannedOperation
        {
            Sequence = repairPlan.Operations.Count + 1,
            Title = action.Title,
            ExecutionMode = OracleApexPlannedExecutionMode.CodeAction,
            CodeActionId = action.Id,
            CodeActionRequest = new OracleApexCodeActionRequest { ActionId = action.Id },
            TargetComponentType = action.TargetSemanticType,
            TargetIdentifier = action.TargetIdentifier,
            AffectedSymbols = [action.TargetIdentifier],
            ExpectedChangedFiles = mapping.Diagnostic.FilePath.Length > 0 ? [mapping.Diagnostic.FilePath] : Array.Empty<string>(),
        });
    }

    private static void AddReferenceRepair(OracleApexWorkspaceIndex index, OracleApexEditPlan repairPlan, OracleApexDiagnosticMapping mapping)
    {
        var entry = index.Entries.FirstOrDefault(item => string.Equals(item.NodeId, mapping.SemanticNodeId, StringComparison.OrdinalIgnoreCase))
            ?? index.Entries.FirstOrDefault(item => string.Equals(item.Identifier, mapping.WorkspaceIdentifier, StringComparison.OrdinalIgnoreCase));
        if (entry is null || string.IsNullOrWhiteSpace(mapping.Diagnostic.Property))
        {
            repairPlan.UnresolvedQuestions.Add($"Reference repair needs a mapped semantic component for '{mapping.Diagnostic.Message}'.");
            return;
        }

        var semanticType = mapping.Diagnostic.Property switch
        {
            "authorization-scheme" => "authorization-scheme",
            "authentication-scheme" => "authentication-scheme",
            "lov" => "lov",
            "list" => "list",
            _ => string.Empty,
        };
        var candidate = string.IsNullOrWhiteSpace(semanticType)
            ? null
            : index.SharedComponents.Where(item => string.Equals(item.SemanticType, semanticType, StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.Identifier, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (candidate is null)
        {
            repairPlan.UnresolvedQuestions.Add($"Could not resolve replacement for '{mapping.Diagnostic.Property}' on '{entry.Identifier}'.");
            return;
        }

        repairPlan.Operations.Add(new OracleApexPlannedOperation
        {
            Sequence = repairPlan.Operations.Count + 1,
            Title = $"Reuse {semanticType} '{candidate.Identifier}' for '{entry.Identifier}'",
            ExecutionMode = OracleApexPlannedExecutionMode.SemanticEditor,
            SemanticOperations = [OracleApexSemanticEditOperation.UpdateProperties(entry.SemanticType, entry.Identifier, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [mapping.Diagnostic.Property] = candidate.Identifier }, ResolveParentIdentifier(index, entry))],
            TargetComponentType = entry.SemanticType,
            TargetIdentifier = entry.Identifier,
            Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [mapping.Diagnostic.Property] = candidate.Identifier },
            AffectedSymbols = [entry.Identifier, candidate.Identifier],
            ExpectedChangedFiles = [entry.SourceFile],
        });
    }

    private static void AddPageTargetRepair(OracleApexWorkspaceIndex index, OracleApexEditPlan repairPlan, OracleApexDiagnosticMapping mapping)
    {
        var entry = index.Entries.FirstOrDefault(item => string.Equals(item.NodeId, mapping.SemanticNodeId, StringComparison.OrdinalIgnoreCase))
            ?? index.Entries.FirstOrDefault(item => string.Equals(item.Identifier, mapping.WorkspaceIdentifier, StringComparison.OrdinalIgnoreCase));
        var targetPage = index.Pages.OrderBy(item => item.Identifier, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (entry is null || targetPage is null || !targetPage.Properties.TryGetValue("id", out var pageId))
        {
            repairPlan.UnresolvedQuestions.Add($"Could not infer a valid target page for '{mapping.WorkspaceIdentifier}'.");
            return;
        }

        repairPlan.Operations.Add(new OracleApexPlannedOperation
        {
            Sequence = repairPlan.Operations.Count + 1,
            Title = $"Set valid target page for '{entry.Identifier}'",
            ExecutionMode = OracleApexPlannedExecutionMode.SemanticEditor,
            SemanticOperations = [OracleApexSemanticEditOperation.UpdateProperties(entry.SemanticType, entry.Identifier, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["target-page"] = pageId }, ResolveParentIdentifier(index, entry))],
            TargetComponentType = entry.SemanticType,
            TargetIdentifier = entry.Identifier,
            Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["target-page"] = pageId },
            AffectedSymbols = [entry.Identifier, targetPage.Identifier],
            ExpectedChangedFiles = [entry.SourceFile],
        });
    }

    private static void AddDuplicateRepair(OracleApexWorkspaceIndex index, IReadOnlyList<OracleApexCodeAction> availableActions, OracleApexEditPlan repairPlan, OracleApexDiagnosticMapping mapping)
    {
        var entry = index.Entries.FirstOrDefault(item => string.Equals(item.NodeId, mapping.SemanticNodeId, StringComparison.OrdinalIgnoreCase))
            ?? index.Entries.FirstOrDefault(item => string.Equals(item.Identifier, mapping.WorkspaceIdentifier, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            repairPlan.UnresolvedQuestions.Add($"Could not locate duplicate component for '{mapping.Diagnostic.Message}'.");
            return;
        }

        var newIdentifier = NextIdentifier(index, entry.Identifier);
        var kind = entry.SemanticType switch
        {
            "page" => OracleApexCodeActionKind.RenamePage,
            "region" => OracleApexCodeActionKind.RenameRegion,
            "item" => OracleApexCodeActionKind.RenameItem,
            _ => OracleApexCodeActionKind.RenameSharedComponent,
        };
        var action = availableActions.FirstOrDefault(item => item.Kind == kind && string.Equals(item.TargetNodeId, entry.NodeId, StringComparison.OrdinalIgnoreCase));
        if (action is null)
        {
            repairPlan.UnresolvedQuestions.Add($"No rename action was found for duplicate component '{entry.Identifier}'.");
            return;
        }

        repairPlan.Operations.Add(new OracleApexPlannedOperation
        {
            Sequence = repairPlan.Operations.Count + 1,
            Title = $"Rename duplicate '{entry.Identifier}' to '{newIdentifier}'",
            ExecutionMode = OracleApexPlannedExecutionMode.CodeAction,
            CodeActionId = action.Id,
            CodeActionRequest = new OracleApexCodeActionRequest { ActionId = action.Id, NewIdentifier = newIdentifier },
            TargetComponentType = entry.SemanticType,
            TargetIdentifier = entry.Identifier,
            AffectedSymbols = [entry.Identifier, newIdentifier],
            ExpectedChangedFiles = [entry.SourceFile],
        });
    }

    private static string InferPropertyValue(OracleApexWorkspaceIndex index, OracleApexEditPlan sourcePlan, OracleApexDiagnosticMapping mapping, OracleApexWorkspaceIndexEntry entry)
    {
        var property = mapping.Diagnostic.Property;
        if (string.Equals(property, "name", StringComparison.OrdinalIgnoreCase) || string.Equals(property, "title", StringComparison.OrdinalIgnoreCase))
        {
            return entry.Identifier;
        }

        if (string.Equals(property, "alias", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(entry.Identifier.Trim().ToUpperInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '_')).Trim('_');
        }

        if (string.Equals(property, "id", StringComparison.OrdinalIgnoreCase) && string.Equals(entry.SemanticType, "page", StringComparison.OrdinalIgnoreCase))
        {
            var nextId = index.Pages.Select(page => page.Properties.TryGetValue("id", out var value) && int.TryParse(value, out var parsed) ? parsed : 0).DefaultIfEmpty(0).Max() + 1;
            return nextId.ToString();
        }

        if (string.Equals(property, "type", StringComparison.OrdinalIgnoreCase))
        {
            return entry.SemanticType switch
            {
                "region" => sourcePlan.Operations.FirstOrDefault(operation => operation.AffectedSymbols.Contains(entry.Identifier, StringComparer.OrdinalIgnoreCase))?.Properties.TryGetValue("type", out var value) == true ? value : "Interactive Report",
                "process" => "Validation",
                _ => string.Empty,
            };
        }

        if (string.Equals(property, "source-type", StringComparison.OrdinalIgnoreCase))
        {
            return "SQL Query";
        }

        return string.Empty;
    }

    private static string? ResolveParentIdentifier(OracleApexWorkspaceIndex index, OracleApexWorkspaceIndexEntry entry)
        => string.IsNullOrWhiteSpace(entry.ParentNodeId)
            ? null
            : index.Entries.FirstOrDefault(item => string.Equals(item.NodeId, entry.ParentNodeId, StringComparison.OrdinalIgnoreCase))?.Identifier;

    private static string NextIdentifier(OracleApexWorkspaceIndex index, string baseIdentifier)
    {
        var suffix = 2;
        var candidate = $"{baseIdentifier} {suffix}";
        while (index.Entries.Any(entry => string.Equals(entry.Identifier, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            suffix++;
            candidate = $"{baseIdentifier} {suffix}";
        }

        return candidate;
    }
}
