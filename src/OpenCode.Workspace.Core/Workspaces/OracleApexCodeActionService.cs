using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class OracleApexCodeActionService
{
    private readonly OracleApexWorkspaceIndexBuilder _workspaceIndexBuilder;
    private readonly IOracleApexSemanticEditor _semanticEditor;

    public OracleApexCodeActionService(OracleApexWorkspaceIndexBuilder? workspaceIndexBuilder = null, IOracleApexSemanticEditor? semanticEditor = null)
    {
        _workspaceIndexBuilder = workspaceIndexBuilder ?? new OracleApexWorkspaceIndexBuilder();
        _semanticEditor = semanticEditor ?? new OracleApexSemanticEditor(_workspaceIndexBuilder);
    }

    public IReadOnlyList<OracleApexCodeAction> GetAvailableActions(string rootPath, OracleApexEnvironmentPreferences environment, string environmentName)
    {
        var index = _workspaceIndexBuilder.Build(rootPath, environment, environmentName);
        var actions = new List<OracleApexCodeAction>();

        actions.AddRange(index.Pages.Select(page => BuildRenameAction(OracleApexCodeActionKind.RenamePage, "Rename page", page)));
        actions.AddRange(index.Regions.Select(region => BuildRenameAction(OracleApexCodeActionKind.RenameRegion, "Rename region", region)));
        actions.AddRange(index.Items.Select(item => BuildRenameAction(OracleApexCodeActionKind.RenameItem, "Rename item", item)));
        actions.AddRange(index.SharedComponents.Where(component => component.SemanticType != "navigation-entry").Select(component => BuildRenameAction(OracleApexCodeActionKind.RenameSharedComponent, "Rename shared component", component)));
        actions.AddRange(index.Pages.Select(page => new OracleApexCodeAction { Id = $"add-region:{page.NodeId}", Kind = OracleApexCodeActionKind.AddRegionToPage, Title = $"Add region to page '{page.Identifier}'", TargetNodeId = page.NodeId, TargetIdentifier = page.Identifier, TargetSemanticType = page.SemanticType }));
        actions.AddRange(index.Regions.Select(region => new OracleApexCodeAction { Id = $"add-item:{region.NodeId}", Kind = OracleApexCodeActionKind.AddItemToRegion, Title = $"Add item to region '{region.Identifier}'", TargetNodeId = region.NodeId, TargetIdentifier = region.Identifier, TargetSemanticType = region.SemanticType, ParentNodeId = region.ParentNodeId }));
        actions.AddRange(index.SharedComponents.Where(component => component.SemanticType == "navigation-menu").Select(menu => new OracleApexCodeAction { Id = $"add-navigation-entry:{menu.NodeId}", Kind = OracleApexCodeActionKind.AddNavigationEntry, Title = $"Add navigation entry to '{menu.Identifier}'", TargetNodeId = menu.NodeId, TargetIdentifier = menu.Identifier, TargetSemanticType = menu.SemanticType }));
        actions.AddRange(index.Pages.Select(page => new OracleApexCodeAction { Id = $"remove-page:{page.NodeId}", Kind = OracleApexCodeActionKind.RemovePageSafely, Title = $"Remove page '{page.Identifier}' safely", TargetNodeId = page.NodeId, TargetIdentifier = page.Identifier, TargetSemanticType = page.SemanticType }));
        actions.AddRange(index.Regions.Select(region => new OracleApexCodeAction { Id = $"remove-region:{region.NodeId}", Kind = OracleApexCodeActionKind.RemoveRegionSafely, Title = $"Remove region '{region.Identifier}' safely", TargetNodeId = region.NodeId, TargetIdentifier = region.Identifier, TargetSemanticType = region.SemanticType, ParentNodeId = region.ParentNodeId }));

        foreach (var diagnostic in index.Diagnostics.Where(item => item.Code == "missing-required-property"))
        {
            var property = ExtractQuotedValue(diagnostic.Message);
            if (string.IsNullOrWhiteSpace(property))
            {
                continue;
            }

            actions.Add(new OracleApexCodeAction
            {
                Id = $"fix-required:{diagnostic.NodeId}:{property}",
                Kind = OracleApexCodeActionKind.FixMissingRequiredProperties,
                Title = $"Fix missing required property '{property}' for {diagnostic.SemanticType}",
                TargetNodeId = diagnostic.NodeId,
                TargetSemanticType = diagnostic.SemanticType,
                RequiredPropertyName = property,
            });
        }

        foreach (var diagnostic in index.Diagnostics.Where(item => item.Code == "invalid-child-component"))
        {
            var entry = index.Entries.FirstOrDefault(item => string.Equals(item.NodeId, diagnostic.NodeId, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                continue;
            }

            var pageAncestor = FindAncestor(index, entry, "page");
            if (pageAncestor is null)
            {
                continue;
            }

            if (entry.SemanticType is not ("region" or "item"))
            {
                continue;
            }

            actions.Add(new OracleApexCodeAction
            {
                Id = $"fix-parent:{entry.NodeId}",
                Kind = OracleApexCodeActionKind.FixInvalidParentPlacement,
                Title = $"Move {entry.SemanticType} '{entry.Identifier}' under page '{pageAncestor.Identifier}'",
                TargetNodeId = entry.NodeId,
                TargetIdentifier = entry.Identifier,
                TargetSemanticType = entry.SemanticType,
                ParentNodeId = entry.ParentNodeId,
                DestinationNodeId = pageAncestor.NodeId,
            });
        }

        foreach (var diagnostic in index.Diagnostics.Where(item => item.Code.StartsWith("reference-", StringComparison.OrdinalIgnoreCase) && item.Code != "reference-property-became-required"))
        {
            actions.Add(new OracleApexCodeAction
            {
                Id = $"review-migration:{diagnostic.Code}:{diagnostic.NodeId}:{diagnostic.Line}:{diagnostic.Column}",
                Kind = OracleApexCodeActionKind.ReviewVersionMigrationImpact,
                Title = $"Review APEXlang migration impact: {diagnostic.Message}",
                TargetNodeId = diagnostic.NodeId,
                TargetSemanticType = diagnostic.SemanticType,
                ReviewMessage = diagnostic.Message,
            });
        }

        return actions.OrderBy(action => action.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public OracleApexCodeActionResult Execute(string rootPath, OracleApexEnvironmentPreferences environment, string environmentName, OracleApexCodeActionRequest request)
    {
        var index = _workspaceIndexBuilder.Build(rootPath, environment, environmentName);
        var action = GetAvailableActions(rootPath, environment, environmentName).FirstOrDefault(item => string.Equals(item.Id, request.ActionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Oracle APEX code action '{request.ActionId}' was not found.");

        if (action.Kind == OracleApexCodeActionKind.ReviewVersionMigrationImpact)
        {
            return new OracleApexCodeActionResult
            {
                IsSuccess = true,
                Summary = action.ReviewMessage,
                ChangedFiles = [],
                Diagnostics = new OracleApexSemanticEditDiagnostics { Entries = index.Diagnostics },
                WorkspaceIndex = index,
            };
        }

        var operations = BuildOperations(action, request, index);
        var result = _semanticEditor.Apply(rootPath, environment, environmentName, operations);
        return new OracleApexCodeActionResult
        {
            IsSuccess = result.IsSuccess,
            Summary = BuildSummary(action, request, result),
            ChangedFiles = result.ChangedFiles,
            Diagnostics = result.Diagnostics,
            WorkspaceIndex = result.WorkspaceIndex,
        };
    }

    private static OracleApexCodeAction BuildRenameAction(OracleApexCodeActionKind kind, string verb, OracleApexWorkspaceIndexEntry entry)
        => new()
        {
            Id = $"{kind}:{entry.NodeId}",
            Kind = kind,
            Title = $"{verb} '{entry.Identifier}'",
            TargetNodeId = entry.NodeId,
            TargetIdentifier = entry.Identifier,
            TargetSemanticType = entry.SemanticType,
            ParentNodeId = entry.ParentNodeId,
        };

    private static IReadOnlyList<OracleApexSemanticEditOperation> BuildOperations(OracleApexCodeAction action, OracleApexCodeActionRequest request, OracleApexWorkspaceIndex index)
    {
        var target = index.Entries.FirstOrDefault(entry => string.Equals(entry.NodeId, action.TargetNodeId, StringComparison.OrdinalIgnoreCase));
        return action.Kind switch
        {
            OracleApexCodeActionKind.RenamePage => [OracleApexSemanticEditOperation.RenamePage(action.TargetIdentifier, RequireNewIdentifier(request))],
            OracleApexCodeActionKind.RenameRegion => [OracleApexSemanticEditOperation.RenameRegion(RequireParentIdentifier(index, target), action.TargetIdentifier, RequireNewIdentifier(request))],
            OracleApexCodeActionKind.RenameItem => [OracleApexSemanticEditOperation.RenameItem(RequireParentIdentifier(index, target), action.TargetIdentifier, RequireNewIdentifier(request))],
            OracleApexCodeActionKind.RenameSharedComponent => [OracleApexSemanticEditOperation.RenameSharedComponent(action.TargetSemanticType, action.TargetIdentifier, RequireNewIdentifier(request))],
            OracleApexCodeActionKind.AddRegionToPage => [OracleApexSemanticEditOperation.AddRegion(action.TargetIdentifier, RequireNewIdentifier(request), request.Properties)],
            OracleApexCodeActionKind.AddItemToRegion => [new OracleApexSemanticEditOperation { Kind = OracleApexSemanticEditKind.AddItem, ParentIdentifier = action.TargetIdentifier, ParentSemanticType = "region", NewIdentifier = RequireNewIdentifier(request), Properties = request.Properties }],
            OracleApexCodeActionKind.AddNavigationEntry => [OracleApexSemanticEditOperation.AddNavigationEntry(action.TargetIdentifier, RequireNewIdentifier(request), request.Properties)],
            OracleApexCodeActionKind.RemovePageSafely => [OracleApexSemanticEditOperation.RemovePage(action.TargetIdentifier)],
            OracleApexCodeActionKind.RemoveRegionSafely => [OracleApexSemanticEditOperation.RemoveRegion(RequireParentIdentifier(index, target), action.TargetIdentifier)],
            OracleApexCodeActionKind.FixMissingRequiredProperties => [OracleApexSemanticEditOperation.UpdateProperties(action.TargetSemanticType, target?.Identifier ?? action.TargetIdentifier, RequireSingleProperty(request, action.RequiredPropertyName), GetParentIdentifier(index, target))],
            OracleApexCodeActionKind.FixInvalidParentPlacement => BuildFixInvalidParentOperations(index, action, target),
            OracleApexCodeActionKind.ReviewVersionMigrationImpact => Array.Empty<OracleApexSemanticEditOperation>(),
            _ => throw new InvalidOperationException($"Oracle APEX code action '{action.Kind}' is not supported."),
        };
    }

    private static IReadOnlyList<OracleApexSemanticEditOperation> BuildFixInvalidParentOperations(OracleApexWorkspaceIndex index, OracleApexCodeAction action, OracleApexWorkspaceIndexEntry? target)
    {
        if (target is null)
        {
            throw new InvalidOperationException("Target component for invalid parent placement fix was not found.");
        }

        var sourceParentIdentifier = GetParentIdentifier(index, target) ?? string.Empty;
        var destination = index.Entries.First(entry => string.Equals(entry.NodeId, action.DestinationNodeId, StringComparison.OrdinalIgnoreCase));
        return target.SemanticType switch
        {
            "region" => [OracleApexSemanticEditOperation.MoveRegion(sourceParentIdentifier, target.Identifier, destination.Identifier)],
            "item" =>
            [
                OracleApexSemanticEditOperation.RemoveItem(sourceParentIdentifier, target.Identifier),
                new OracleApexSemanticEditOperation { Kind = OracleApexSemanticEditKind.AddItem, ParentIdentifier = destination.Identifier, ParentSemanticType = "page", NewIdentifier = target.Identifier, Properties = target.Properties },
            ],
            _ => throw new InvalidOperationException($"Invalid parent placement fix is not supported for '{target.SemanticType}'."),
        };
    }

    private static string BuildSummary(OracleApexCodeAction action, OracleApexCodeActionRequest request, OracleApexSemanticEditResult result)
        => result.IsSuccess
            ? action.Kind switch
            {
                OracleApexCodeActionKind.RenamePage or OracleApexCodeActionKind.RenameRegion or OracleApexCodeActionKind.RenameItem or OracleApexCodeActionKind.RenameSharedComponent => $"Renamed '{action.TargetIdentifier}' to '{request.NewIdentifier}'.",
                OracleApexCodeActionKind.AddRegionToPage => $"Added region '{request.NewIdentifier}' to page '{action.TargetIdentifier}'.",
                OracleApexCodeActionKind.AddItemToRegion => $"Added item '{request.NewIdentifier}' to region '{action.TargetIdentifier}'.",
                OracleApexCodeActionKind.AddNavigationEntry => $"Added navigation entry '{request.NewIdentifier}' to '{action.TargetIdentifier}'.",
                OracleApexCodeActionKind.RemovePageSafely => $"Removed page '{action.TargetIdentifier}' safely.",
                OracleApexCodeActionKind.RemoveRegionSafely => $"Removed region '{action.TargetIdentifier}' safely.",
                OracleApexCodeActionKind.FixMissingRequiredProperties => $"Fixed missing required property '{action.RequiredPropertyName}' for '{action.TargetSemanticType}'.",
                OracleApexCodeActionKind.FixInvalidParentPlacement => $"Moved '{action.TargetIdentifier}' to a valid parent.",
                OracleApexCodeActionKind.ReviewVersionMigrationImpact => action.ReviewMessage,
                _ => result.Message,
            }
            : result.Message;

    private static OracleApexWorkspaceIndexEntry? FindAncestor(OracleApexWorkspaceIndex index, OracleApexWorkspaceIndexEntry entry, string semanticType)
    {
        var current = entry;
        while (!string.IsNullOrWhiteSpace(current.ParentNodeId))
        {
            var parent = index.Entries.FirstOrDefault(item => string.Equals(item.NodeId, current.ParentNodeId, StringComparison.OrdinalIgnoreCase));
            if (parent is null)
            {
                return null;
            }

            if (string.Equals(parent.SemanticType, semanticType, StringComparison.OrdinalIgnoreCase))
            {
                return parent;
            }

            current = parent;
        }

        return null;
    }

    private static string RequireNewIdentifier(OracleApexCodeActionRequest request)
        => string.IsNullOrWhiteSpace(request.NewIdentifier)
            ? throw new InvalidOperationException("Code action requires a new identifier.")
            : request.NewIdentifier;

    private static IReadOnlyDictionary<string, string> RequireSingleProperty(OracleApexCodeActionRequest request, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new InvalidOperationException("Code action did not specify a required property name.");
        }

        if (!request.Properties.TryGetValue(propertyName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Code action requires property '{propertyName}'.");
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [propertyName] = value };
    }

    private static string RequireParentIdentifier(OracleApexWorkspaceIndex index, OracleApexWorkspaceIndexEntry? entry)
        => GetParentIdentifier(index, entry) ?? throw new InvalidOperationException("Code action target does not have a resolvable parent.");

    private static string? GetParentIdentifier(OracleApexWorkspaceIndex index, OracleApexWorkspaceIndexEntry? entry)
        => entry is null || string.IsNullOrWhiteSpace(entry.ParentNodeId)
            ? null
            : index.Entries.FirstOrDefault(item => string.Equals(item.NodeId, entry.ParentNodeId, StringComparison.OrdinalIgnoreCase))?.Identifier;

    private static string ExtractQuotedValue(string message)
    {
        var quotedValues = new List<string>();
        var searchStart = 0;
        while (searchStart < message.Length)
        {
            var first = message.IndexOf('\'', searchStart);
            if (first < 0)
            {
                break;
            }

            var second = message.IndexOf('\'', first + 1);
            if (second <= first)
            {
                break;
            }

            quotedValues.Add(message[(first + 1)..second]);
            searchStart = second + 1;
        }

        return quotedValues.Count == 0 ? string.Empty : quotedValues[^1];
    }
}

public sealed class OracleApexCodeAction
{
    public string Id { get; init; } = string.Empty;
    public OracleApexCodeActionKind Kind { get; init; }
    public string Title { get; init; } = string.Empty;
    public string TargetNodeId { get; init; } = string.Empty;
    public string TargetIdentifier { get; init; } = string.Empty;
    public string TargetSemanticType { get; init; } = string.Empty;
    public string ParentNodeId { get; init; } = string.Empty;
    public string DestinationNodeId { get; init; } = string.Empty;
    public string RequiredPropertyName { get; init; } = string.Empty;
    public string ReviewMessage { get; init; } = string.Empty;
}

public enum OracleApexCodeActionKind
{
    RenamePage,
    RenameRegion,
    RenameItem,
    RenameSharedComponent,
    AddRegionToPage,
    AddItemToRegion,
    AddNavigationEntry,
    RemovePageSafely,
    RemoveRegionSafely,
    FixMissingRequiredProperties,
    FixInvalidParentPlacement,
    ReviewVersionMigrationImpact,
}

public sealed class OracleApexCodeActionRequest
{
    public string ActionId { get; init; } = string.Empty;
    public string NewIdentifier { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class OracleApexCodeActionResult
{
    public bool IsSuccess { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> ChangedFiles { get; init; } = Array.Empty<string>();
    public OracleApexSemanticEditDiagnostics Diagnostics { get; init; } = new();
    public OracleApexWorkspaceIndex WorkspaceIndex { get; init; } = new();
}
