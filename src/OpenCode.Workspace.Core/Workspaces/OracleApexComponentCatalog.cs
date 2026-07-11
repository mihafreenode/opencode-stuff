namespace OpenCode.Workspace.Core.Workspaces;

public sealed class OracleApexComponentCatalog
{
    public static OracleApexComponentCatalog AtlasSeed { get; } = CreateDefault();
    public static OracleApexComponentCatalog Default { get; } = AtlasSeed.MergeWithReference(OracleApexBuiltInLanguageReference.Create(), null);

    public IReadOnlyDictionary<string, OracleApexComponentDefinition> Components { get; }

    private OracleApexComponentCatalog(IReadOnlyDictionary<string, OracleApexComponentDefinition> components)
        => Components = components;

    public OracleApexComponentDefinition GetComponent(string semanticType)
        => Components.TryGetValue(semanticType, out var component)
            ? component
            : throw new KeyNotFoundException($"Oracle APEX component type '{semanticType}' is not defined in the catalog.");

    public bool TryGetComponent(string semanticType, out OracleApexComponentDefinition? component)
        => Components.TryGetValue(semanticType, out component);

    public OracleApexComponentCatalog MergeWithReference(OracleApexLanguageReferenceCatalog referenceCatalog, OracleApexLanguageReferenceCompatibilityResult? compatibility)
    {
        var merged = new Dictionary<string, OracleApexComponentDefinition>(Components, StringComparer.OrdinalIgnoreCase);

        foreach (var referenceComponent in referenceCatalog.Components.Values)
        {
            if (!merged.TryGetValue(referenceComponent.CanonicalName, out var existing))
            {
                merged[referenceComponent.CanonicalName] = new OracleApexComponentDefinition
                {
                    CanonicalName = referenceComponent.CanonicalName,
                    DisplayName = string.IsNullOrWhiteSpace(referenceComponent.DisplayName) ? referenceComponent.CanonicalName : referenceComponent.DisplayName,
                    ParentComponents = referenceComponent.ParentComponents,
                    ChildComponents = referenceComponent.ChildComponents,
                    Properties = referenceComponent.DirectProperties.Concat(referenceComponent.PropertyGroups.SelectMany(group => group.Properties)).Select(ToPropertyDefinition).ToList(),
                    RequiredProperties = referenceComponent.DirectProperties.Concat(referenceComponent.PropertyGroups.SelectMany(group => group.Properties)).Where(item => item.Required).Select(item => item.PropertyPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    DocumentationReference = BuildOfficialDocumentationReference(referenceCatalog.Provenance.SourceLocation, referenceComponent.DocumentationAnchor),
                    SupportedApexVersions = [referenceCatalog.ApexVersion],
                    CanonicalExamples = referenceComponent.CanonicalExamples,
                    Provenance = new OracleApexReferenceProvenance
                    {
                        SourceKind = referenceCatalog.Provenance.SourceKind,
                        SourceLocation = referenceCatalog.Provenance.SourceLocation,
                        ApexVersion = referenceCatalog.ApexVersion,
                    },
                };
                continue;
            }

            var propertyMap = existing.Properties.ToDictionary(property => property.Name, property => property, StringComparer.OrdinalIgnoreCase);
            foreach (var referenceProperty in referenceComponent.DirectProperties.Concat(referenceComponent.PropertyGroups.SelectMany(group => group.Properties)))
            {
                var normalizedProperty = ToPropertyDefinition(referenceProperty);
                if (propertyMap.TryGetValue(normalizedProperty.Name, out var existingProperty))
                {
                    propertyMap[normalizedProperty.Name] = MergeProperty(existingProperty, normalizedProperty);
                }
                else
                {
                    propertyMap[normalizedProperty.Name] = new OracleApexPropertyDefinition
                    {
                        Name = normalizedProperty.Name,
                        PropertyType = normalizedProperty.PropertyType,
                        Required = false,
                        EnumValues = normalizedProperty.EnumValues,
                        DefaultValue = normalizedProperty.DefaultValue,
                        AppliesWhen = normalizedProperty.AppliesWhen,
                        MaxLength = normalizedProperty.MaxLength,
                        NumericBounds = normalizedProperty.NumericBounds,
                        ValidationConstraint = normalizedProperty.ValidationConstraint,
                    };
                }
            }

                merged[referenceComponent.CanonicalName] = new OracleApexComponentDefinition
                {
                    CanonicalName = existing.CanonicalName,
                    DisplayName = string.IsNullOrWhiteSpace(existing.DisplayName) ? referenceComponent.DisplayName : existing.DisplayName,
                ParentComponents = referenceComponent.ParentComponents.Count == 0 ? existing.ParentComponents : referenceComponent.ParentComponents,
                ChildComponents = referenceComponent.ChildComponents.Count == 0 ? existing.ChildComponents : referenceComponent.ChildComponents,
                Properties = propertyMap.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                RequiredProperties = propertyMap.Values.Where(item => item.Required).Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                DocumentationReference = BuildOfficialDocumentationReference(referenceCatalog.Provenance.SourceLocation, referenceComponent.DocumentationAnchor),
                SupportedApexVersions = existing.SupportedApexVersions.Concat([referenceCatalog.ApexVersion]).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
                CanonicalExamples = referenceComponent.CanonicalExamples.Count == 0 ? existing.CanonicalExamples : referenceComponent.CanonicalExamples,
                Provenance = new OracleApexReferenceProvenance
                {
                    SourceKind = referenceCatalog.Provenance.SourceKind,
                    SourceLocation = referenceCatalog.Provenance.SourceLocation,
                    ApexVersion = referenceCatalog.ApexVersion,
                },
            };
        }

        return new OracleApexComponentCatalog(merged);
    }

    public OracleApexLanguageReferenceCompatibilityResult CompareWithReference(OracleApexLanguageReferenceCatalog referenceCatalog)
    {
        var warnings = new List<OracleApexLanguageReferenceCompatibilityWarning>();
        foreach (var referenceComponent in referenceCatalog.Components.Values)
        {
            if (!Components.TryGetValue(referenceComponent.CanonicalName, out var component))
            {
                warnings.Add(new OracleApexLanguageReferenceCompatibilityWarning
                {
                    ComponentName = referenceComponent.CanonicalName,
                    Message = $"Official component '{referenceComponent.CanonicalName}' is not represented in the Atlas-enriched catalog.",
                    OfficialValue = referenceComponent.CanonicalName,
                });
                continue;
            }

            foreach (var referenceProperty in referenceComponent.DirectProperties.Concat(referenceComponent.PropertyGroups.SelectMany(group => group.Properties)))
            {
                if (!component.Properties.Any(property => string.Equals(property.Name, referenceProperty.PropertyPath, StringComparison.OrdinalIgnoreCase)))
                {
                    warnings.Add(new OracleApexLanguageReferenceCompatibilityWarning
                    {
                        ComponentName = referenceComponent.CanonicalName,
                        PropertyName = referenceProperty.PropertyPath,
                        Message = $"Official property '{referenceProperty.PropertyPath}' is missing from the Atlas-enriched catalog component '{referenceComponent.CanonicalName}'.",
                        OfficialValue = referenceProperty.DataType,
                    });
                }
            }
        }

        return new OracleApexLanguageReferenceCompatibilityResult { Warnings = warnings };
    }

    public string BuildDocumentation()
    {
        var lines = new List<string>
        {
            "# Oracle APEX Component Catalog",
            string.Empty,
            "## Supported Component Types",
            string.Empty,
        };

        foreach (var component in Components.Values.OrderBy(item => item.CanonicalName, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"- {component.CanonicalName}: {component.DisplayName}");
        }

        lines.Add(string.Empty);
        lines.Add("## Hierarchy");
        lines.Add(string.Empty);
        foreach (var component in Components.Values.OrderBy(item => item.CanonicalName, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"- {component.DisplayName}: parents={(component.ParentComponents.Count == 0 ? "root" : string.Join(", ", component.ParentComponents))}; children={(component.ChildComponents.Count == 0 ? "none" : string.Join(", ", component.ChildComponents))}");
        }

        lines.Add(string.Empty);
        lines.Add("## Common Properties");
        lines.Add(string.Empty);
        foreach (var component in Components.Values.OrderBy(item => item.CanonicalName, StringComparer.OrdinalIgnoreCase))
        {
            var properties = component.Properties.Count == 0
                ? "none"
                : string.Join(", ", component.Properties.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Select(item => $"{item.Name}:{item.PropertyType}"));
            lines.Add($"- {component.DisplayName}: {properties}");
        }

        lines.Add(string.Empty);
        lines.Add("## Relationships");
        lines.Add(string.Empty);
        foreach (var component in Components.Values.OrderBy(item => item.CanonicalName, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"- {component.DisplayName}: doc={component.DocumentationReference}; versions={string.Join(", ", component.SupportedApexVersions)}");
        }

        lines.Add(string.Empty);
        lines.Add("## Examples");
        lines.Add(string.Empty);
        lines.Add("- `application` contains `page`, shared components, and REST definitions.");
        lines.Add("- `page` contains `region`, `item`, `button`, `process`, `dynamic-action`, `branch`, and `computation`.");
        lines.Add("- `navigation-menu` contains `navigation-entry` records that can reference pages.");

        return string.Join("\n", lines) + "\n";
    }

    private static OracleApexComponentCatalog CreateDefault()
    {
        var components = new Dictionary<string, OracleApexComponentDefinition>(StringComparer.OrdinalIgnoreCase);

        Add(components, "application", "Application", [], ["page", "authorization-scheme", "authentication-scheme", "navigation-menu", "list", "lov", "build-option", "static-file", "plugin", "rest-data-source", "rest-module", "rest-handler"],
        [
            Property("id", "integer", required: true),
            Property("name", "string", required: true),
            Property("alias", "string"),
            Property("version", "string"),
            Property("workspace", "string"),
            Property("parsing-schema", "string"),
        ]);

        Add(components, "page", "Page", ["application"], ["region", "item", "button", "process", "dynamic-action", "branch", "computation"],
        [
            Property("id", "integer", required: true),
            Property("name", "string", required: true),
            Property("alias", "string", required: true),
            Property("mode", "enum", enumValues: ["Normal", "Modal", "Non-Modal"]),
            Property("authentication", "string"),
            Property("parent-page", "integer"),
            Property("breadcrumb", "string"),
        ]);

        Add(components, "region", "Region", ["page"], ["item", "button", "process", "dynamic-action", "region"],
        [
            Property("name", "string"),
            Property("title", "string", required: true),
            Property("type", "string", required: true),
            Property("source-type", "string"),
            Property("source", "sql/plsql"),
            Property("rest-source", "string"),
            Property("authorization-scheme", "component-reference"),
        ]);

        Add(components, "item", "Item", ["page", "region"], [],
        [
            Property("name", "string", required: true),
            Property("type", "string"),
            Property("label", "string"),
            Property("lov", "component-reference"),
            Property("source", "sql/plsql"),
        ]);

        Add(components, "button", "Button", ["page", "region"], [],
        [
            Property("name", "string", required: true),
            Property("label", "string"),
            Property("target-page", "integer"),
            Property("authorization-scheme", "component-reference"),
        ]);

        Add(components, "process", "Process", ["page", "region"], [],
        [
            Property("name", "string", required: true),
            Property("type", "string"),
            Property("source", "sql/plsql"),
            Property("authorization-scheme", "component-reference"),
        ]);

        Add(components, "dynamic-action", "Dynamic Action", ["page", "region"], [],
        [
            Property("name", "string", required: true),
            Property("event", "string"),
            Property("selection-type", "string"),
            Property("authorization-scheme", "component-reference"),
        ]);

        Add(components, "branch", "Branch", ["page"], [],
        [
            Property("name", "string", required: true),
            Property("target-page", "integer"),
        ]);

        Add(components, "computation", "Computation", ["page"], [],
        [
            Property("name", "string", required: true),
            Property("item", "component-reference"),
            Property("expression", "sql/plsql"),
        ]);

        Add(components, "authorization-scheme", "Authorization Scheme", ["application"], [], [Property("name", "string", required: true)]);
        Add(components, "authentication-scheme", "Authentication Scheme", ["application"], [], [Property("name", "string", required: true)]);
        Add(components, "navigation-menu", "Navigation Menu", ["application"], ["navigation-entry"], [Property("name", "string", required: true)]);
        Add(components, "navigation-entry", "Navigation Entry", ["navigation-menu"], [], [Property("label", "string", required: true), Property("target-page", "integer"), Property("parent-entry", "component-reference")]);
        Add(components, "list", "List", ["application"], [], [Property("name", "string", required: true)]);
        Add(components, "lov", "LOV", ["application"], [], [Property("name", "string", required: true)]);
        Add(components, "build-option", "Build Option", ["application"], [], [Property("name", "string", required: true)]);
        Add(components, "static-file", "Static File", ["application"], [], [Property("name", "string", required: true)]);
        Add(components, "plugin", "Plug-in", ["application"], [], [Property("name", "string", required: true)]);
        Add(components, "rest-data-source", "REST Data Source", ["application"], [], [Property("name", "string", required: true), Property("url", "string")]);
        Add(components, "rest-module", "REST Module", ["application"], ["rest-handler"], [Property("name", "string", required: true), Property("base-path", "string")]);
        Add(components, "rest-handler", "REST Handler", ["application", "rest-module"], [], [Property("name", "string", required: true), Property("method", "enum", enumValues: ["GET", "POST", "PUT", "PATCH", "DELETE"]), Property("source", "sql/plsql")]);
        Add(components, "deployment-profile", "Deployment Profile", [], [], [Property("name", "string", required: true), Property("workspace", "string"), Property("parsing-schema", "string"), Property("application-id", "integer")]);

        return new OracleApexComponentCatalog(components);
    }

    internal static OracleApexPropertyDefinition Property(string name, string propertyType, bool required = false, IReadOnlyList<string>? enumValues = null)
        => new()
        {
            Name = name,
            PropertyType = propertyType,
            Required = required,
            EnumValues = enumValues ?? Array.Empty<string>(),
        };

    private static OracleApexPropertyDefinition ToPropertyDefinition(OracleApexLanguageReferenceProperty property)
        => new()
        {
            Name = property.PropertyPath,
            PropertyType = property.DataType,
            Required = property.Required,
            EnumValues = property.EnumValues,
            DefaultValue = property.DefaultValue,
            AppliesWhen = property.AppliesWhen,
            MaxLength = property.MaxLength,
            NumericBounds = property.NumericBounds,
            ValidationConstraint = property.ValidationConstraint,
        };

    private static OracleApexPropertyDefinition MergeProperty(OracleApexPropertyDefinition existing, OracleApexPropertyDefinition incoming)
        => new()
        {
            Name = existing.Name,
            PropertyType = string.IsNullOrWhiteSpace(existing.PropertyType) ? incoming.PropertyType : existing.PropertyType,
            Required = existing.Required,
            EnumValues = existing.EnumValues.Count == 0 ? incoming.EnumValues : existing.EnumValues,
            DefaultValue = string.IsNullOrWhiteSpace(existing.DefaultValue) ? incoming.DefaultValue : existing.DefaultValue,
            AppliesWhen = string.IsNullOrWhiteSpace(existing.AppliesWhen) ? incoming.AppliesWhen : existing.AppliesWhen,
            MaxLength = string.IsNullOrWhiteSpace(existing.MaxLength) ? incoming.MaxLength : existing.MaxLength,
            NumericBounds = string.IsNullOrWhiteSpace(existing.NumericBounds) ? incoming.NumericBounds : existing.NumericBounds,
            ValidationConstraint = string.IsNullOrWhiteSpace(existing.ValidationConstraint) ? incoming.ValidationConstraint : existing.ValidationConstraint,
        };

    private static string BuildOfficialDocumentationReference(string sourceLocation, string anchor)
        => string.IsNullOrWhiteSpace(anchor) ? sourceLocation : $"{sourceLocation.TrimEnd('#')}#{anchor}";

    private static void Add(
        IDictionary<string, OracleApexComponentDefinition> components,
        string canonicalName,
        string displayName,
        IReadOnlyList<string> parents,
        IReadOnlyList<string> children,
        IReadOnlyList<OracleApexPropertyDefinition> properties)
    {
        components[canonicalName] = new OracleApexComponentDefinition
        {
            CanonicalName = canonicalName,
            DisplayName = displayName,
            ParentComponents = parents,
            ChildComponents = children,
            Properties = properties,
            RequiredProperties = properties.Where(item => item.Required).Select(item => item.Name).ToList(),
            DocumentationReference = $"https://docs.oracle.com/search/?q=Oracle%20APEX%2026.1%20{Uri.EscapeDataString(displayName)}",
            SupportedApexVersions = ["26.1"],
        };
    }
}

public sealed class OracleApexComponentDefinition
{
    public string CanonicalName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public IReadOnlyList<string> ParentComponents { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ChildComponents { get; init; } = Array.Empty<string>();
    public IReadOnlyList<OracleApexPropertyDefinition> Properties { get; init; } = Array.Empty<OracleApexPropertyDefinition>();
    public IReadOnlyList<string> RequiredProperties { get; init; } = Array.Empty<string>();
    public string DocumentationReference { get; init; } = string.Empty;
    public IReadOnlyList<string> SupportedApexVersions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CanonicalExamples { get; init; } = Array.Empty<string>();
    public OracleApexReferenceProvenance Provenance { get; init; } = new();
}

public sealed class OracleApexPropertyDefinition
{
    public string Name { get; init; } = string.Empty;
    public string PropertyType { get; init; } = string.Empty;
    public bool Required { get; init; }
    public IReadOnlyList<string> EnumValues { get; init; } = Array.Empty<string>();
    public string DefaultValue { get; init; } = string.Empty;
    public string AppliesWhen { get; init; } = string.Empty;
    public string MaxLength { get; init; } = string.Empty;
    public string NumericBounds { get; init; } = string.Empty;
    public string ValidationConstraint { get; init; } = string.Empty;
}

public sealed class OracleApexReferenceProvenance
{
    public string SourceKind { get; init; } = string.Empty;
    public string SourceLocation { get; init; } = string.Empty;
    public string ApexVersion { get; init; } = string.Empty;
}
