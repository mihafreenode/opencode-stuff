using System.Text.Json;
using System.Text.RegularExpressions;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class OracleApexAtlasBuilder
{
    private const string AtlasDirectoryName = "apexlang-atlas";
    private const string LanguageReferenceDirectoryName = "apexlang-language-reference";
    private const string DocumentationRelativePath = "docs/oracle-apex-atlas.md";
    private const string ComponentCatalogDocumentationRelativePath = "docs/oracle-apex-component-catalog.md";
    private const string WorkspaceIndexFileName = "workspace-index.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly OracleApexLanguageReferenceCatalog PreviousLanguageReferenceCatalog = OracleApexBuiltInLanguageReference.CreatePrevious();
    private static readonly OracleApexLanguageReferenceCatalog CurrentLanguageReferenceCatalog = OracleApexBuiltInLanguageReference.Create();
    private static readonly OracleApexLanguageReferenceCompatibilityResult PreviousLanguageReferenceCompatibility = OracleApexComponentCatalog.AtlasSeed.CompareWithReference(PreviousLanguageReferenceCatalog);
    private static readonly OracleApexLanguageReferenceCompatibilityResult CurrentLanguageReferenceCompatibility = OracleApexComponentCatalog.AtlasSeed.CompareWithReference(CurrentLanguageReferenceCatalog);
    private static readonly OracleApexLanguageReferenceDiffReport BuiltInLanguageReferenceDiff = new OracleApexLanguageReferenceCatalogComparer().Compare(PreviousLanguageReferenceCatalog, CurrentLanguageReferenceCatalog, PreviousLanguageReferenceCompatibility, CurrentLanguageReferenceCompatibility);
    private static readonly OracleApexLanguageReferenceWorkspaceImpactAnalyzer BuiltInLanguageReferenceAnalyzer = new(BuiltInLanguageReferenceDiff);
    private static readonly string[] KnownBlockTypes =
    [
        "authorization scheme",
        "authentication scheme",
        "navigation menu",
        "list of values",
        "dynamic action",
        "build option",
        "static file",
        "application",
        "region",
        "button",
        "process",
        "branch",
        "plugin",
        "entry",
        "page",
        "item",
        "list",
    ];
    private static readonly Regex PropertyPattern = new(@"^(?<key>[A-Za-z][A-Za-z0-9_\-]*)\s*:\s*(?<value>.*)$", RegexOptions.Compiled);
    private static readonly Regex PageFilePattern = new(@"^p(?<id>\d+)(?:-(?<alias>.+))?\.apx$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DatabaseReferencePattern = new(@"\b(?:from|join|update|into|merge\s+into|delete\s+from)\s+([A-Za-z][A-Za-z0-9_$#\.]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PlSqlQualifiedIdentifierPattern = new(@"\b([A-Za-z][A-Za-z0-9_$#]*)\.([A-Za-z][A-Za-z0-9_$#]*)\b", RegexOptions.Compiled);
    private static readonly Regex RestEndpointPattern = new(@"\b(?:https?://[^\s""']+|/[A-Za-z0-9_\-./]+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "JOIN", "LEFT", "RIGHT", "FULL", "OUTER", "INNER", "ON", "UPDATE", "DELETE", "INSERT", "INTO", "VALUES",
        "BEGIN", "END", "DECLARE", "CREATE", "OR", "REPLACE", "PACKAGE", "PROCEDURE", "FUNCTION", "RETURN", "MERGE", "WHEN", "THEN", "ELSE",
    };
    private static readonly string[] RequiredAtlasFiles =
    [
        "atlas.json",
        "pages.json",
        "regions.json",
        "navigation.json",
        "shared-components.json",
        "dependencies.json",
        "search-index.json",
        WorkspaceIndexFileName,
        "state.json",
    ];
    private readonly OracleApexComponentCatalog _componentCatalog = OracleApexComponentCatalog.Default;
    private readonly OracleApexWorkspaceIndexBuilder _workspaceIndexBuilder = new(new OracleApexSemanticModelBuilder(OracleApexComponentCatalog.Default));

    public OracleApexAtlasBuildResult Rebuild(WorkspaceDefinition definition, WorkspacePaths paths, string? environmentName = null, bool force = false)
    {
        if (!OracleWorkspaceFamily.HasApex(definition) || definition.Oracle.Apex.Environments.Count == 0)
        {
            return OracleApexAtlasBuildResult.Skipped("Oracle APEX Atlas is not applicable for this workspace.");
        }

        var resolvedEnvironmentName = string.IsNullOrWhiteSpace(environmentName)
            ? (!string.IsNullOrWhiteSpace(definition.Oracle.Apex.DefaultEnvironment)
                ? definition.Oracle.Apex.DefaultEnvironment
                : definition.Oracle.Apex.Environments.Keys.First())
            : environmentName;
        var environment = definition.Oracle.Apex.Environments[resolvedEnvironmentName!];
        return Rebuild(paths, environment, resolvedEnvironmentName!, force);
    }

    public OracleApexAtlasBuildResult Rebuild(WorkspacePaths paths, OracleApexEnvironmentPreferences environment, string environmentName, bool force = false)
    {
        var atlasRootPath = Path.Combine(paths.OpencodePath, "knowledge", AtlasDirectoryName);
        var docsPath = Path.Combine(paths.RootPath, DocumentationRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var componentCatalogDocsPath = Path.Combine(paths.RootPath, ComponentCatalogDocumentationRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var sourcePath = Path.Combine(paths.RootPath, environment.SourcePath ?? string.Empty);

        if (string.IsNullOrWhiteSpace(environment.SourcePath))
        {
            return OracleApexAtlasBuildResult.Skipped("Oracle APEX Atlas skipped because sourcePath is not configured.");
        }

        string sourceHash = string.Empty;

        try
        {
            if (!Directory.Exists(sourcePath))
            {
                return WriteFailureState(atlasRootPath, sourcePath, environmentName, sourceHash, $"Oracle APEX Atlas source path '{environment.SourcePath}' does not exist.", docsPath);
            }

            sourceHash = ComputeSourceHash(sourcePath);
            if (!force && IsCurrent(atlasRootPath, docsPath, sourcePath, sourceHash))
            {
                return OracleApexAtlasBuildResult.Skipped("Oracle APEX Atlas is already current.", atlasRootPath, docsPath, sourceHash);
            }

            var applicationFile = Path.Combine(sourcePath, "application.apx");
            if (!File.Exists(applicationFile))
            {
                return WriteFailureState(atlasRootPath, sourcePath, environmentName, sourceHash, $"Oracle APEX Atlas source path '{environment.SourcePath}' does not contain application.apx.", docsPath);
            }

            var workspaceIndex = _workspaceIndexBuilder.Build(paths.RootPath, environment, environmentName);
            var semanticModel = workspaceIndex.SemanticModel;
            if (semanticModel.Application is null)
            {
                return WriteFailureState(atlasRootPath, sourcePath, environmentName, sourceHash, semanticModel.Diagnostics.FirstOrDefault(item => item.Severity == OracleApexSemanticDiagnosticSeverity.Error)?.Message ?? "Oracle APEX semantic model could not identify an application node.", docsPath);
            }

            var application = BuildApplicationFromSemanticModel(semanticModel.Application, environment, environmentName);
            var pages = BuildPagesFromSemanticModel(semanticModel, sourcePath);
            var regions = pages.SelectMany(page => page.Regions).OrderBy(region => region.PageId).ThenBy(region => region.Title, StringComparer.OrdinalIgnoreCase).ToList();
            var sharedComponents = BuildSharedComponentsFromSemanticModel(semanticModel);
            var navigation = BuildNavigationFromSemanticModel(semanticModel, pages, sharedComponents);
            var dependencies = BuildDependenciesFromSemanticModel(application, semanticModel, pages, sharedComponents);
            var searchIndex = BuildSearchIndexFromSemanticModel(semanticModel, pages, regions, sharedComponents, dependencies);

            var atlas = new AtlasDocument
            {
                Application = application,
                Pages = pages,
                Regions = regions,
                Navigation = navigation,
                SharedComponents = sharedComponents,
                Dependencies = dependencies,
                SearchIndex = searchIndex,
                Build = new AtlasBuildSummary
                {
                    EnvironmentName = environmentName,
                    SourcePath = environment.SourcePath ?? string.Empty,
                    SourceHash = sourceHash,
                    GeneratedUtc = DateTimeOffset.UtcNow,
                },
            };

            Directory.CreateDirectory(atlasRootPath);
            WriteJson(Path.Combine(atlasRootPath, "atlas.json"), atlas);
            WriteJson(Path.Combine(atlasRootPath, "pages.json"), pages);
            WriteJson(Path.Combine(atlasRootPath, "regions.json"), regions);
            WriteJson(Path.Combine(atlasRootPath, "navigation.json"), navigation);
            WriteJson(Path.Combine(atlasRootPath, "shared-components.json"), sharedComponents);
            WriteJson(Path.Combine(atlasRootPath, "dependencies.json"), dependencies);
            WriteJson(Path.Combine(atlasRootPath, "search-index.json"), searchIndex);
            WriteJson(Path.Combine(atlasRootPath, WorkspaceIndexFileName), workspaceIndex);

            var state = new AtlasState
            {
                Status = "ready",
                EnvironmentName = environmentName,
                SourcePath = environment.SourcePath ?? string.Empty,
                SourceHash = sourceHash,
                BuiltUtc = DateTimeOffset.UtcNow,
                ApplicationId = application.Id,
                ApplicationName = application.Name,
                DeploymentProfiles = workspaceIndex.DeploymentProfiles.Select(profile => profile.Name).ToList(),
                GeneratedFiles = RequiredAtlasFiles.ToList(),
                DocumentationPath = DocumentationRelativePath,
            };
            WriteJson(Path.Combine(atlasRootPath, "state.json"), state);
            WriteLanguageReferenceArtifacts(paths, sourceHash, workspaceIndex);

            var docsDirectory = Path.GetDirectoryName(docsPath);
            if (!string.IsNullOrWhiteSpace(docsDirectory))
            {
                Directory.CreateDirectory(docsDirectory);
            }

            File.WriteAllText(docsPath, BuildDocumentation(atlas, workspaceIndex, ResolveAtlasVersion(paths)).Replace("\r\n", "\n", StringComparison.Ordinal));
            Directory.CreateDirectory(Path.GetDirectoryName(componentCatalogDocsPath)!);
            File.WriteAllText(componentCatalogDocsPath, _componentCatalog.BuildDocumentation().Replace("\r\n", "\n", StringComparison.Ordinal));
            return OracleApexAtlasBuildResult.Success(atlasRootPath, docsPath, sourceHash);
        }
        catch (Exception exception)
        {
            return WriteFailureState(atlasRootPath, sourcePath, environmentName, sourceHash, exception.Message, docsPath);
        }
    }

    private static bool IsCurrent(string atlasRootPath, string docsPath, string sourcePath, string sourceHash)
    {
        if (!Directory.Exists(atlasRootPath) || !File.Exists(docsPath))
        {
            return false;
        }

        if (RequiredAtlasFiles.Any(fileName => !File.Exists(Path.Combine(atlasRootPath, fileName))))
        {
            return false;
        }

        var statePath = Path.Combine(atlasRootPath, "state.json");
        if (!File.Exists(statePath))
        {
            return false;
        }

        try
        {
            var state = JsonSerializer.Deserialize<AtlasState>(File.ReadAllText(statePath), JsonOptions);
            return state is not null
                && string.Equals(state.Status, "ready", StringComparison.OrdinalIgnoreCase)
                && string.Equals(state.SourceHash, sourceHash, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static OracleApexAtlasBuildResult WriteFailureState(string atlasRootPath, string sourcePath, string environmentName, string sourceHash, string errorMessage, string docsPath)
    {
        Directory.CreateDirectory(atlasRootPath);
        WriteJson(Path.Combine(atlasRootPath, "state.json"), new AtlasState
        {
            Status = "failed",
            EnvironmentName = environmentName,
            SourcePath = sourcePath,
            SourceHash = sourceHash,
            BuiltUtc = DateTimeOffset.UtcNow,
            Error = errorMessage,
            DocumentationPath = DocumentationRelativePath,
        });
        return OracleApexAtlasBuildResult.Failed(errorMessage, atlasRootPath, docsPath, sourceHash);
    }

    private static string ComputeSourceHash(string sourcePath)
    {
        var files = Directory.GetFiles(sourcePath, "*.apx", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(sourcePath, path), StringComparer.OrdinalIgnoreCase)
            .Select(path => $"{Path.GetRelativePath(sourcePath, path).Replace('\\', '/')}\n{NormalizeContent(File.ReadAllText(path))}")
            .ToArray();
        return WorkspaceAppliedStateService.ComputeHash(files);
    }

    private static ParsedNode ParseFirstRootNode(string filePath, string expectedType)
    {
        var roots = ParseFile(filePath);
        var node = roots.FirstOrDefault(item => string.Equals(item.Type, expectedType, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            throw new InvalidOperationException($"{Path.GetFileName(filePath)} does not contain an '{expectedType}' block.");
        }

        return node;
    }

    private void WriteLanguageReferenceArtifacts(WorkspacePaths paths, string sourceHash, OracleApexWorkspaceIndex workspaceIndex)
    {
        var referenceCatalog = CurrentLanguageReferenceCatalog;
        var compatibility = CurrentLanguageReferenceCompatibility;
        var diffMarkdown = new OracleApexLanguageReferenceCatalogComparer().BuildMarkdown(BuiltInLanguageReferenceDiff);
        var diffJsonPath = ".opencode/knowledge/apexlang-language-reference/language-reference-diff.json";
        var diffMarkdownPath = ".opencode/knowledge/apexlang-language-reference/docs/language-reference-diff.md";
        var workspaceSummary = BuiltInLanguageReferenceAnalyzer.BuildWorkspaceSummary(workspaceIndex, ResolveAtlasVersion(paths), diffJsonPath, diffMarkdownPath);
        var root = Path.Combine(paths.OpencodePath, "knowledge", LanguageReferenceDirectoryName);
        Directory.CreateDirectory(root);
        WriteJson(Path.Combine(root, "language-reference-state.json"), new
        {
            status = "ready",
            apexVersion = referenceCatalog.ApexVersion,
            grammarVersion = referenceCatalog.GrammarVersion,
            sourceHash,
            provenance = referenceCatalog.Provenance,
        });
        WriteJson(Path.Combine(root, "grammar-version.json"), new { apexVersion = referenceCatalog.ApexVersion, grammarVersion = referenceCatalog.GrammarVersion });
        WriteJson(Path.Combine(root, "component-reference-index.json"), referenceCatalog.Components.Values.Select(component => new
        {
            component.CanonicalName,
            component.DisplayName,
            component.DocumentationAnchor,
            Parents = component.ParentComponents,
            Children = component.ChildComponents,
            ExampleCount = component.CanonicalExamples.Count,
        }).OrderBy(item => item.CanonicalName, StringComparer.OrdinalIgnoreCase).ToList());
        WriteJson(Path.Combine(root, "property-reference-index.json"), referenceCatalog.Components.Values.SelectMany(component => component.DirectProperties.Concat(component.PropertyGroups.SelectMany(group => group.Properties)).Select(property => new
        {
            Component = component.CanonicalName,
            property.PropertyPath,
            property.DataType,
            property.Required,
            property.DefaultValue,
            property.EnumValues,
            property.AppliesWhen,
        })).OrderBy(item => item.Component, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.PropertyPath, StringComparer.OrdinalIgnoreCase).ToList());
        WriteJson(Path.Combine(root, "catalog-compatibility.json"), compatibility);
        WriteJson(Path.Combine(root, "language-reference-diff.json"), BuiltInLanguageReferenceDiff);
        WriteFile(Path.Combine(root, "docs", "language-reference-summary.md"), BuildLanguageReferenceSummary(referenceCatalog, compatibility, workspaceSummary));
        WriteFile(Path.Combine(root, "docs", "language-reference-diff.md"), diffMarkdown);
        WriteFile(Path.Combine(root, "prompts", "apexlang-language-reference.md"), BuildLanguageReferencePrompt(referenceCatalog, compatibility, workspaceSummary));
    }

    private static string BuildLanguageReferenceSummary(OracleApexLanguageReferenceCatalog referenceCatalog, OracleApexLanguageReferenceCompatibilityResult compatibility, OracleApexLanguageReferenceWorkspaceCompatibilitySummary workspaceSummary)
    {
        var lines = new List<string>
        {
            "# APEXlang Language Reference Summary",
            string.Empty,
            $"- APEX version: {referenceCatalog.ApexVersion}",
            $"- Grammar version: {referenceCatalog.GrammarVersion}",
            $"- Components: {referenceCatalog.Components.Count}",
            $"- Compatibility warnings: {compatibility.Warnings.Count}",
            string.Empty,
            "## Version Compatibility",
            $"- Project version: {ValueOrUnknown(workspaceSummary.ProjectVersion)}",
            $"- Reference version: {workspaceSummary.ReferenceVersion}",
            $"- Atlas version: {ValueOrUnknown(workspaceSummary.AtlasVersion)}",
            $"- Status: {workspaceSummary.Status}",
            $"- Relevant findings: {workspaceSummary.RelevantFindingCount}",
            $"- Full diff JSON: {workspaceSummary.DiffJsonPath}",
            $"- Full diff Markdown: {workspaceSummary.DiffMarkdownPath}",
            string.Empty,
            "## Core Components",
        };

        foreach (var component in referenceCatalog.Components.Values.OrderBy(item => item.CanonicalName, StringComparer.OrdinalIgnoreCase).Take(20))
        {
            lines.Add($"- {component.CanonicalName}: children={(component.ChildComponents.Count == 0 ? "none" : string.Join(", ", component.ChildComponents))}; properties={component.DirectProperties.Count + component.PropertyGroups.Sum(group => group.Properties.Count)}");
        }

        if (compatibility.Warnings.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Compatibility Warnings");
            foreach (var warning in compatibility.Warnings.Take(20))
            {
                lines.Add($"- {warning.ComponentName} {warning.PropertyName}: {warning.Message}");
            }
        }

        if (workspaceSummary.Findings.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("## Workspace Impact");
            foreach (var finding in workspaceSummary.Findings)
            {
                lines.Add($"- {finding.Message}");
            }
        }

        return string.Join("\n", lines) + "\n";
    }

    private static string BuildLanguageReferencePrompt(OracleApexLanguageReferenceCatalog referenceCatalog, OracleApexLanguageReferenceCompatibilityResult compatibility, OracleApexLanguageReferenceWorkspaceCompatibilitySummary workspaceSummary)
    {
        var lines = new List<string>
        {
            "# APEXlang Language Reference",
            $"Official reference version: {referenceCatalog.ApexVersion}",
            $"Grammar version: {referenceCatalog.GrammarVersion}",
            "",
            "Use the official hierarchy and documented properties first. Use Atlas enrichment for Builder-specific metadata and SQLcl as the final validation authority.",
            "",
            "## Version Compatibility",
            $"- Project version: {ValueOrUnknown(workspaceSummary.ProjectVersion)}",
            $"- Reference version: {workspaceSummary.ReferenceVersion}",
            $"- Atlas version: {ValueOrUnknown(workspaceSummary.AtlasVersion)}",
            $"- Status: {workspaceSummary.Status}",
            $"- Relevant findings: {workspaceSummary.RelevantFindingCount}",
            "",
            "Workspace impact:",
        };

        lines.AddRange(workspaceSummary.Findings.Count == 0
            ? ["- none"]
            : workspaceSummary.Findings.Select(finding => $"- {finding.Message}"));

        lines.AddRange([
            "",
            "Full details:",
            $"- {workspaceSummary.DiffJsonPath}",
            $"- {workspaceSummary.DiffMarkdownPath}",
            "",
            "## Canonical Components",
        ]);

        foreach (var component in referenceCatalog.Components.Values.OrderBy(item => item.CanonicalName, StringComparer.OrdinalIgnoreCase).Take(12))
        {
            lines.Add($"- {component.CanonicalName}: parents={(component.ParentComponents.Count == 0 ? "root" : string.Join(", ", component.ParentComponents))}; children={(component.ChildComponents.Count == 0 ? "none" : string.Join(", ", component.ChildComponents))}");
        }

        lines.Add(string.Empty);
        lines.Add("## Compatibility Notes");
        lines.Add(compatibility.Warnings.Count == 0 ? "- No known Atlas/reference discrepancies in the built-in index." : string.Join("\n", compatibility.Warnings.Take(10).Select(warning => $"- {warning.Message}")));
        return string.Join("\n", lines) + "\n";
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static ApplicationAtlasEntry BuildApplicationFromSemanticModel(OracleApexSemanticNode applicationNode, OracleApexEnvironmentPreferences environment, string environmentName)
    {
        return new ApplicationAtlasEntry
        {
            Id = ReadInt(applicationNode.GetProperty("id")) ?? environment.ApplicationId ?? 0,
            Name = applicationNode.GetProperty("name") ?? applicationNode.Identifier,
            Alias = applicationNode.GetProperty("alias") ?? string.Empty,
            Version = applicationNode.GetProperty("version") ?? string.Empty,
            Workspace = applicationNode.GetProperty("workspace") ?? environment.Workspace ?? string.Empty,
            ParsingSchema = applicationNode.GetProperty("parsing-schema") ?? environment.ParsingSchema ?? string.Empty,
            EnvironmentName = environmentName,
        };
    }

    private static List<PageAtlasEntry> BuildPagesFromSemanticModel(OracleApexSemanticModel semanticModel, string sourcePath)
        => semanticModel.GetNodes("page")
            .Select(page => new PageAtlasEntry
            {
                PageId = ReadInt(page.GetProperty("id")) ?? 0,
                Name = page.GetProperty("name") ?? page.Identifier,
                Alias = page.GetProperty("alias") ?? string.Empty,
                Mode = page.GetProperty("mode") ?? string.Empty,
                Authentication = page.GetProperty("authentication") ?? string.Empty,
                SourceFile = page.SourceFile,
                Regions = page.Children.Where(child => child.SemanticType == "region").Select(region => new RegionAtlasEntry
                {
                    PageId = ReadInt(page.GetProperty("id")) ?? 0,
                    PageName = page.GetProperty("name") ?? page.Identifier,
                    Name = region.GetProperty("name") ?? region.Identifier,
                    Title = region.GetProperty("title") ?? region.Identifier,
                    RegionType = region.GetProperty("type") ?? string.Empty,
                    SourceType = region.GetProperty("source-type") ?? string.Empty,
                    ReferencedTablesOrViews = region.ReferencedObjects.Where(IsDatabaseObject).ToList(),
                    ReferencedRestSources = region.ReferencedObjects.Where(IsRestReference).ToList(),
                }).ToList(),
                Items = page.Children.Where(child => child.SemanticType == "item").Select(BuildPageComponentFromSemanticNode).ToList(),
                Buttons = page.Children.Where(child => child.SemanticType == "button").Select(BuildPageComponentFromSemanticNode).ToList(),
                DynamicActions = page.Children.Where(child => child.SemanticType == "dynamic-action").Select(BuildPageComponentFromSemanticNode).ToList(),
                Processes = page.Children.Where(child => child.SemanticType == "process").Select(BuildPageComponentFromSemanticNode).ToList(),
                Branches = page.Children.Where(child => child.SemanticType == "branch").Select(branch => new BranchAtlasEntry
                {
                    Name = branch.GetProperty("name") ?? branch.Identifier,
                    TargetPageId = ReadInt(branch.GetProperty("target-page")),
                }).ToList(),
                Breadcrumb = page.GetProperty("breadcrumb") ?? string.Empty,
                ParentPageId = ReadInt(page.GetProperty("parent-page")),
            })
            .OrderBy(page => page.PageId)
            .ThenBy(page => page.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static SharedComponentsAtlas BuildSharedComponentsFromSemanticModel(OracleApexSemanticModel semanticModel)
    {
        var components = semanticModel.Nodes
            .Where(node => node.SemanticType is "lov" or "list" or "navigation-menu" or "authorization-scheme" or "authentication-scheme" or "build-option" or "static-file" or "plugin")
            .Select(node => new SharedComponentAtlasEntry
            {
                Category = node.SemanticType switch
                {
                    "lov" => "lovs",
                    "list" => "lists",
                    "navigation-menu" => "navigation-menus",
                    "authorization-scheme" => "authorization-schemes",
                    "authentication-scheme" => "authentication-schemes",
                    "build-option" => "build-options",
                    "static-file" => "static-files",
                    "plugin" => "plugins",
                    _ => node.SemanticType,
                },
                Name = node.GetProperty("name") ?? node.Identifier,
                Type = node.SemanticType,
                SourceFile = node.SourceFile,
                PageTargets = node.Children.Where(child => ReadInt(child.GetProperty("target-page")) is not null).Select(child => ReadInt(child.GetProperty("target-page"))!.Value).Distinct().ToList(),
                ChildEntries = node.Children.Select(child => new SharedComponentChildEntry
                {
                    Name = child.GetProperty("label") ?? child.GetProperty("name") ?? child.Identifier,
                    TargetPageId = ReadInt(child.GetProperty("target-page")),
                    ParentEntry = child.GetProperty("parent-entry") ?? string.Empty,
                }).ToList(),
            })
            .ToList();

        return new SharedComponentsAtlas
        {
            Lovs = components.Where(component => component.Category == "lovs").ToList(),
            Lists = components.Where(component => component.Category == "lists").ToList(),
            NavigationMenus = components.Where(component => component.Category == "navigation-menus").ToList(),
            AuthorizationSchemes = components.Where(component => component.Category == "authorization-schemes").ToList(),
            AuthenticationSchemes = components.Where(component => component.Category == "authentication-schemes").ToList(),
            BuildOptions = components.Where(component => component.Category == "build-options").ToList(),
            StaticFiles = components.Where(component => component.Category == "static-files").ToList(),
            Plugins = components.Where(component => component.Category == "plugins").ToList(),
        };
    }

    private static NavigationAtlas BuildNavigationFromSemanticModel(OracleApexSemanticModel semanticModel, IReadOnlyList<PageAtlasEntry> pages, SharedComponentsAtlas sharedComponents)
        => new()
        {
            Menus = sharedComponents.NavigationMenus.Select(menu => new NavigationMenuAtlasEntry
            {
                Name = menu.Name,
                SourceFile = menu.SourceFile,
                Entries = menu.ChildEntries,
            }).ToList(),
            Breadcrumbs = pages.Where(page => !string.IsNullOrWhiteSpace(page.Breadcrumb)).Select(page => new BreadcrumbAtlasEntry
            {
                PageId = page.PageId,
                PageName = page.Name,
                Breadcrumb = page.Breadcrumb,
            }).ToList(),
            PageHierarchy = pages.Select(page => new PageHierarchyAtlasEntry
            {
                PageId = page.PageId,
                PageName = page.Name,
                ParentPageId = page.ParentPageId,
            }).ToList(),
        };

    private static DependencyGraphAtlas BuildDependenciesFromSemanticModel(ApplicationAtlasEntry application, OracleApexSemanticModel semanticModel, IReadOnlyList<PageAtlasEntry> pages, SharedComponentsAtlas sharedComponents)
    {
        var nodes = new Dictionary<string, DependencyNodeAtlasEntry>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<DependencyEdgeAtlasEntry>();
        var applicationNodeId = $"application:{application.Id}";
        AddNode(nodes, new DependencyNodeAtlasEntry { Id = applicationNodeId, Type = "application", Name = application.Name });

        foreach (var semanticNode in semanticModel.Nodes.Where(node => node.SemanticType != "application"))
        {
            var dependencyNodeId = $"semantic:{semanticNode.SemanticType}:{semanticNode.NodeId}";
            AddNode(nodes, new DependencyNodeAtlasEntry { Id = dependencyNodeId, Type = semanticNode.SemanticType, Name = semanticNode.Identifier, ParentId = semanticNode.Parent is null ? applicationNodeId : $"semantic:{semanticNode.Parent.SemanticType}:{semanticNode.Parent.NodeId}" });
            edges.Add(new DependencyEdgeAtlasEntry { From = semanticNode.Parent is null ? applicationNodeId : $"semantic:{semanticNode.Parent.SemanticType}:{semanticNode.Parent.NodeId}", To = dependencyNodeId, Relationship = "contains" });

            foreach (var reference in semanticNode.ReferencedObjects)
            {
                var referenceType = IsDatabaseObject(reference)
                    ? ClassifyDatabaseObject(reference)
                    : IsRestReference(reference)
                        ? "rest-endpoint"
                        : "plsql-identifier";
                var referenceNodeId = $"{referenceType}:{reference}";
                AddNode(nodes, new DependencyNodeAtlasEntry { Id = referenceNodeId, Type = referenceType, Name = reference });
                edges.Add(new DependencyEdgeAtlasEntry { From = dependencyNodeId, To = referenceNodeId, Relationship = "references" });
            }
        }

        return new DependencyGraphAtlas
        {
            Nodes = nodes.Values.OrderBy(node => node.Type, StringComparer.OrdinalIgnoreCase).ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            Edges = edges.GroupBy(edge => $"{edge.From}|{edge.To}|{edge.Relationship}", StringComparer.OrdinalIgnoreCase).Select(group => group.First()).OrderBy(edge => edge.From, StringComparer.OrdinalIgnoreCase).ThenBy(edge => edge.To, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    private static SearchIndexAtlas BuildSearchIndexFromSemanticModel(OracleApexSemanticModel semanticModel, IReadOnlyList<PageAtlasEntry> pages, IReadOnlyList<RegionAtlasEntry> regions, SharedComponentsAtlas sharedComponents, DependencyGraphAtlas dependencies)
    {
        var entries = new List<SearchIndexEntryAtlas>();
        entries.AddRange(semanticModel.Nodes.Select(node => new SearchIndexEntryAtlas
        {
            Type = node.SemanticType,
            Name = node.GetProperty("name") ?? node.GetProperty("title") ?? node.Identifier,
            Location = node.SourceFile,
            Keywords = node.ReferencedObjects.Concat(node.Properties.Select(pair => $"{pair.Key}:{pair.Value}")).ToList(),
        }));

        return new SearchIndexAtlas
        {
            Entries = entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .GroupBy(entry => $"{entry.Type}|{entry.Name}|{entry.Location}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(entry => entry.Type, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    private static PageComponentAtlasEntry BuildPageComponentFromSemanticNode(OracleApexSemanticNode node)
        => new()
        {
            Name = node.GetProperty("name") ?? node.Identifier,
            Type = node.GetProperty("type") ?? node.SemanticType,
        };

    private static int? ReadInt(string? value)
        => int.TryParse(value, out var parsed) ? parsed : null;

    private static bool IsDatabaseObject(string value)
        => !string.IsNullOrWhiteSpace(value) && !IsRestReference(value) && value.Any(char.IsUpper) && value.Contains('.', StringComparison.Ordinal) || value.EndsWith("_V", StringComparison.OrdinalIgnoreCase) || value.All(character => char.IsLetterOrDigit(character) || character is '_' or '.' or '$' or '#');

    private static bool IsRestReference(string value)
        => value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("/ords/", StringComparison.OrdinalIgnoreCase);

    private static ApplicationAtlasEntry BuildApplication(ParsedNode applicationNode, OracleApexEnvironmentPreferences environment, string environmentName)
    {
        return new ApplicationAtlasEntry
        {
            Id = ReadInt(applicationNode, "id") ?? environment.ApplicationId ?? 0,
            Name = ReadString(applicationNode, "name") ?? applicationNode.Name ?? string.Empty,
            Alias = ReadString(applicationNode, "alias") ?? string.Empty,
            Version = ReadString(applicationNode, "version") ?? string.Empty,
            Workspace = ReadString(applicationNode, "workspace") ?? environment.Workspace ?? string.Empty,
            ParsingSchema = ReadString(applicationNode, "parsing-schema", "parsing_schema", "schema") ?? environment.ParsingSchema ?? string.Empty,
            EnvironmentName = environmentName,
        };
    }

    private static PageAtlasEntry BuildPage(string filePath, string sourcePath)
    {
        var pageNode = ParseFirstRootNode(filePath, "page");
        var fileName = Path.GetFileName(filePath);
        var pageMatch = PageFilePattern.Match(fileName);
        var pageId = ReadInt(pageNode, "id") ?? (pageMatch.Success ? int.Parse(pageMatch.Groups["id"].Value) : 0);
        var pageAlias = ReadString(pageNode, "alias") ?? (pageMatch.Success ? pageMatch.Groups["alias"].Value.Replace('-', ' ').ToUpperInvariant().Replace(" ", "-", StringComparison.Ordinal) : string.Empty);
        var pageName = ReadString(pageNode, "name") ?? pageNode.Name ?? fileName;

        var regions = pageNode.Children.Where(child => child.Type == "region").Select(child => BuildRegion(pageId, pageName, child)).ToList();
        var items = pageNode.Children.Where(child => child.Type == "item").Select(BuildPageComponent).ToList();
        var buttons = pageNode.Children.Where(child => child.Type == "button").Select(BuildPageComponent).ToList();
        var dynamicActions = pageNode.Children.Where(child => child.Type == "dynamic-action").Select(BuildPageComponent).ToList();
        var processes = pageNode.Children.Where(child => child.Type == "process").Select(BuildPageComponent).ToList();
        var branches = pageNode.Children.Where(child => child.Type == "branch").Select(BuildBranch).ToList();

        return new PageAtlasEntry
        {
            PageId = pageId,
            Name = pageName,
            Alias = pageAlias,
            Mode = ReadString(pageNode, "mode", "page-mode") ?? string.Empty,
            Authentication = ReadString(pageNode, "authentication", "authentication-scheme") ?? string.Empty,
            SourceFile = Path.GetRelativePath(sourcePath, filePath).Replace('\\', '/'),
            Regions = regions,
            Items = items,
            Buttons = buttons,
            DynamicActions = dynamicActions,
            Processes = processes,
            Branches = branches,
            Breadcrumb = ReadString(pageNode, "breadcrumb", "breadcrumb-name") ?? string.Empty,
            ParentPageId = ReadInt(pageNode, "parent-page", "parent-page-id"),
        };
    }

    private static RegionAtlasEntry BuildRegion(int pageId, string pageName, ParsedNode regionNode)
    {
        var regionText = string.Join("\n", regionNode.TextLines);
        var referencedObjects = ExtractDatabaseObjects(regionText, regionNode).ToList();
        var restSources = ExtractRestEndpoints(regionText, regionNode).ToList();
        return new RegionAtlasEntry
        {
            PageId = pageId,
            PageName = pageName,
            Name = ReadString(regionNode, "name") ?? regionNode.Name ?? string.Empty,
            Title = ReadString(regionNode, "title", "name") ?? regionNode.Name ?? string.Empty,
            RegionType = ReadString(regionNode, "type", "region-type") ?? string.Empty,
            SourceType = ReadString(regionNode, "source-type", "sourceType", "source-type-code") ?? string.Empty,
            ReferencedTablesOrViews = referencedObjects,
            ReferencedRestSources = restSources,
        };
    }

    private static PageComponentAtlasEntry BuildPageComponent(ParsedNode node)
        => new()
        {
            Name = ReadString(node, "name") ?? node.Name ?? string.Empty,
            Type = ReadString(node, "type") ?? node.Type,
        };

    private static BranchAtlasEntry BuildBranch(ParsedNode node)
        => new()
        {
            Name = ReadString(node, "name") ?? node.Name ?? string.Empty,
            TargetPageId = ReadInt(node, "target-page", "target-page-id"),
        };

    private static SharedComponentsAtlas BuildSharedComponents(string sourcePath)
    {
        var root = Path.Combine(sourcePath, "shared_components");
        if (!Directory.Exists(root))
        {
            return new SharedComponentsAtlas();
        }

        var components = Directory.GetFiles(root, "*.apx", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => BuildSharedComponent(root, path))
            .ToList();

        return new SharedComponentsAtlas
        {
            Lovs = components.Where(component => component.Category == "lovs").ToList(),
            Lists = components.Where(component => component.Category == "lists").ToList(),
            NavigationMenus = components.Where(component => component.Category == "navigation-menus").ToList(),
            AuthorizationSchemes = components.Where(component => component.Category == "authorization-schemes").ToList(),
            AuthenticationSchemes = components.Where(component => component.Category == "authentication-schemes").ToList(),
            BuildOptions = components.Where(component => component.Category == "build-options").ToList(),
            StaticFiles = components.Where(component => component.Category == "static-files").ToList(),
            Plugins = components.Where(component => component.Category == "plugins").ToList(),
        };
    }

    private static SharedComponentAtlasEntry BuildSharedComponent(string sharedComponentsRoot, string filePath)
    {
        var roots = ParseFile(filePath);
        var rootNode = roots.FirstOrDefault();
        var relativePath = Path.GetRelativePath(sharedComponentsRoot, filePath).Replace('\\', '/');
        var categorySegment = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var category = NormalizeSharedCategory(categorySegment);
        return new SharedComponentAtlasEntry
        {
            Category = category,
            Name = rootNode is null ? Path.GetFileNameWithoutExtension(filePath) : (ReadString(rootNode, "name", "title") ?? rootNode.Name ?? Path.GetFileNameWithoutExtension(filePath)),
            Type = rootNode?.Type ?? category,
            SourceFile = relativePath,
            PageTargets = ExtractIntegerProperties(rootNode, "target-page", "target-page-id"),
            ChildEntries = rootNode?.Children.Select(child => new SharedComponentChildEntry
            {
                Name = ReadString(child, "label", "name") ?? child.Name ?? string.Empty,
                TargetPageId = ReadInt(child, "target-page", "target-page-id"),
                ParentEntry = ReadString(child, "parent-entry") ?? string.Empty,
            }).ToList() ?? new List<SharedComponentChildEntry>(),
        };
    }

    private static NavigationAtlas BuildNavigation(IReadOnlyList<PageAtlasEntry> pages, SharedComponentsAtlas sharedComponents)
    {
        return new NavigationAtlas
        {
            Menus = sharedComponents.NavigationMenus.Select(menu => new NavigationMenuAtlasEntry
            {
                Name = menu.Name,
                SourceFile = menu.SourceFile,
                Entries = menu.ChildEntries,
            }).ToList(),
            Breadcrumbs = pages.Where(page => !string.IsNullOrWhiteSpace(page.Breadcrumb)).Select(page => new BreadcrumbAtlasEntry
            {
                PageId = page.PageId,
                PageName = page.Name,
                Breadcrumb = page.Breadcrumb,
            }).ToList(),
            PageHierarchy = pages.Select(page => new PageHierarchyAtlasEntry
            {
                PageId = page.PageId,
                PageName = page.Name,
                ParentPageId = page.ParentPageId,
            }).ToList(),
        };
    }

    private static DependencyGraphAtlas BuildDependencies(ApplicationAtlasEntry application, IReadOnlyList<PageAtlasEntry> pages, SharedComponentsAtlas sharedComponents)
    {
        var nodes = new Dictionary<string, DependencyNodeAtlasEntry>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<DependencyEdgeAtlasEntry>();
        var applicationNodeId = $"application:{application.Id}";
        AddNode(nodes, new DependencyNodeAtlasEntry { Id = applicationNodeId, Type = "application", Name = application.Name });

        foreach (var page in pages)
        {
            var pageNodeId = $"page:{page.PageId}";
            AddNode(nodes, new DependencyNodeAtlasEntry { Id = pageNodeId, Type = "page", Name = page.Name, ParentId = applicationNodeId });
            edges.Add(new DependencyEdgeAtlasEntry { From = applicationNodeId, To = pageNodeId, Relationship = "contains" });

            foreach (var region in page.Regions)
            {
                var regionNodeId = $"page:{page.PageId}:region:{Slugify(region.Title)}";
                AddNode(nodes, new DependencyNodeAtlasEntry { Id = regionNodeId, Type = "region", Name = region.Title, ParentId = pageNodeId });
                edges.Add(new DependencyEdgeAtlasEntry { From = pageNodeId, To = regionNodeId, Relationship = "contains" });

                foreach (var databaseObject in region.ReferencedTablesOrViews)
                {
                    var databaseNodeId = $"database:{databaseObject}";
                    AddNode(nodes, new DependencyNodeAtlasEntry { Id = databaseNodeId, Type = ClassifyDatabaseObject(databaseObject), Name = databaseObject });
                    edges.Add(new DependencyEdgeAtlasEntry { From = regionNodeId, To = databaseNodeId, Relationship = "references" });
                }

                foreach (var restSource in region.ReferencedRestSources)
                {
                    var restNodeId = $"rest:{restSource}";
                    AddNode(nodes, new DependencyNodeAtlasEntry { Id = restNodeId, Type = "rest-endpoint", Name = restSource });
                    edges.Add(new DependencyEdgeAtlasEntry { From = regionNodeId, To = restNodeId, Relationship = "references" });
                }
            }

            AddPageComponentDependencies(pageNodeId, "item", page.Items, nodes, edges);
            AddPageComponentDependencies(pageNodeId, "button", page.Buttons, nodes, edges);
            AddPageComponentDependencies(pageNodeId, "dynamic-action", page.DynamicActions, nodes, edges);

            foreach (var process in page.Processes)
            {
                var processNodeId = $"page:{page.PageId}:process:{Slugify(process.Name)}";
                AddNode(nodes, new DependencyNodeAtlasEntry { Id = processNodeId, Type = "process", Name = process.Name, ParentId = pageNodeId });
                edges.Add(new DependencyEdgeAtlasEntry { From = pageNodeId, To = processNodeId, Relationship = "contains" });

                foreach (var identifier in ExtractPlSqlIdentifiers(process.Name))
                {
                    var identifierNodeId = $"plsql:{identifier}";
                    AddNode(nodes, new DependencyNodeAtlasEntry { Id = identifierNodeId, Type = "plsql-identifier", Name = identifier });
                    edges.Add(new DependencyEdgeAtlasEntry { From = processNodeId, To = identifierNodeId, Relationship = "invokes" });
                }
            }
        }

        foreach (var component in EnumerateSharedComponents(sharedComponents))
        {
            var componentNodeId = $"shared:{component.Category}:{Slugify(component.Name)}";
            AddNode(nodes, new DependencyNodeAtlasEntry { Id = componentNodeId, Type = "shared-component", Name = component.Name, ParentId = applicationNodeId });
            edges.Add(new DependencyEdgeAtlasEntry { From = applicationNodeId, To = componentNodeId, Relationship = "contains" });

            foreach (var targetPageId in component.PageTargets.Distinct())
            {
                var pageNodeId = $"page:{targetPageId}";
                if (nodes.ContainsKey(pageNodeId))
                {
                    edges.Add(new DependencyEdgeAtlasEntry { From = componentNodeId, To = pageNodeId, Relationship = "targets" });
                }
            }
        }

        return new DependencyGraphAtlas
        {
            Nodes = nodes.Values.OrderBy(node => node.Type, StringComparer.OrdinalIgnoreCase).ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            Edges = edges
                .GroupBy(edge => $"{edge.From}|{edge.To}|{edge.Relationship}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(edge => edge.From, StringComparer.OrdinalIgnoreCase)
                .ThenBy(edge => edge.To, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    private static SearchIndexAtlas BuildSearchIndex(IReadOnlyList<PageAtlasEntry> pages, IReadOnlyList<RegionAtlasEntry> regions, SharedComponentsAtlas sharedComponents, DependencyGraphAtlas dependencies)
    {
        var entries = new List<SearchIndexEntryAtlas>();

        entries.AddRange(pages.Select(page => new SearchIndexEntryAtlas { Type = "page", Name = page.Name, Location = $"pages/{page.PageId}", Keywords = [page.Alias] }));
        entries.AddRange(regions.Select(region => new SearchIndexEntryAtlas { Type = "region", Name = region.Title, Location = $"pages/{region.PageId}/regions/{Slugify(region.Title)}", Keywords = [region.RegionType, region.SourceType] }));
        entries.AddRange(pages.SelectMany(page => page.Items.Select(item => new SearchIndexEntryAtlas { Type = "item", Name = item.Name, Location = $"pages/{page.PageId}/items/{Slugify(item.Name)}" })));
        entries.AddRange(pages.SelectMany(page => page.Processes.Select(process => new SearchIndexEntryAtlas { Type = "process", Name = process.Name, Location = $"pages/{page.PageId}/processes/{Slugify(process.Name)}" })));
        entries.AddRange(sharedComponents.AuthorizationSchemes.Select(item => new SearchIndexEntryAtlas { Type = "authorization-scheme", Name = item.Name, Location = item.SourceFile }));
        entries.AddRange(dependencies.Nodes.Where(node => node.Type is "table" or "view").Select(node => new SearchIndexEntryAtlas { Type = node.Type, Name = node.Name, Location = "dependencies" }));
        entries.AddRange(dependencies.Nodes.Where(node => node.Type == "rest-endpoint").Select(node => new SearchIndexEntryAtlas { Type = "rest-endpoint", Name = node.Name, Location = "dependencies" }));
        entries.AddRange(dependencies.Nodes.Where(node => node.Type == "plsql-identifier").Select(node => new SearchIndexEntryAtlas { Type = "plsql-identifier", Name = node.Name, Location = "dependencies" }));

        return new SearchIndexAtlas
        {
            Entries = entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .GroupBy(entry => $"{entry.Type}|{entry.Name}|{entry.Location}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(entry => entry.Type, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    private static string BuildDocumentation(AtlasDocument atlas, OracleApexWorkspaceIndex workspaceIndex, string atlasVersion)
    {
        var workspaceSummary = BuiltInLanguageReferenceAnalyzer.BuildWorkspaceSummary(
            workspaceIndex,
            atlasVersion,
            ".opencode/knowledge/apexlang-language-reference/language-reference-diff.json",
            ".opencode/knowledge/apexlang-language-reference/docs/language-reference-diff.md");
        var lines = new List<string>
        {
            "# Oracle APEX Atlas",
            string.Empty,
            "## Application Summary",
            string.Empty,
            $"- Application: {atlas.Application.Name} ({atlas.Application.Id})",
            $"- Alias: {DefaultIfEmpty(atlas.Application.Alias)}",
            $"- Version: {DefaultIfEmpty(atlas.Application.Version)}",
            $"- Workspace: {DefaultIfEmpty(atlas.Application.Workspace)}",
            $"- Parsing schema: {DefaultIfEmpty(atlas.Application.ParsingSchema)}",
            string.Empty,
            "## Page Inventory",
            string.Empty,
        };

        lines.AddRange(atlas.Pages.Count == 0
            ? ["- No pages were discovered."]
            : atlas.Pages.Select(page => $"- {page.PageId}: {page.Name} [{page.Alias}] regions={page.Regions.Count} items={page.Items.Count} buttons={page.Buttons.Count} processes={page.Processes.Count} dynamic-actions={page.DynamicActions.Count}"));

        lines.Add(string.Empty);
        lines.Add("## Navigation Tree");
        lines.Add(string.Empty);
        lines.AddRange(atlas.Navigation.PageHierarchy.Count == 0
            ? ["- No page hierarchy was discovered."]
            : atlas.Navigation.PageHierarchy.Select(entry => $"- Page {entry.PageId}: {entry.PageName} parent={entry.ParentPageId?.ToString() ?? "none"}"));

        if (atlas.Navigation.Menus.Count > 0)
        {
            lines.AddRange(atlas.Navigation.Menus.Select(menu => $"- Menu {menu.Name}: {string.Join(", ", menu.Entries.Select(entry => $"{entry.Name}->{entry.TargetPageId?.ToString() ?? "?"}"))}"));
        }

        lines.Add(string.Empty);
        lines.Add("## Shared Component Summary");
        lines.Add(string.Empty);
        lines.Add($"- LOVs: {atlas.SharedComponents.Lovs.Count}");
        lines.Add($"- Lists: {atlas.SharedComponents.Lists.Count}");
        lines.Add($"- Navigation Menus: {atlas.SharedComponents.NavigationMenus.Count}");
        lines.Add($"- Authorization Schemes: {atlas.SharedComponents.AuthorizationSchemes.Count}");
        lines.Add($"- Authentication Schemes: {atlas.SharedComponents.AuthenticationSchemes.Count}");
        lines.Add($"- Build Options: {atlas.SharedComponents.BuildOptions.Count}");
        lines.Add($"- Static Files: {atlas.SharedComponents.StaticFiles.Count}");
        lines.Add($"- Plug-ins: {atlas.SharedComponents.Plugins.Count}");
        lines.Add(string.Empty);
        lines.Add("## Dependency Overview");
        lines.Add(string.Empty);
        lines.Add($"- Nodes: {atlas.Dependencies.Nodes.Count}");
        lines.Add($"- Edges: {atlas.Dependencies.Edges.Count}");
        lines.Add($"- Database objects: {atlas.Dependencies.Nodes.Count(node => node.Type is "table" or "view")}");
        lines.Add($"- REST endpoints: {atlas.Dependencies.Nodes.Count(node => node.Type == "rest-endpoint")}");
        lines.Add($"- PL/SQL identifiers: {atlas.Dependencies.Nodes.Count(node => node.Type == "plsql-identifier")}");
        lines.Add(string.Empty);
        lines.Add("## APEXlang Version Compatibility");
        lines.Add(string.Empty);
        lines.Add($"- Current reference version: {BuiltInLanguageReferenceDiff.ToApexVersion}");
        lines.Add($"- Previous reference version: {BuiltInLanguageReferenceDiff.FromApexVersion}");
        lines.Add($"- Atlas version: {ValueOrUnknown(workspaceSummary.AtlasVersion)}");
        lines.Add($"- Workspace status: {workspaceSummary.Status}");
        lines.Add($"- Diff counts: components={CountDiffKinds("component-")}; relationships={CountDiffKinds("parent-") + CountDiffKinds("child-")}; properties={CountPropertyDiffs()}; documentation={CountDiffKinds("documentation-") + CountDiffKinds("canonical-example-")}; atlas-compatibility={BuiltInLanguageReferenceDiff.AtlasCompatibility.CurrentWarningCount}");
        if (workspaceSummary.Findings.Count > 0)
        {
            foreach (var finding in workspaceSummary.Findings.Take(5))
            {
                lines.Add($"- Workspace impact: {finding.Message}");
            }
        }
        else
        {
            lines.Add("- Workspace impact: none");
        }
        lines.Add($"- Detailed diff Markdown: {workspaceSummary.DiffMarkdownPath}");
        lines.Add($"- Detailed diff JSON: {workspaceSummary.DiffJsonPath}");
        lines.Add(string.Empty);
        lines.Add("Generated from exported APEXlang under `.opencode/knowledge/apexlang-atlas/`.");
        return string.Join("\n", lines) + "\n";
    }

    private static int CountDiffKinds(string prefix)
        => BuiltInLanguageReferenceDiff.Differences.Count(item => item.Kind.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static int CountPropertyDiffs()
        => BuiltInLanguageReferenceDiff.Differences.Count(item => item.Kind.StartsWith("property-", StringComparison.OrdinalIgnoreCase));

    private static string ResolveAtlasVersion(WorkspacePaths paths)
    {
        var workspaceMetadataPath = Path.Combine(paths.OpencodePath, "apexlang", "source", "apexlang_meta_data.json");
        if (TryReadAtlasBuildId(workspaceMetadataPath, out var buildId))
        {
            return buildId;
        }

        var cacheRoot = Path.Combine(paths.OpencodePath, "cache", "apexlang");
        if (Directory.Exists(cacheRoot))
        {
            foreach (var metadataPath in Directory.GetFiles(cacheRoot, "apexlang_meta_data.json", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (TryReadAtlasBuildId(metadataPath, out buildId))
                {
                    return buildId;
                }
            }
        }

        return string.Empty;
    }

    private static bool TryReadAtlasBuildId(string metadataPath, out string buildId)
    {
        buildId = string.Empty;
        if (!File.Exists(metadataPath))
        {
            return false;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
        if (document.RootElement.TryGetProperty("buildID", out var buildIdElement) || document.RootElement.TryGetProperty("buildId", out buildIdElement))
        {
            buildId = buildIdElement.GetString() ?? string.Empty;
        }

        return !string.IsNullOrWhiteSpace(buildId);
    }

    private static string ValueOrUnknown(string value)
        => string.IsNullOrWhiteSpace(value) ? "unknown" : value;

    private static IReadOnlyList<ParsedNode> ParseFile(string filePath)
    {
        var roots = new List<ParsedNode>();
        var stack = new Stack<ParsedNode>();

        foreach (var rawLine in File.ReadAllLines(filePath))
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (trimmed == ")")
            {
                if (stack.Count > 0)
                {
                    stack.Pop();
                }

                continue;
            }

            if (TryParseBlockStart(trimmed, out var blockType, out var blockName))
            {
                var node = new ParsedNode
                {
                    Type = NormalizeToken(blockType),
                    Name = CleanValue(blockName),
                };

                if (stack.Count == 0)
                {
                    roots.Add(node);
                }
                else
                {
                    stack.Peek().Children.Add(node);
                }

                stack.Push(node);
                continue;
            }

            if (stack.Count == 0)
            {
                continue;
            }

            stack.Peek().TextLines.Add(trimmed);
            var propertyMatch = PropertyPattern.Match(trimmed);
            if (propertyMatch.Success)
            {
                stack.Peek().Properties[NormalizeToken(propertyMatch.Groups["key"].Value)] = CleanValue(propertyMatch.Groups["value"].Value);
            }
        }

        return roots;
    }

    private static string? ReadString(ParsedNode node, params string[] keys)
    {
        foreach (var key in keys.Select(NormalizeToken))
        {
            if (node.Properties.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static int? ReadInt(ParsedNode node, params string[] keys)
    {
        var value = ReadString(node, keys);
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static IReadOnlyList<int> ExtractIntegerProperties(ParsedNode? node, params string[] keys)
    {
        if (node is null)
        {
            return Array.Empty<int>();
        }

        return node.Children.Select(child => ReadInt(child, keys)).Where(value => value.HasValue).Select(value => value!.Value).Distinct().ToList();
    }

    private static IEnumerable<string> ExtractDatabaseObjects(string text, ParsedNode node)
    {
        var objects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in DatabaseReferencePattern.Matches(text))
        {
            objects.Add(match.Groups[1].Value.ToUpperInvariant());
        }

        foreach (var key in new[] { "table", "tables", "view", "views", "source-table", "source-view" })
        {
            var value = ReadString(node, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                foreach (var segment in value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    objects.Add(segment.ToUpperInvariant());
                }
            }
        }

        return objects.OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ExtractRestEndpoints(string text, ParsedNode node)
    {
        var endpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in RestEndpointPattern.Matches(text))
        {
            var value = match.Value.Trim();
            if (value.StartsWith("/ords/", StringComparison.OrdinalIgnoreCase) || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                endpoints.Add(value);
            }
        }

        foreach (var key in new[] { "rest-source", "rest-source-url", "endpoint", "url" })
        {
            var value = ReadString(node, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                endpoints.Add(value);
            }
        }

        return endpoints.OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ExtractPlSqlIdentifiers(string text)
    {
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in PlSqlQualifiedIdentifierPattern.Matches(text.ToUpperInvariant()))
        {
            var identifier = $"{match.Groups[1].Value}.{match.Groups[2].Value}";
            var root = match.Groups[1].Value;
            if (!SqlKeywords.Contains(root))
            {
                identifiers.Add(identifier);
            }
        }

        return identifiers.OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<SharedComponentAtlasEntry> EnumerateSharedComponents(SharedComponentsAtlas sharedComponents)
        => sharedComponents.Lovs
            .Concat(sharedComponents.Lists)
            .Concat(sharedComponents.NavigationMenus)
            .Concat(sharedComponents.AuthorizationSchemes)
            .Concat(sharedComponents.AuthenticationSchemes)
            .Concat(sharedComponents.BuildOptions)
            .Concat(sharedComponents.StaticFiles)
            .Concat(sharedComponents.Plugins);

    private static void AddPageComponentDependencies(string pageNodeId, string type, IReadOnlyList<PageComponentAtlasEntry> components, IDictionary<string, DependencyNodeAtlasEntry> nodes, ICollection<DependencyEdgeAtlasEntry> edges)
    {
        foreach (var component in components)
        {
            var componentNodeId = $"{pageNodeId}:{type}:{Slugify(component.Name)}";
            AddNode(nodes, new DependencyNodeAtlasEntry { Id = componentNodeId, Type = type, Name = component.Name, ParentId = pageNodeId });
            edges.Add(new DependencyEdgeAtlasEntry { From = pageNodeId, To = componentNodeId, Relationship = "contains" });
        }
    }

    private static void AddNode(IDictionary<string, DependencyNodeAtlasEntry> nodes, DependencyNodeAtlasEntry node)
    {
        if (!nodes.ContainsKey(node.Id))
        {
            nodes[node.Id] = node;
        }
    }

    private static string NormalizeSharedCategory(string value)
        => NormalizeToken(value) switch
        {
            "navigation-menus" or "navigation-menu" => "navigation-menus",
            "authorization-schemes" or "authorization-scheme" => "authorization-schemes",
            "authentication-schemes" or "authentication-scheme" => "authentication-schemes",
            "build-options" or "build-option" => "build-options",
            "static-files" or "static-file" => "static-files",
            "plugins" or "plug-ins" or "plugin" => "plugins",
            _ => NormalizeToken(value),
        };

    private static string NormalizeToken(string value)
        => value.Trim().Replace('_', '-').Replace(' ', '-').ToLowerInvariant();

    private static bool TryParseBlockStart(string line, out string type, out string name)
    {
        type = string.Empty;
        name = string.Empty;
        if (!line.EndsWith('('))
        {
            return false;
        }

        var content = line[..^1].TrimEnd();
        foreach (var knownType in KnownBlockTypes)
        {
            if (content.Equals(knownType, StringComparison.OrdinalIgnoreCase))
            {
                type = knownType;
                return true;
            }

            if (content.StartsWith(knownType + " ", StringComparison.OrdinalIgnoreCase))
            {
                type = knownType;
                name = content[(knownType.Length + 1)..].Trim();
                return true;
            }
        }

        var firstSpace = content.IndexOf(' ');
        if (firstSpace <= 0)
        {
            return false;
        }

        type = content[..firstSpace].Trim();
        name = content[(firstSpace + 1)..].Trim();
        return true;
    }

    private static string CleanValue(string value)
        => value.Trim().Trim('"', '\'');

    private static string NormalizeContent(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static string Slugify(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "unnamed"
            : string.Concat(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-')).Trim('-');

    private static string DefaultIfEmpty(string value)
        => string.IsNullOrWhiteSpace(value) ? "unknown" : value;

    private static string ClassifyDatabaseObject(string name)
        => name.EndsWith("_V", StringComparison.OrdinalIgnoreCase) || name.Contains(".V", StringComparison.OrdinalIgnoreCase) ? "view" : "table";

    private sealed class ParsedNode
    {
        public string Type { get; init; } = string.Empty;
        public string? Name { get; init; }
        public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> TextLines { get; } = [];
        public List<ParsedNode> Children { get; } = [];
    }

    private sealed class AtlasDocument
    {
        public ApplicationAtlasEntry Application { get; init; } = new();
        public IReadOnlyList<PageAtlasEntry> Pages { get; init; } = Array.Empty<PageAtlasEntry>();
        public IReadOnlyList<RegionAtlasEntry> Regions { get; init; } = Array.Empty<RegionAtlasEntry>();
        public NavigationAtlas Navigation { get; init; } = new();
        public SharedComponentsAtlas SharedComponents { get; init; } = new();
        public DependencyGraphAtlas Dependencies { get; init; } = new();
        public SearchIndexAtlas SearchIndex { get; init; } = new();
        public AtlasBuildSummary Build { get; init; } = new();
    }

    private sealed class AtlasBuildSummary
    {
        public string EnvironmentName { get; init; } = string.Empty;
        public string SourcePath { get; init; } = string.Empty;
        public string SourceHash { get; init; } = string.Empty;
        public DateTimeOffset GeneratedUtc { get; init; }
    }

    private sealed class AtlasState
    {
        public string Status { get; init; } = string.Empty;
        public string EnvironmentName { get; init; } = string.Empty;
        public string SourcePath { get; init; } = string.Empty;
        public string SourceHash { get; init; } = string.Empty;
        public DateTimeOffset BuiltUtc { get; init; }
        public int ApplicationId { get; init; }
        public string ApplicationName { get; init; } = string.Empty;
        public IReadOnlyList<string> GeneratedFiles { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> DeploymentProfiles { get; init; } = Array.Empty<string>();
        public string DocumentationPath { get; init; } = string.Empty;
        public string Error { get; init; } = string.Empty;
    }

    private sealed class ApplicationAtlasEntry
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Alias { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string Workspace { get; init; } = string.Empty;
        public string ParsingSchema { get; init; } = string.Empty;
        public string EnvironmentName { get; init; } = string.Empty;
    }

    private sealed class PageAtlasEntry
    {
        public int PageId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Alias { get; init; } = string.Empty;
        public string Mode { get; init; } = string.Empty;
        public string Authentication { get; init; } = string.Empty;
        public string SourceFile { get; init; } = string.Empty;
        public IReadOnlyList<RegionAtlasEntry> Regions { get; init; } = Array.Empty<RegionAtlasEntry>();
        public IReadOnlyList<PageComponentAtlasEntry> Items { get; init; } = Array.Empty<PageComponentAtlasEntry>();
        public IReadOnlyList<PageComponentAtlasEntry> Buttons { get; init; } = Array.Empty<PageComponentAtlasEntry>();
        public IReadOnlyList<PageComponentAtlasEntry> DynamicActions { get; init; } = Array.Empty<PageComponentAtlasEntry>();
        public IReadOnlyList<PageComponentAtlasEntry> Processes { get; init; } = Array.Empty<PageComponentAtlasEntry>();
        public IReadOnlyList<BranchAtlasEntry> Branches { get; init; } = Array.Empty<BranchAtlasEntry>();
        public string Breadcrumb { get; init; } = string.Empty;
        public int? ParentPageId { get; init; }
    }

    private sealed class PageComponentAtlasEntry
    {
        public string Name { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
    }

    private sealed class BranchAtlasEntry
    {
        public string Name { get; init; } = string.Empty;
        public int? TargetPageId { get; init; }
    }

    private sealed class RegionAtlasEntry
    {
        public int PageId { get; init; }
        public string PageName { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string RegionType { get; init; } = string.Empty;
        public string SourceType { get; init; } = string.Empty;
        public IReadOnlyList<string> ReferencedTablesOrViews { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> ReferencedRestSources { get; init; } = Array.Empty<string>();
    }

    private sealed class SharedComponentsAtlas
    {
        public IReadOnlyList<SharedComponentAtlasEntry> Lovs { get; init; } = Array.Empty<SharedComponentAtlasEntry>();
        public IReadOnlyList<SharedComponentAtlasEntry> Lists { get; init; } = Array.Empty<SharedComponentAtlasEntry>();
        public IReadOnlyList<SharedComponentAtlasEntry> NavigationMenus { get; init; } = Array.Empty<SharedComponentAtlasEntry>();
        public IReadOnlyList<SharedComponentAtlasEntry> AuthorizationSchemes { get; init; } = Array.Empty<SharedComponentAtlasEntry>();
        public IReadOnlyList<SharedComponentAtlasEntry> AuthenticationSchemes { get; init; } = Array.Empty<SharedComponentAtlasEntry>();
        public IReadOnlyList<SharedComponentAtlasEntry> BuildOptions { get; init; } = Array.Empty<SharedComponentAtlasEntry>();
        public IReadOnlyList<SharedComponentAtlasEntry> StaticFiles { get; init; } = Array.Empty<SharedComponentAtlasEntry>();
        public IReadOnlyList<SharedComponentAtlasEntry> Plugins { get; init; } = Array.Empty<SharedComponentAtlasEntry>();
    }

    private sealed class SharedComponentAtlasEntry
    {
        public string Category { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public string SourceFile { get; init; } = string.Empty;
        public IReadOnlyList<int> PageTargets { get; init; } = Array.Empty<int>();
        public IReadOnlyList<SharedComponentChildEntry> ChildEntries { get; init; } = Array.Empty<SharedComponentChildEntry>();
    }

    private sealed class SharedComponentChildEntry
    {
        public string Name { get; init; } = string.Empty;
        public int? TargetPageId { get; init; }
        public string ParentEntry { get; init; } = string.Empty;
    }

    private sealed class NavigationAtlas
    {
        public IReadOnlyList<NavigationMenuAtlasEntry> Menus { get; init; } = Array.Empty<NavigationMenuAtlasEntry>();
        public IReadOnlyList<BreadcrumbAtlasEntry> Breadcrumbs { get; init; } = Array.Empty<BreadcrumbAtlasEntry>();
        public IReadOnlyList<PageHierarchyAtlasEntry> PageHierarchy { get; init; } = Array.Empty<PageHierarchyAtlasEntry>();
    }

    private sealed class NavigationMenuAtlasEntry
    {
        public string Name { get; init; } = string.Empty;
        public string SourceFile { get; init; } = string.Empty;
        public IReadOnlyList<SharedComponentChildEntry> Entries { get; init; } = Array.Empty<SharedComponentChildEntry>();
    }

    private sealed class BreadcrumbAtlasEntry
    {
        public int PageId { get; init; }
        public string PageName { get; init; } = string.Empty;
        public string Breadcrumb { get; init; } = string.Empty;
    }

    private sealed class PageHierarchyAtlasEntry
    {
        public int PageId { get; init; }
        public string PageName { get; init; } = string.Empty;
        public int? ParentPageId { get; init; }
    }

    private sealed class DependencyGraphAtlas
    {
        public IReadOnlyList<DependencyNodeAtlasEntry> Nodes { get; init; } = Array.Empty<DependencyNodeAtlasEntry>();
        public IReadOnlyList<DependencyEdgeAtlasEntry> Edges { get; init; } = Array.Empty<DependencyEdgeAtlasEntry>();
    }

    private sealed class DependencyNodeAtlasEntry
    {
        public string Id { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string ParentId { get; init; } = string.Empty;
    }

    private sealed class DependencyEdgeAtlasEntry
    {
        public string From { get; init; } = string.Empty;
        public string To { get; init; } = string.Empty;
        public string Relationship { get; init; } = string.Empty;
    }

    private sealed class SearchIndexAtlas
    {
        public IReadOnlyList<SearchIndexEntryAtlas> Entries { get; init; } = Array.Empty<SearchIndexEntryAtlas>();
    }

    private sealed class SearchIndexEntryAtlas
    {
        public string Type { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Location { get; init; } = string.Empty;
        public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
    }
}

public sealed class OracleApexAtlasBuildResult
{
    public bool IsSuccess { get; init; }
    public bool IsSkipped { get; init; }
    public string Message { get; init; } = string.Empty;
    public string AtlasRootPath { get; init; } = string.Empty;
    public string DocumentationPath { get; init; } = string.Empty;
    public string SourceHash { get; init; } = string.Empty;

    public static OracleApexAtlasBuildResult Success(string atlasRootPath, string documentationPath, string sourceHash)
        => new() { IsSuccess = true, Message = "Oracle APEX Atlas rebuilt.", AtlasRootPath = atlasRootPath, DocumentationPath = documentationPath, SourceHash = sourceHash };

    public static OracleApexAtlasBuildResult Skipped(string message, string atlasRootPath = "", string documentationPath = "", string sourceHash = "")
        => new() { IsSuccess = true, IsSkipped = true, Message = message, AtlasRootPath = atlasRootPath, DocumentationPath = documentationPath, SourceHash = sourceHash };

    public static OracleApexAtlasBuildResult Failed(string message, string atlasRootPath, string documentationPath, string sourceHash)
        => new() { Message = message, AtlasRootPath = atlasRootPath, DocumentationPath = documentationPath, SourceHash = sourceHash };
}
