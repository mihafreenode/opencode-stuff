using OpenCode.Workspace.Core.Models;
using System.Text.Json.Serialization;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class OracleApexWorkspaceIndexBuilder
{
    private static readonly OracleApexLanguageReferenceDiffReport BuiltInReferenceDiff = new OracleApexLanguageReferenceCatalogComparer().Compare(
        OracleApexBuiltInLanguageReference.CreatePrevious(),
        OracleApexBuiltInLanguageReference.Create(),
        OracleApexComponentCatalog.AtlasSeed.CompareWithReference(OracleApexBuiltInLanguageReference.CreatePrevious()),
        OracleApexComponentCatalog.AtlasSeed.CompareWithReference(OracleApexBuiltInLanguageReference.Create()));

    private readonly OracleApexSemanticModelBuilder _semanticModelBuilder;

    public OracleApexWorkspaceIndexBuilder(OracleApexSemanticModelBuilder? semanticModelBuilder = null)
        => _semanticModelBuilder = semanticModelBuilder ?? new OracleApexSemanticModelBuilder();

    public OracleApexWorkspaceIndex Build(string rootPath, OracleApexEnvironmentPreferences environment, string environmentName)
    {
        var sourcePath = Path.Combine(rootPath, (environment.SourcePath ?? "src/apex").Replace('/', Path.DirectorySeparatorChar));
        var semanticModel = _semanticModelBuilder.Build(sourcePath);
        var deploymentProfiles = _semanticModelBuilder.BuildDeploymentProfiles(sourcePath);
        var entries = semanticModel.Nodes.Select(BuildEntry).ToList();

        var sharedComponentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "authorization-scheme", "authentication-scheme", "navigation-menu", "navigation-entry", "list", "lov", "build-option", "static-file", "plugin",
        };

        var references = semanticModel.Nodes
            .SelectMany(node => node.ReferencedObjects.Select(reference => new OracleApexWorkspaceIndexReference
            {
                NodeId = node.NodeId,
                Identifier = node.Identifier,
                SemanticType = node.SemanticType,
                Reference = reference,
                SourceFile = node.SourceFile,
                Line = node.Line,
                Column = node.Column,
            }))
            .OrderBy(item => item.Reference, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceFile, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var locations = entries.Select(entry => new OracleApexWorkspaceIndexLocation
        {
            NodeId = entry.NodeId,
            SemanticType = entry.SemanticType,
            Identifier = entry.Identifier,
            SourceFile = entry.SourceFile,
            Line = entry.Line,
            Column = entry.Column,
            EndLine = entry.EndLine,
            EndColumn = entry.EndColumn,
        }).Concat(deploymentProfiles.Select(profile => new OracleApexWorkspaceIndexLocation
        {
            NodeId = $"deployment:{profile.SourceFile}:{profile.Name}",
            SemanticType = "deployment-profile",
            Identifier = profile.Name,
            SourceFile = profile.SourceFile,
            Line = profile.Line,
            Column = profile.Column,
            EndLine = profile.Line,
            EndColumn = profile.Column,
        })).OrderBy(item => item.SourceFile, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Line).ToList();

        var diagnostics = semanticModel.Diagnostics.Select(item => new OracleApexWorkspaceIndexDiagnostic
        {
            Severity = item.Severity.ToString(),
            Code = item.Code,
            Message = item.Message,
            SourceFile = item.SourceFile,
            Line = item.Line,
            Column = item.Column,
            NodeId = item.NodeId,
            SemanticType = item.SemanticType,
        }).Concat(deploymentProfiles.Where(profile => !profile.IsValid).Select(profile => new OracleApexWorkspaceIndexDiagnostic
        {
            Severity = OracleApexSemanticDiagnosticSeverity.Error.ToString(),
            Code = "invalid-deployment-profile",
            Message = profile.ValidationMessage,
            SourceFile = profile.SourceFile,
            Line = profile.Line,
            Column = profile.Column,
            NodeId = $"deployment:{profile.SourceFile}:{profile.Name}",
            SemanticType = "deployment-profile",
        })).Concat(new OracleApexLanguageReferenceWorkspaceImpactAnalyzer(BuiltInReferenceDiff).Analyze(semanticModel))
        .OrderBy(item => item.SourceFile, StringComparer.OrdinalIgnoreCase)
        .ThenBy(item => item.Line)
        .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
        .ToList();

        var searchEntries = entries.Select(entry => new OracleApexWorkspaceIndexSearchEntry
        {
            Type = entry.SemanticType,
            Name = entry.Identifier,
            SourceFile = entry.SourceFile,
            Keywords = entry.ReferencedObjects.Concat(entry.Properties.Select(pair => $"{pair.Key}:{pair.Value}")).ToList(),
        }).Concat(deploymentProfiles.Select(profile => new OracleApexWorkspaceIndexSearchEntry
        {
            Type = "deployment-profile",
            Name = profile.Name,
            SourceFile = profile.SourceFile,
            Keywords = profile.Properties.Select(pair => $"{pair.Key}:{pair.Value}").Concat(profile.ReferencedObjects).ToList(),
        })).OrderBy(item => item.Type, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();

        return new OracleApexWorkspaceIndex
        {
            EnvironmentName = environmentName,
            SourcePath = environment.SourcePath ?? "src/apex",
            SemanticModel = semanticModel,
            Entries = entries,
            Pages = entries.Where(entry => entry.SemanticType == "page").ToList(),
            Regions = entries.Where(entry => entry.SemanticType == "region").ToList(),
            Items = entries.Where(entry => entry.SemanticType == "item").ToList(),
            SharedComponents = entries.Where(entry => sharedComponentTypes.Contains(entry.SemanticType)).ToList(),
            NavigationEntries = entries.Where(entry => entry.SemanticType == "navigation-entry").ToList(),
            DeploymentProfiles = deploymentProfiles.Select(profile => new OracleApexWorkspaceDeploymentProfileIndexEntry
            {
                Name = profile.Name,
                SourceFile = profile.SourceFile,
                AbsolutePath = profile.AbsolutePath,
                Line = profile.Line,
                Column = profile.Column,
                Properties = profile.Properties,
                ReferencedObjects = profile.ReferencedObjects,
                IsValid = profile.IsValid,
                ValidationMessage = profile.ValidationMessage,
            }).ToList(),
            References = references,
            Diagnostics = diagnostics,
            SourceLocations = locations,
            SearchEntries = searchEntries,
        };
    }

    private static OracleApexWorkspaceIndexEntry BuildEntry(OracleApexSemanticNode node)
        => new()
        {
            NodeId = node.NodeId,
            SemanticType = node.SemanticType,
            Identifier = node.Identifier,
            SourceFile = node.SourceFile,
            Line = node.Line,
            Column = node.Column,
            EndLine = node.EndLine,
            EndColumn = node.EndColumn,
            ParentNodeId = node.Parent?.NodeId ?? string.Empty,
            ChildNodeIds = node.Children.Select(child => child.NodeId).ToList(),
            Properties = node.Properties,
            ReferencedObjects = node.ReferencedObjects,
        };
}

public sealed class OracleApexWorkspaceIndex
{
    public string EnvironmentName { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    [JsonIgnore]
    public OracleApexSemanticModel SemanticModel { get; init; } = new(null, Array.Empty<OracleApexSemanticNode>(), Array.Empty<OracleApexSemanticDiagnostic>());
    public IReadOnlyList<OracleApexWorkspaceIndexEntry> Entries { get; init; } = Array.Empty<OracleApexWorkspaceIndexEntry>();
    public IReadOnlyList<OracleApexWorkspaceIndexEntry> Pages { get; init; } = Array.Empty<OracleApexWorkspaceIndexEntry>();
    public IReadOnlyList<OracleApexWorkspaceIndexEntry> Regions { get; init; } = Array.Empty<OracleApexWorkspaceIndexEntry>();
    public IReadOnlyList<OracleApexWorkspaceIndexEntry> Items { get; init; } = Array.Empty<OracleApexWorkspaceIndexEntry>();
    public IReadOnlyList<OracleApexWorkspaceIndexEntry> SharedComponents { get; init; } = Array.Empty<OracleApexWorkspaceIndexEntry>();
    public IReadOnlyList<OracleApexWorkspaceIndexEntry> NavigationEntries { get; init; } = Array.Empty<OracleApexWorkspaceIndexEntry>();
    public IReadOnlyList<OracleApexWorkspaceDeploymentProfileIndexEntry> DeploymentProfiles { get; init; } = Array.Empty<OracleApexWorkspaceDeploymentProfileIndexEntry>();
    public IReadOnlyList<OracleApexWorkspaceIndexReference> References { get; init; } = Array.Empty<OracleApexWorkspaceIndexReference>();
    public IReadOnlyList<OracleApexWorkspaceIndexDiagnostic> Diagnostics { get; init; } = Array.Empty<OracleApexWorkspaceIndexDiagnostic>();
    public IReadOnlyList<OracleApexWorkspaceIndexLocation> SourceLocations { get; init; } = Array.Empty<OracleApexWorkspaceIndexLocation>();
    public IReadOnlyList<OracleApexWorkspaceIndexSearchEntry> SearchEntries { get; init; } = Array.Empty<OracleApexWorkspaceIndexSearchEntry>();
}

public sealed class OracleApexWorkspaceIndexEntry
{
    public string NodeId { get; init; } = string.Empty;
    public string SemanticType { get; init; } = string.Empty;
    public string Identifier { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public int Line { get; init; }
    public int Column { get; init; }
    public int EndLine { get; init; }
    public int EndColumn { get; init; }
    public string ParentNodeId { get; init; } = string.Empty;
    public IReadOnlyList<string> ChildNodeIds { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> ReferencedObjects { get; init; } = Array.Empty<string>();
}

public sealed class OracleApexWorkspaceDeploymentProfileIndexEntry
{
    public string Name { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public string AbsolutePath { get; init; } = string.Empty;
    public int Line { get; init; }
    public int Column { get; init; }
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> ReferencedObjects { get; init; } = Array.Empty<string>();
    public bool IsValid { get; init; }
    public string ValidationMessage { get; init; } = string.Empty;
}

public sealed class OracleApexWorkspaceIndexReference
{
    public string NodeId { get; init; } = string.Empty;
    public string Identifier { get; init; } = string.Empty;
    public string SemanticType { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public int Line { get; init; }
    public int Column { get; init; }
}

public sealed class OracleApexWorkspaceIndexDiagnostic
{
    public string Severity { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public int Line { get; init; }
    public int Column { get; init; }
    public string NodeId { get; init; } = string.Empty;
    public string SemanticType { get; init; } = string.Empty;
}

public sealed class OracleApexWorkspaceIndexLocation
{
    public string NodeId { get; init; } = string.Empty;
    public string SemanticType { get; init; } = string.Empty;
    public string Identifier { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public int Line { get; init; }
    public int Column { get; init; }
    public int EndLine { get; init; }
    public int EndColumn { get; init; }
}

public sealed class OracleApexWorkspaceIndexSearchEntry
{
    public string Type { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
}
