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

public sealed class OracleApexLanguageReferenceDiffReport
{
    public string FromApexVersion { get; init; } = string.Empty;
    public string ToApexVersion { get; init; } = string.Empty;
    public string FromGrammarVersion { get; init; } = string.Empty;
    public string ToGrammarVersion { get; init; } = string.Empty;
    public OracleApexLanguageReferenceProvenance FromProvenance { get; init; } = new();
    public OracleApexLanguageReferenceProvenance ToProvenance { get; init; } = new();
    public IReadOnlyList<OracleApexLanguageReferenceDifference> Differences { get; init; } = Array.Empty<OracleApexLanguageReferenceDifference>();
    public OracleApexLanguageReferenceAtlasCompatibilityDiff AtlasCompatibility { get; init; } = new();
}

public sealed class OracleApexLanguageReferenceDifference
{
    public string Kind { get; init; } = string.Empty;
    public string ComponentName { get; init; } = string.Empty;
    public string RelatedComponentName { get; init; } = string.Empty;
    public string PropertyPath { get; init; } = string.Empty;
    public string BeforeValue { get; init; } = string.Empty;
    public string AfterValue { get; init; } = string.Empty;
    public IReadOnlyList<string> AddedValues { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RemovedValues { get; init; } = Array.Empty<string>();
    public OracleApexLanguageReferenceDifferenceProvenance Provenance { get; init; } = new();
}

public sealed class OracleApexLanguageReferenceDifferenceProvenance
{
    public OracleApexLanguageReferenceProvenance FromCatalog { get; init; } = new();
    public OracleApexLanguageReferenceProvenance ToCatalog { get; init; } = new();
    public string FromDocumentationReference { get; init; } = string.Empty;
    public string ToDocumentationReference { get; init; } = string.Empty;
}

public sealed class OracleApexLanguageReferenceAtlasCompatibilityDiff
{
    public int PreviousWarningCount { get; init; }
    public int CurrentWarningCount { get; init; }
    public bool DriftIncreased { get; init; }
    public IReadOnlyList<OracleApexLanguageReferenceCompatibilityWarning> AddedWarnings { get; init; } = Array.Empty<OracleApexLanguageReferenceCompatibilityWarning>();
    public IReadOnlyList<OracleApexLanguageReferenceCompatibilityWarning> RemovedWarnings { get; init; } = Array.Empty<OracleApexLanguageReferenceCompatibilityWarning>();
}
