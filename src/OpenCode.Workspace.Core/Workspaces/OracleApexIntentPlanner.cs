using System.Text.RegularExpressions;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class OracleApexIntentPlanner
{
    private static readonly Regex CreatePagePattern = new(@"(?:add|create)\s+(?:a\s+)?(?<name>[A-Za-z][A-Za-z0-9 _-]+?)\s+page\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RemovePagePattern = new(@"remove\s+page\s+(?<name>[A-Za-z][A-Za-z0-9 _-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RenamePattern = new(@"rename\s+(?<type>page|region|item|shared component|lov|list|authorization scheme|authentication scheme|navigation menu)\s+(?<old>[A-Za-z][A-Za-z0-9 _-]+?)\s+to\s+(?<new>[A-Za-z][A-Za-z0-9 _-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AddNavigationEntryPattern = new(@"add\s+navigation\s+entry\s+(?<name>[A-Za-z][A-Za-z0-9 _-]+)(?:\s+to\s+(?<menu>[A-Za-z][A-Za-z0-9 _-]+))?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly OracleApexWorkspaceIndexBuilder _workspaceIndexBuilder;
    private readonly OracleApexComponentCatalog _componentCatalog;
    private readonly OracleApexCodeActionService _codeActionService;
    private readonly IOracleApexSemanticEditor _semanticEditor;

    public OracleApexIntentPlanner(
        OracleApexWorkspaceIndexBuilder? workspaceIndexBuilder = null,
        OracleApexComponentCatalog? componentCatalog = null,
        OracleApexCodeActionService? codeActionService = null,
        IOracleApexSemanticEditor? semanticEditor = null)
    {
        _workspaceIndexBuilder = workspaceIndexBuilder ?? new OracleApexWorkspaceIndexBuilder();
        _componentCatalog = componentCatalog ?? OracleApexComponentCatalog.Default;
        _semanticEditor = semanticEditor ?? new OracleApexSemanticEditor(_workspaceIndexBuilder, _componentCatalog);
        _codeActionService = codeActionService ?? new OracleApexCodeActionService(_workspaceIndexBuilder, _semanticEditor);
    }

    public OracleApexEditPlanResult CreatePlan(string rootPath, OracleApexEnvironmentPreferences environment, string environmentName, string intent)
    {
        var index = _workspaceIndexBuilder.Build(rootPath, environment, environmentName);
        var availableActions = _codeActionService.GetAvailableActions(rootPath, environment, environmentName);
        var plan = new OracleApexEditPlan
        {
            Intent = intent.Trim(),
            EnvironmentName = environmentName,
            SourcePath = environment.SourcePath ?? "src/apex",
        };

        BuildPlan(index, availableActions, intent, plan);
        plan.ExpectedChangedFiles = plan.Operations.SelectMany(operation => operation.ExpectedChangedFiles).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        plan.AffectedSymbols = plan.Operations.SelectMany(operation => operation.AffectedSymbols).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase).ToList();
        plan.Classification = Classify(plan);
        plan.RequiresConfirmation = plan.Classification != OracleApexPlanClassification.Additive;

        var validation = new OracleApexPlanValidationResult
        {
            IsValid = plan.UnresolvedQuestions.Count == 0,
            Warnings = plan.Warnings,
            UnresolvedQuestions = plan.UnresolvedQuestions,
        };

        return new OracleApexEditPlanResult { Plan = plan, Validation = validation };
    }

    public OracleApexEditPlanExecutionResult ExecutePlan(string rootPath, OracleApexEnvironmentPreferences environment, string environmentName, OracleApexEditPlan plan, bool confirmDestructive = false)
    {
        if (plan.RequiresConfirmation && !confirmDestructive)
        {
            return new OracleApexEditPlanExecutionResult
            {
                IsSuccess = false,
                Summary = "Plan requires explicit confirmation before destructive execution.",
                WorkspaceIndex = _workspaceIndexBuilder.Build(rootPath, environment, environmentName),
            };
        }

        var sourcePath = Path.Combine(rootPath, (environment.SourcePath ?? "src/apex").Replace('/', Path.DirectorySeparatorChar));
        var backups = SnapshotApexFiles(sourcePath);
        var changedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var latestIndex = _workspaceIndexBuilder.Build(rootPath, environment, environmentName);

        foreach (var operation in plan.Operations.OrderBy(operation => operation.Sequence))
        {
            OracleApexCodeActionResult? codeActionResult = null;
            OracleApexSemanticEditResult? semanticResult = null;

            if (operation.ExecutionMode == OracleApexPlannedExecutionMode.CodeAction)
            {
                codeActionResult = _codeActionService.Execute(rootPath, environment, environmentName, operation.CodeActionRequest!);
                if (!codeActionResult.IsSuccess)
                {
                    RestoreApexFiles(sourcePath, backups);
                    return new OracleApexEditPlanExecutionResult
                    {
                        IsSuccess = false,
                        Summary = codeActionResult.Summary,
                        ChangedFiles = Array.Empty<string>(),
                        Diagnostics = codeActionResult.Diagnostics,
                        WorkspaceIndex = _workspaceIndexBuilder.Build(rootPath, environment, environmentName),
                    };
                }

                latestIndex = codeActionResult.WorkspaceIndex;
                foreach (var file in codeActionResult.ChangedFiles)
                {
                    changedFiles.Add(file);
                }
            }
            else
            {
                semanticResult = _semanticEditor.Apply(rootPath, environment, environmentName, operation.SemanticOperations);
                if (!semanticResult.IsSuccess)
                {
                    RestoreApexFiles(sourcePath, backups);
                    return new OracleApexEditPlanExecutionResult
                    {
                        IsSuccess = false,
                        Summary = semanticResult.Message,
                        ChangedFiles = Array.Empty<string>(),
                        Diagnostics = semanticResult.Diagnostics,
                        WorkspaceIndex = _workspaceIndexBuilder.Build(rootPath, environment, environmentName),
                    };
                }

                latestIndex = semanticResult.WorkspaceIndex;
                foreach (var file in semanticResult.ChangedFiles)
                {
                    changedFiles.Add(file);
                }
            }
        }

        latestIndex = _workspaceIndexBuilder.Build(rootPath, environment, environmentName);
        return new OracleApexEditPlanExecutionResult
        {
            IsSuccess = true,
            Summary = $"Executed {plan.Operations.Count} planned Oracle APEX operation(s).",
            ChangedFiles = changedFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(),
            Diagnostics = new OracleApexSemanticEditDiagnostics { Entries = latestIndex.Diagnostics },
            WorkspaceIndex = latestIndex,
        };
    }

    private void BuildPlan(OracleApexWorkspaceIndex index, IReadOnlyList<OracleApexCodeAction> availableActions, string intent, OracleApexEditPlan plan)
    {
        var lowerIntent = intent.ToLowerInvariant();

        if (lowerIntent.Contains("fix semantic diagnostics", StringComparison.Ordinal))
        {
            AddDiagnosticFixes(index, availableActions, plan);
            return;
        }

        var renameMatch = RenamePattern.Match(intent);
        if (renameMatch.Success)
        {
            AddRenamePlan(index, availableActions, renameMatch, plan);
            return;
        }

        var removePageMatch = RemovePagePattern.Match(intent);
        if (removePageMatch.Success)
        {
            AddRemovePagePlan(index, availableActions, removePageMatch.Groups["name"].Value.Trim(), plan);
            return;
        }

        var navigationMatch = AddNavigationEntryPattern.Match(intent);
        if (navigationMatch.Success && !lowerIntent.Contains("page", StringComparison.Ordinal))
        {
            AddNavigationEntryPlan(index, availableActions, navigationMatch.Groups["name"].Value.Trim(), navigationMatch.Groups["menu"].Value.Trim(), plan, inferTargetPageId: null);
            return;
        }

        var pageMatch = CreatePagePattern.Match(intent);
        if (pageMatch.Success)
        {
            AddCreatePagePlan(index, availableActions, pageMatch.Groups["name"].Value.Trim(), lowerIntent, plan);
            return;
        }

        if (lowerIntent.Contains("shared lov", StringComparison.Ordinal) || lowerIntent.Contains("list of values", StringComparison.Ordinal))
        {
            AddCreateSharedLovPlan(index, intent, plan);
            return;
        }

        plan.UnresolvedQuestions.Add("Planner could not map the request to a supported deterministic APEXlang intent.");
    }

    private void AddCreatePagePlan(OracleApexWorkspaceIndex index, IReadOnlyList<OracleApexCodeAction> availableActions, string pageBaseName, string lowerIntent, OracleApexEditPlan plan)
    {
        var nextPageId = NextPageId(index);
        var pageAlias = Slug(pageBaseName).ToUpperInvariant();
        AddSemanticPlan(plan, $"Create page '{pageBaseName}'", new[] { pageBaseName }, [$"pages/p{nextPageId:D5}-{Slug(pageAlias)}.apx"], OracleApexSemanticEditOperation.AddPage(pageBaseName, new Dictionary<string, string> { ["id"] = nextPageId.ToString(), ["alias"] = pageAlias, ["name"] = pageBaseName }) );

        var targetPageName = pageBaseName;
        if (lowerIntent.Contains("interactive report", StringComparison.Ordinal) || lowerIntent.Contains("report region", StringComparison.Ordinal))
        {
            var action = availableActions.FirstOrDefault(item => item.Kind == OracleApexCodeActionKind.AddRegionToPage && string.Equals(item.TargetIdentifier, targetPageName, StringComparison.OrdinalIgnoreCase));
            if (action is null)
            {
                AddSemanticPlan(plan, $"Add interactive report region to '{targetPageName}'", [targetPageName, $"{pageBaseName} Report"], [$"pages/p{nextPageId:D5}-{Slug(pageAlias)}.apx"], new OracleApexSemanticEditOperation { Kind = OracleApexSemanticEditKind.AddRegion, ParentIdentifier = targetPageName, ParentSemanticType = "page", NewIdentifier = $"{pageBaseName} Report", Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["type"] = "Interactive Report", ["source-type"] = "SQL Query" } });
            }
            else
            {
                AddCodeActionPlan(plan, action, new OracleApexCodeActionRequest { ActionId = action.Id, NewIdentifier = $"{pageBaseName} Report", Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["type"] = "Interactive Report", ["source-type"] = "SQL Query" } }, [targetPageName, $"{pageBaseName} Report"], [$"pages/p{nextPageId:D5}-{Slug(pageAlias)}.apx"], ["Creates an interactive report region with default SQL query source type."]);
            }
        }

        var formPageName = string.Empty;
        var formPageId = 0;
        if (lowerIntent.Contains("form page", StringComparison.Ordinal))
        {
            formPageName = pageBaseName.EndsWith(" Form", StringComparison.OrdinalIgnoreCase) ? pageBaseName : $"{pageBaseName} Form";
            formPageId = nextPageId + 1;
            var formAlias = Slug(formPageName).ToUpperInvariant();
            AddSemanticPlan(plan, $"Create form page '{formPageName}'", [formPageName], [$"pages/p{formPageId:D5}-{Slug(formAlias)}.apx"], OracleApexSemanticEditOperation.AddPage(formPageName, new Dictionary<string, string> { ["id"] = formPageId.ToString(), ["alias"] = formAlias, ["name"] = formPageName }) );
            AddSemanticPlan(plan, $"Add form region to '{formPageName}'", [formPageName, $"{pageBaseName} Form Region"], [$"pages/p{formPageId:D5}-{Slug(formAlias)}.apx"], new OracleApexSemanticEditOperation { Kind = OracleApexSemanticEditKind.AddRegion, ParentIdentifier = formPageName, ParentSemanticType = "page", NewIdentifier = $"{pageBaseName} Form Region", Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["type"] = "Form" } });
        }

        if (lowerIntent.Contains("navigation entr", StringComparison.Ordinal))
        {
            AddNavigationEntryPlan(index, availableActions, pageBaseName, string.Empty, plan, nextPageId);
            if (!string.IsNullOrWhiteSpace(formPageName))
            {
                AddNavigationEntryPlan(index, availableActions, formPageName, string.Empty, plan, formPageId);
            }
        }

        if (lowerIntent.Contains("validation", StringComparison.Ordinal))
        {
            var validationTargetPage = string.IsNullOrWhiteSpace(formPageName) ? pageBaseName : formPageName;
            var validationTargetFile = string.IsNullOrWhiteSpace(formPageName) ? $"pages/p{nextPageId:D5}-{Slug(pageAlias)}.apx" : $"pages/p{formPageId:D5}-{Slug(Slug(formPageName).ToUpperInvariant())}.apx";
            AddSemanticPlan(plan, $"Add validation process to '{validationTargetPage}'", [validationTargetPage, $"Validate {pageBaseName}"], [validationTargetFile], OracleApexSemanticEditOperation.AddProcess(validationTargetPage, $"Validate {pageBaseName}", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["type"] = "Validation" }));
        }
    }

    private void AddCreateSharedLovPlan(OracleApexWorkspaceIndex index, string intent, OracleApexEditPlan plan)
    {
        var name = ExtractTrailingName(intent, "lov") ?? ExtractTrailingName(intent, "values") ?? "NEW_LOV";
        AddSemanticPlan(plan, $"Create shared LOV '{name}'", [name], [$"shared_components/lovs/{Slug(name)}.apx"], OracleApexSemanticEditOperation.AddSharedComponent("lov", name, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["name"] = name }));
    }

    private void AddNavigationEntryPlan(OracleApexWorkspaceIndex index, IReadOnlyList<OracleApexCodeAction> availableActions, string entryName, string menuName, OracleApexEditPlan plan, int? inferTargetPageId)
    {
        OracleApexWorkspaceIndexEntry? menu;
        if (!string.IsNullOrWhiteSpace(menuName))
        {
            menu = index.SharedComponents.FirstOrDefault(item => item.SemanticType == "navigation-menu" && string.Equals(item.Identifier, menuName, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            var menus = index.SharedComponents.Where(item => item.SemanticType == "navigation-menu").ToList();
            menu = menus.Count == 1 ? menus[0] : null;
            if (menu is null)
            {
                plan.UnresolvedQuestions.Add("Navigation entry target menu is ambiguous. Specify which navigation menu should receive the new entry.");
                return;
            }

            plan.Assumptions.Add($"Using navigation menu '{menu.Identifier}' because it is the only discovered menu.");
        }

        var action = availableActions.FirstOrDefault(item => item.Kind == OracleApexCodeActionKind.AddNavigationEntry && string.Equals(item.TargetIdentifier, menu.Identifier, StringComparison.OrdinalIgnoreCase));
        var properties = inferTargetPageId.HasValue ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["target-page"] = inferTargetPageId.Value.ToString() } : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (action is not null)
        {
            AddCodeActionPlan(plan, action, new OracleApexCodeActionRequest { ActionId = action.Id, NewIdentifier = entryName, Properties = properties }, [menu.Identifier, entryName], [menu.SourceFile], Array.Empty<string>());
            if (inferTargetPageId.HasValue)
            {
                plan.Assumptions.Add($"Navigation entry '{entryName}' will target page id {inferTargetPageId.Value}.");
            }
        }
    }

    private void AddRenamePlan(OracleApexWorkspaceIndex index, IReadOnlyList<OracleApexCodeAction> availableActions, Match match, OracleApexEditPlan plan)
    {
        var type = match.Groups["type"].Value.Trim().ToLowerInvariant();
        var oldName = match.Groups["old"].Value.Trim();
        var newName = match.Groups["new"].Value.Trim();
        var kind = type switch
        {
            "page" => OracleApexCodeActionKind.RenamePage,
            "region" => OracleApexCodeActionKind.RenameRegion,
            "item" => OracleApexCodeActionKind.RenameItem,
            _ => OracleApexCodeActionKind.RenameSharedComponent,
        };
        var action = availableActions.FirstOrDefault(item => item.Kind == kind && string.Equals(item.TargetIdentifier, oldName, StringComparison.OrdinalIgnoreCase));
        if (action is null)
        {
            plan.UnresolvedQuestions.Add($"Could not resolve component '{oldName}' for rename.");
            return;
        }

        var target = index.Entries.FirstOrDefault(entry => string.Equals(entry.NodeId, action.TargetNodeId, StringComparison.OrdinalIgnoreCase));
        AddCodeActionPlan(plan, action, new OracleApexCodeActionRequest { ActionId = action.Id, NewIdentifier = newName }, [oldName, newName], [target?.SourceFile ?? string.Empty], Array.Empty<string>());
    }

    private void AddRemovePagePlan(OracleApexWorkspaceIndex index, IReadOnlyList<OracleApexCodeAction> availableActions, string pageName, OracleApexEditPlan plan)
    {
        var action = availableActions.FirstOrDefault(item => item.Kind == OracleApexCodeActionKind.RemovePageSafely && string.Equals(item.TargetIdentifier, pageName, StringComparison.OrdinalIgnoreCase));
        if (action is null)
        {
            plan.UnresolvedQuestions.Add($"Could not resolve page '{pageName}' for safe removal.");
            return;
        }

        var target = index.Pages.First(entry => string.Equals(entry.Identifier, pageName, StringComparison.OrdinalIgnoreCase));
        AddCodeActionPlan(plan, action, new OracleApexCodeActionRequest { ActionId = action.Id }, [pageName], [target.SourceFile], ["This plan removes a page and any direct page references cleaned by the semantic editor."]);
    }

    private void AddDiagnosticFixes(OracleApexWorkspaceIndex index, IReadOnlyList<OracleApexCodeAction> availableActions, OracleApexEditPlan plan)
    {
        foreach (var action in availableActions.Where(item => item.Kind is OracleApexCodeActionKind.FixMissingRequiredProperties or OracleApexCodeActionKind.FixInvalidParentPlacement))
        {
            if (action.Kind == OracleApexCodeActionKind.FixMissingRequiredProperties)
            {
                plan.UnresolvedQuestions.Add($"Provide a value for required property '{action.RequiredPropertyName}' on '{action.TargetSemanticType}'.");
                continue;
            }

            AddCodeActionPlan(plan, action, new OracleApexCodeActionRequest { ActionId = action.Id }, [action.TargetIdentifier], [ResolveSourceFile(index, action.TargetNodeId)], ["This action repairs invalid parent placement using semantic refactoring."]);
        }
    }

    private static OracleApexPlanClassification Classify(OracleApexEditPlan plan)
        => plan.Operations.Any(operation => operation.IsDestructive)
            ? OracleApexPlanClassification.Destructive
            : plan.UnresolvedQuestions.Count > 0 || plan.Warnings.Count > 0
                ? OracleApexPlanClassification.PotentiallyConflicting
                : OracleApexPlanClassification.Additive;

    private static void AddSemanticPlan(OracleApexEditPlan plan, string title, IReadOnlyList<string> affectedSymbols, IReadOnlyList<string> expectedFiles, params OracleApexSemanticEditOperation[] operations)
    {
        plan.Operations.Add(new OracleApexPlannedOperation
        {
            Sequence = plan.Operations.Count + 1,
            Title = title,
            ExecutionMode = OracleApexPlannedExecutionMode.SemanticEditor,
            SemanticOperations = operations.ToList(),
            TargetComponentType = InferTargetType(operations),
            TargetIdentifier = InferTargetIdentifier(operations),
            Properties = operations.SelectMany(operation => operation.Properties).GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase),
            AffectedSymbols = affectedSymbols.ToList(),
            ExpectedChangedFiles = expectedFiles.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            References = Array.Empty<string>(),
            Warnings = Array.Empty<string>(),
            IsDestructive = operations.Any(operation => operation.Kind is OracleApexSemanticEditKind.RemovePage or OracleApexSemanticEditKind.RemoveRegion or OracleApexSemanticEditKind.RemoveItem),
        });
    }

    private static void AddCodeActionPlan(OracleApexEditPlan plan, OracleApexCodeAction action, OracleApexCodeActionRequest request, IReadOnlyList<string> affectedSymbols, IReadOnlyList<string> expectedFiles, IReadOnlyList<string> warnings)
    {
        plan.Operations.Add(new OracleApexPlannedOperation
        {
            Sequence = plan.Operations.Count + 1,
            Title = action.Title,
            ExecutionMode = OracleApexPlannedExecutionMode.CodeAction,
            CodeActionId = action.Id,
            CodeActionRequest = request,
            TargetComponentType = action.TargetSemanticType,
            TargetIdentifier = action.TargetIdentifier,
            Properties = request.Properties,
            AffectedSymbols = affectedSymbols.ToList(),
            ExpectedChangedFiles = expectedFiles.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            References = request.Properties.Where(pair => pair.Key is "target-page" or "parent-entry").Select(pair => $"{pair.Key}:{pair.Value}").ToList(),
            Warnings = warnings,
            IsDestructive = action.Kind is OracleApexCodeActionKind.RemovePageSafely or OracleApexCodeActionKind.RemoveRegionSafely,
        });

        foreach (var warning in warnings)
        {
            plan.Warnings.Add(warning);
        }
    }

    private static Dictionary<string, string?> SnapshotApexFiles(string sourcePath)
        => Directory.Exists(sourcePath)
            ? Directory.GetFiles(sourcePath, "*.apx", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(sourcePath, path), path => (string?)File.ReadAllText(path), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    private static void RestoreApexFiles(string sourcePath, IReadOnlyDictionary<string, string?> backups)
    {
        if (Directory.Exists(sourcePath))
        {
            foreach (var path in Directory.GetFiles(sourcePath, "*.apx", SearchOption.AllDirectories))
            {
                File.Delete(path);
            }
        }

        foreach (var backup in backups)
        {
            var fullPath = Path.Combine(sourcePath, backup.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, backup.Value!.Replace("\r\n", "\n", StringComparison.Ordinal));
        }
    }

    private static string ResolveSourceFile(OracleApexWorkspaceIndex index, string nodeId)
        => index.Entries.FirstOrDefault(entry => string.Equals(entry.NodeId, nodeId, StringComparison.OrdinalIgnoreCase))?.SourceFile ?? string.Empty;

    private static string? ExtractTrailingName(string intent, string anchor)
    {
        var index = intent.LastIndexOf(anchor, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var tail = intent[(index + anchor.Length)..].Trim().Trim('.', ',', ' ');
        return string.IsNullOrWhiteSpace(tail) ? null : tail;
    }

    private static int NextPageId(OracleApexWorkspaceIndex index)
        => index.Pages.Select(page => page.Properties.TryGetValue("id", out var value) && int.TryParse(value, out var parsed) ? parsed : 0).DefaultIfEmpty(0).Max() + 1;

    private static string Slug(string value)
        => string.Concat(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-')).Trim('-');

    private static string InferTargetType(IReadOnlyList<OracleApexSemanticEditOperation> operations)
        => operations.LastOrDefault()?.ComponentType
            ?? operations.LastOrDefault()?.ParentSemanticType
            ?? operations.LastOrDefault()?.Kind.ToString()
            ?? string.Empty;

    private static string InferTargetIdentifier(IReadOnlyList<OracleApexSemanticEditOperation> operations)
        => operations.LastOrDefault()?.NewIdentifier
            ?? operations.LastOrDefault()?.TargetIdentifier
            ?? string.Empty;
}

public sealed class OracleApexEditPlan
{
    public string Intent { get; init; } = string.Empty;
    public string EnvironmentName { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public List<OracleApexPlannedOperation> Operations { get; } = [];
    public List<string> Assumptions { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> UnresolvedQuestions { get; } = [];
    public IReadOnlyList<string> ExpectedChangedFiles { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> AffectedSymbols { get; set; } = Array.Empty<string>();
    public OracleApexPlanClassification Classification { get; set; }
    public bool RequiresConfirmation { get; set; }
}

public sealed class OracleApexPlannedOperation
{
    public int Sequence { get; init; }
    public string Title { get; init; } = string.Empty;
    public OracleApexPlannedExecutionMode ExecutionMode { get; init; }
    public string CodeActionId { get; init; } = string.Empty;
    public OracleApexCodeActionRequest? CodeActionRequest { get; init; }
    public IReadOnlyList<OracleApexSemanticEditOperation> SemanticOperations { get; init; } = Array.Empty<OracleApexSemanticEditOperation>();
    public string TargetComponentType { get; init; } = string.Empty;
    public string TargetIdentifier { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> References { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AffectedSymbols { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExpectedChangedFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool IsDestructive { get; init; }
}

public enum OracleApexPlannedExecutionMode
{
    CodeAction,
    SemanticEditor,
}

public sealed class OracleApexEditPlanResult
{
    public OracleApexEditPlan Plan { get; init; } = new();
    public OracleApexPlanValidationResult Validation { get; init; } = new();
}

public sealed class OracleApexPlanValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnresolvedQuestions { get; init; } = Array.Empty<string>();
}

public sealed class OracleApexEditPlanExecutionResult
{
    public bool IsSuccess { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> ChangedFiles { get; init; } = Array.Empty<string>();
    public OracleApexSemanticEditDiagnostics Diagnostics { get; init; } = new();
    public OracleApexWorkspaceIndex WorkspaceIndex { get; init; } = new();
}

public enum OracleApexPlanClassification
{
    Additive,
    Destructive,
    PotentiallyConflicting,
}
