namespace OpenCode.Workspace.Core.Workspaces;

public sealed class OracleApexLanguageReferenceCatalog
{
    public string ApexVersion { get; init; } = string.Empty;
    public string GrammarVersion { get; init; } = string.Empty;
    public OracleApexLanguageReferenceProvenance Provenance { get; init; } = new();
    public IReadOnlyDictionary<string, OracleApexLanguageReferenceComponent> Components { get; init; } = new Dictionary<string, OracleApexLanguageReferenceComponent>(StringComparer.OrdinalIgnoreCase);
}

public sealed class OracleApexLanguageReferenceComponent
{
    public string CanonicalName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string DocumentationAnchor { get; init; } = string.Empty;
    public IReadOnlyList<string> ParentComponents { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ChildComponents { get; init; } = Array.Empty<string>();
    public IReadOnlyList<OracleApexLanguageReferenceProperty> DirectProperties { get; init; } = Array.Empty<OracleApexLanguageReferenceProperty>();
    public IReadOnlyList<OracleApexLanguageReferencePropertyGroup> PropertyGroups { get; init; } = Array.Empty<OracleApexLanguageReferencePropertyGroup>();
    public IReadOnlyList<string> CanonicalExamples { get; init; } = Array.Empty<string>();
}

public sealed class OracleApexLanguageReferencePropertyGroup
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<OracleApexLanguageReferenceProperty> Properties { get; init; } = Array.Empty<OracleApexLanguageReferenceProperty>();
}

public sealed class OracleApexLanguageReferenceProperty
{
    public string Name { get; init; } = string.Empty;
    public string PropertyPath { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public bool Required { get; init; }
    public string DefaultValue { get; init; } = string.Empty;
    public IReadOnlyList<string> EnumValues { get; init; } = Array.Empty<string>();
    public string AppliesWhen { get; init; } = string.Empty;
    public string MaxLength { get; init; } = string.Empty;
    public string NumericBounds { get; init; } = string.Empty;
    public string ValidationConstraint { get; init; } = string.Empty;
}

public sealed class OracleApexLanguageReferenceProvenance
{
    public string SourceKind { get; init; } = string.Empty;
    public string SourceLocation { get; init; } = string.Empty;
    public string GrammarLocation { get; init; } = string.Empty;
    public string ApexVersion { get; init; } = string.Empty;
    public DateTimeOffset ImportedUtc { get; init; }
}

public sealed class OracleApexLanguageReferenceCompatibilityResult
{
    public IReadOnlyList<OracleApexLanguageReferenceCompatibilityWarning> Warnings { get; init; } = Array.Empty<OracleApexLanguageReferenceCompatibilityWarning>();
    public bool HasWarnings => Warnings.Count > 0;
}

public sealed class OracleApexLanguageReferenceCompatibilityWarning
{
    public string ComponentName { get; init; } = string.Empty;
    public string PropertyName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string OfficialValue { get; init; } = string.Empty;
    public string AtlasValue { get; init; } = string.Empty;
}
