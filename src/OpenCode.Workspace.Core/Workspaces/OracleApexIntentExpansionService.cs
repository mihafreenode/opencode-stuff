using System.Text.RegularExpressions;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class OracleApexIntentExpansionService
{
    private static readonly Regex CrudPattern = new(@"(?:build|create|add)\s+(?:a\s+)?crud\s+(?:module\s+)?for\s+(?<entity>[A-Za-z][A-Za-z0-9 _-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ReportingPattern = new(@"(?:build|create|add)\s+(?:a\s+)?reporting\s+(?:module\s+)?for\s+(?<entity>[A-Za-z][A-Za-z0-9 _-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ManagementPattern = new(@"(?:build|create|add)\s+(?:a\s+)?(?<entity>[A-Za-z][A-Za-z0-9 _-]+?)\s+management(?:\s+module)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AdministrationPattern = new(@"(?:build|create|add)\s+(?<entity>[A-Za-z][A-Za-z0-9 _-]+?)\s+administration", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public OracleApexIntentExpansionResult Expand(OracleApexWorkspaceIndex index, string intent)
    {
        var normalizedIntent = intent.Trim();
        if (string.IsNullOrWhiteSpace(normalizedIntent))
        {
            return new OracleApexIntentExpansionResult();
        }

        var lowerIntent = normalizedIntent.ToLowerInvariant();
        var explicitApproach = ResolveExplicitApproach(lowerIntent);
        var alternatives = new List<OracleApexBlueprintAlternative>();
        OracleApexModuleBlueprint? module = null;

        var crudMatch = CrudPattern.Match(normalizedIntent);
        if (crudMatch.Success)
        {
            var entity = BuildEntityBlueprint(crudMatch.Groups["entity"].Value.Trim());
            alternatives.AddRange(BuildCrudAlternatives(entity));
            module = BuildModuleBlueprint("CRUD Module", entity.DisplayPluralName, entity, explicitApproach ?? "interactive-grid", explicitApproach is not null ? Array.Empty<OracleApexBlueprintAlternative>() : alternatives, supportsSharedLov: true);
        }
        else
        {
            var reportingMatch = ReportingPattern.Match(normalizedIntent);
            if (reportingMatch.Success)
            {
                var entity = BuildEntityBlueprint(reportingMatch.Groups["entity"].Value.Trim());
                module = BuildModuleBlueprint("Reporting", $"{entity.DisplayPluralName} Reporting", entity, explicitApproach ?? "interactive-report", Array.Empty<OracleApexBlueprintAlternative>(), supportsSharedLov: false);
            }
            else
            {
                var managementMatch = ManagementPattern.Match(normalizedIntent);
                if (managementMatch.Success)
                {
                    var entity = BuildEntityBlueprint(managementMatch.Groups["entity"].Value.Trim());
                    alternatives.AddRange(BuildCrudAlternatives(entity));
                    module = BuildModuleBlueprint("CRUD Module", $"{entity.DisplayPluralName} Management", entity, explicitApproach ?? "report-form", explicitApproach is not null ? Array.Empty<OracleApexBlueprintAlternative>() : alternatives, supportsSharedLov: true);
                }
                else if (lowerIntent.Contains("help desk", StringComparison.Ordinal))
                {
                    var entity = new OracleApexEntityBlueprint
                    {
                        Name = "Ticket",
                        PluralName = "Tickets",
                        DisplayName = "Ticket",
                        DisplayPluralName = "Tickets",
                        ModuleName = "Help Desk",
                    };
                    alternatives.AddRange(BuildCrudAlternatives(entity));
                    module = BuildModuleBlueprint("CRUD Module", "Help Desk", entity, explicitApproach ?? "report-form", explicitApproach is not null ? Array.Empty<OracleApexBlueprintAlternative>() : alternatives, supportsSharedLov: true);
                    module.IncludeDashboard = true;
                }
                else
                {
                    var administrationMatch = AdministrationPattern.Match(normalizedIntent);
                    if (administrationMatch.Success)
                    {
                        var entity = BuildEntityBlueprint(administrationMatch.Groups["entity"].Value.Trim());
                        module = BuildModuleBlueprint("Administration", $"{entity.DisplayPluralName} Administration", entity, explicitApproach ?? "report-form", Array.Empty<OracleApexBlueprintAlternative>(), supportsSharedLov: false);
                        module.RequiresAuthorization = true;
                    }
                }
            }
        }

        if (module is null)
        {
            return new OracleApexIntentExpansionResult();
        }

        PopulateWorkspaceAwareness(index, module);
        var blueprint = new OracleApexApplicationBlueprint
        {
            Intent = normalizedIntent,
            Summary = $"Design {module.Name} for the current Oracle APEX application.",
            Modules = [module],
            Alternatives = module.Alternatives,
            DeploymentTargets = index.DeploymentProfiles.Select(profile => profile.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList(),
        };

        if (module.Alternatives.Count > 1)
        {
            blueprint.UnresolvedQuestions.Add($"Choose an implementation approach for {module.Entity?.DisplayPluralName ?? module.Name}: {string.Join(" or ", module.Alternatives.Select(item => item.Label))}.");
        }

        if (blueprint.DeploymentTargets.Count > 0)
        {
            blueprint.Assumptions.Add($"Workspace deployment targets: {string.Join(", ", blueprint.DeploymentTargets)}.");
        }

        if (!string.IsNullOrWhiteSpace(module.AuthenticationSchemeName))
        {
            blueprint.Assumptions.Add($"Reuse authentication scheme '{module.AuthenticationSchemeName}'.");
        }

        return new OracleApexIntentExpansionResult { Blueprint = blueprint };
    }

    private static OracleApexModuleBlueprint BuildModuleBlueprint(string blueprintName, string moduleName, OracleApexEntityBlueprint entity, string approach, IReadOnlyList<OracleApexBlueprintAlternative> alternatives, bool supportsSharedLov)
    {
        var pageBaseName = entity.DisplayPluralName;
        return new OracleApexModuleBlueprint
        {
            BlueprintName = blueprintName,
            Name = moduleName,
            Entity = entity,
            Approach = approach,
            ReportPageName = pageBaseName,
            FormPageName = $"{entity.DisplayName} Form",
            DashboardPageName = $"{entity.DisplayPluralName} Dashboard",
            RequiresReportPage = true,
            RequiresFormPage = approach != "interactive-grid" && approach != "interactive-report",
            RequiresNavigationEntries = true,
            RequiresValidation = approach != "interactive-grid" && approach != "interactive-report",
            RequiresSharedLov = supportsSharedLov,
            Alternatives = alternatives,
        };
    }

    private static List<OracleApexBlueprintAlternative> BuildCrudAlternatives(OracleApexEntityBlueprint entity)
    {
        return
        [
            new OracleApexBlueprintAlternative
            {
                Id = "interactive-grid",
                Label = "Interactive Grid",
                Description = $"Single-page maintenance for {entity.DisplayPluralName} with fast inline edits.",
                TradeOffs = "Faster CRUD delivery, but less guided validation and detail layout control.",
                IsRecommended = true,
            },
            new OracleApexBlueprintAlternative
            {
                Id = "report-form",
                Label = "Report + Form",
                Description = $"Separate report and form pages for {entity.DisplayPluralName}.",
                TradeOffs = "More pages, but clearer navigation, validation, and future extensibility.",
            },
        ];
    }

    private static string? ResolveExplicitApproach(string lowerIntent)
    {
        if (lowerIntent.Contains("interactive grid", StringComparison.Ordinal))
        {
            return "interactive-grid";
        }

        if (lowerIntent.Contains("interactive report", StringComparison.Ordinal) || lowerIntent.Contains("reporting", StringComparison.Ordinal))
        {
            return "interactive-report";
        }

        if (lowerIntent.Contains("report + form", StringComparison.Ordinal)
            || lowerIntent.Contains("report and form", StringComparison.Ordinal)
            || lowerIntent.Contains("form page", StringComparison.Ordinal))
        {
            return "report-form";
        }

        return null;
    }

    private static OracleApexEntityBlueprint BuildEntityBlueprint(string rawName)
    {
        var normalized = NormalizeWords(rawName);
        var singular = normalized;
        var plural = normalized.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? normalized : normalized + "s";

        if (string.Equals(normalized, "Inventory", StringComparison.OrdinalIgnoreCase))
        {
            singular = "Inventory Item";
            plural = "Inventory";
        }

        if (string.Equals(normalized, "User", StringComparison.OrdinalIgnoreCase))
        {
            plural = "Users";
        }

        return new OracleApexEntityBlueprint
        {
            Name = singular,
            PluralName = plural,
            DisplayName = singular,
            DisplayPluralName = plural,
            ModuleName = plural,
        };
    }

    private static void PopulateWorkspaceAwareness(OracleApexWorkspaceIndex index, OracleApexModuleBlueprint module)
    {
        var entity = module.Entity;
        if (entity is null)
        {
            return;
        }

        module.AuthenticationSchemeName = index.SharedComponents.FirstOrDefault(item => item.SemanticType == "authentication-scheme")?.Identifier ?? string.Empty;

        if (module.RequiresAuthorization)
        {
            module.AuthorizationSchemeName = index.SharedComponents.FirstOrDefault(item => item.SemanticType == "authorization-scheme" && item.Identifier.Contains("admin", StringComparison.OrdinalIgnoreCase))?.Identifier
                ?? index.SharedComponents.FirstOrDefault(item => item.SemanticType == "authorization-scheme")?.Identifier
                ?? "ADMINISTRATION_ACCESS";
        }

        var entityToken = entity.DisplayName.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
        module.SharedLovName = index.SharedComponents.FirstOrDefault(item => item.SemanticType == "lov" && item.Identifier.Contains(entityToken, StringComparison.OrdinalIgnoreCase))?.Identifier ?? string.Empty;
        module.ReportPageExists = index.Pages.Any(page => string.Equals(page.Identifier, module.ReportPageName, StringComparison.OrdinalIgnoreCase));
        module.FormPageExists = index.Pages.Any(page => string.Equals(page.Identifier, module.FormPageName, StringComparison.OrdinalIgnoreCase));
        module.DashboardPageExists = module.IncludeDashboard && index.Pages.Any(page => string.Equals(page.Identifier, module.DashboardPageName, StringComparison.OrdinalIgnoreCase));
        module.NavigationMenuName = ResolveNavigationMenuName(index);
        module.ReportNavigationExists = index.NavigationEntries.Any(entry => string.Equals(entry.Identifier, module.ReportPageName, StringComparison.OrdinalIgnoreCase));
        module.FormNavigationExists = index.NavigationEntries.Any(entry => string.Equals(entry.Identifier, module.FormPageName, StringComparison.OrdinalIgnoreCase));
        module.DashboardNavigationExists = module.IncludeDashboard && index.NavigationEntries.Any(entry => string.Equals(entry.Identifier, module.DashboardPageName, StringComparison.OrdinalIgnoreCase));
        module.ReportRegionExists = HasRegion(index, module.ReportPageName, entity.DisplayPluralName);
        module.FormRegionExists = HasRegion(index, module.FormPageName, entity.DisplayName);
        module.ValidationExists = HasProcess(index, module.FormPageName, $"Validate {entity.DisplayName}");

        if (!string.IsNullOrWhiteSpace(module.SharedLovName))
        {
            module.ReusedComponents.Add($"LOV:{module.SharedLovName}");
        }

        if (!string.IsNullOrWhiteSpace(module.NavigationMenuName))
        {
            module.ReusedComponents.Add($"Navigation:{module.NavigationMenuName}");
        }

        if (!string.IsNullOrWhiteSpace(module.AuthorizationSchemeName))
        {
            module.ReusedComponents.Add($"Authorization:{module.AuthorizationSchemeName}");
        }
    }

    private static bool HasRegion(OracleApexWorkspaceIndex index, string pageName, string regionHint)
    {
        var page = index.Pages.FirstOrDefault(item => string.Equals(item.Identifier, pageName, StringComparison.OrdinalIgnoreCase));
        if (page is null)
        {
            return false;
        }

        return index.Regions.Any(region => string.Equals(region.ParentNodeId, page.NodeId, StringComparison.OrdinalIgnoreCase)
            && region.Identifier.Contains(regionHint, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasProcess(OracleApexWorkspaceIndex index, string pageName, string processName)
    {
        var page = index.Pages.FirstOrDefault(item => string.Equals(item.Identifier, pageName, StringComparison.OrdinalIgnoreCase));
        if (page is null)
        {
            return false;
        }

        return index.Entries.Any(entry => string.Equals(entry.ParentNodeId, page.NodeId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.SemanticType, "process", StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.Identifier, processName, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveNavigationMenuName(OracleApexWorkspaceIndex index)
    {
        var menus = index.SharedComponents.Where(item => item.SemanticType == "navigation-menu").ToList();
        return menus.Count == 1 ? menus[0].Identifier : string.Empty;
    }

    private static string NormalizeWords(string value)
        => string.Join(" ", value.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
}

public sealed class OracleApexIntentExpansionResult
{
    public OracleApexApplicationBlueprint? Blueprint { get; init; }
}

public sealed class OracleApexApplicationBlueprint
{
    public string Intent { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<OracleApexModuleBlueprint> Modules { get; init; } = Array.Empty<OracleApexModuleBlueprint>();
    public IReadOnlyList<OracleApexBlueprintAlternative> Alternatives { get; init; } = Array.Empty<OracleApexBlueprintAlternative>();
    public IReadOnlyList<string> DeploymentTargets { get; init; } = Array.Empty<string>();
    public List<string> Assumptions { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> UnresolvedQuestions { get; } = [];
}

public sealed class OracleApexModuleBlueprint
{
    public string BlueprintName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public OracleApexEntityBlueprint? Entity { get; init; }
    public string Approach { get; init; } = string.Empty;
    public string ReportPageName { get; init; } = string.Empty;
    public string FormPageName { get; init; } = string.Empty;
    public string DashboardPageName { get; set; } = string.Empty;
    public bool RequiresReportPage { get; init; }
    public bool RequiresFormPage { get; init; }
    public bool RequiresNavigationEntries { get; init; }
    public bool RequiresValidation { get; init; }
    public bool RequiresSharedLov { get; init; }
    public bool RequiresAuthorization { get; set; }
    public bool IncludeDashboard { get; set; }
    public bool ReportPageExists { get; set; }
    public bool FormPageExists { get; set; }
    public bool DashboardPageExists { get; set; }
    public bool ReportNavigationExists { get; set; }
    public bool FormNavigationExists { get; set; }
    public bool DashboardNavigationExists { get; set; }
    public bool ReportRegionExists { get; set; }
    public bool FormRegionExists { get; set; }
    public bool ValidationExists { get; set; }
    public string NavigationMenuName { get; set; } = string.Empty;
    public string SharedLovName { get; set; } = string.Empty;
    public string AuthenticationSchemeName { get; set; } = string.Empty;
    public string AuthorizationSchemeName { get; set; } = string.Empty;
    public List<string> ReusedComponents { get; } = [];
    public IReadOnlyList<OracleApexBlueprintAlternative> Alternatives { get; init; } = Array.Empty<OracleApexBlueprintAlternative>();
}

public sealed class OracleApexEntityBlueprint
{
    public string Name { get; init; } = string.Empty;
    public string PluralName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string DisplayPluralName { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
}

public sealed class OracleApexBlueprintAlternative
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string TradeOffs { get; init; } = string.Empty;
    public bool IsRecommended { get; init; }
}
