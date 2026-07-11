namespace OpenCode.Workspace.Core.Workspaces;

internal static class OracleApexBuiltInLanguageReference
{
    public static OracleApexLanguageReferenceCatalog Create()
    {
        var components = new Dictionary<string, OracleApexLanguageReferenceComponent>(StringComparer.OrdinalIgnoreCase)
        {
            ["application"] = Component("application", "app", "comp-1000", [], ["page", "authorization-scheme", "authentication-scheme", "navigation-menu", "list", "lov", "build-option", "static-file", "plugin", "rest-data-source"],
            [
                Property("name", "string", required: true, defaultValue: string.Empty, maxLength: "255"),
                Property("alias", "string", required: true, defaultValue: string.Empty, maxLength: "80"),
                Property("version", "string", required: true, defaultValue: "Release 1.0", maxLength: "255"),
                Property("type", "enum", required: true, enumValues: ["standard", "library", "boilerplate", "theme"]),
            ],
            [Group("navigationMenu", [Property("navigationMenu.listPosition", "enum", required: true, defaultValue: "side", enumValues: ["top", "side"]), Property("navigationMenu.list", "string")])],
            ["app (\n  name: \"Demo\"\n  alias: \"DEMO\"\n  version: \"Release 1.0\"\n  type: standard\n)" ]),
            ["page"] = Component("page", "page", "comp-5000", ["application"], ["region", "item", "button", "process", "dynamic-action", "branch", "computation", "validation"],
            [
                Property("name", "string", required: true),
                Property("alias", "string", required: true),
                Property("id", "integer"),
            ],
            [],
            ["page home (\n  name: \"Home\"\n  alias: \"HOME\"\n)" ]),
            ["region"] = Component("region", "region", "comp-5110", ["page", "region"], ["item", "button", "process", "dynamic-action", "region"],
            [
                Property("title", "string", required: true),
                Property("type", "string", required: true),
                Property("source-type", "string"),
            ],
            [],
            ["region report_region (\n  title: \"Report\"\n  type: \"Interactive Report\"\n)" ]),
            ["item"] = Component("item", "pageItem", "comp-5120", ["page", "region"], [],
            [Property("name", "string", required: true), Property("type", "string"), Property("label", "string"), Property("lov", "string")], [], []),
            ["button"] = Component("button", "button", "comp-5130", ["page", "region"], [],
            [Property("name", "string", required: true), Property("label", "string"), Property("target-page", "integer")], [], []),
            ["dynamic-action"] = Component("dynamic-action", "dynamicAction", "comp-5140", ["page", "region"], [], [Property("name", "string", required: true), Property("event", "string")], [], []),
            ["process"] = Component("process", "process", "comp-5530", ["page", "region"], [], [Property("name", "string", required: true), Property("type", "string")], [], []),
            ["branch"] = Component("branch", "branch", "comp-5540", ["page"], [], [Property("name", "string", required: true), Property("target-page", "integer")], [], []),
            ["computation"] = Component("computation", "computation", "comp-5520", ["page"], [], [Property("name", "string", required: true)], [], []),
            ["validation"] = Component("validation", "validation", "comp-5510", ["page"], [], [Property("name", "string")], [], []),
            ["authorization-scheme"] = Component("authorization-scheme", "authorization", "comp-3060", ["application"], [], [Property("name", "string", required: true)], [], []),
            ["authentication-scheme"] = Component("authentication-scheme", "authentication", "comp-3050", ["application"], [], [Property("name", "string", required: true)], [], []),
            ["navigation-menu"] = Component("navigation-menu", "list", "comp-3520", ["application"], ["navigation-entry"], [Property("name", "string", required: true)], [], []),
            ["navigation-entry"] = Component("navigation-entry", "entry", "comp-3525", ["navigation-menu"], [], [Property("label", "string", required: true), Property("target-page", "integer"), Property("parent-entry", "string")], [], []),
            ["lov"] = Component("lov", "lov", "comp-3530", ["application"], ["column", "entry", "parameter"], [Property("name", "string", required: true)], [], []),
            ["list"] = Component("list", "list", "comp-3520", ["application"], ["entry"], [Property("name", "string", required: true)], [], []),
            ["build-option"] = Component("build-option", "buildOption", "comp-3040", ["application"], [], [Property("name", "string", required: true)], [], []),
            ["static-file"] = Component("static-file", "file", "comp-3150", ["application"], [], [Property("name", "string", required: true)], [], []),
            ["plugin"] = Component("plugin", "plugin", "comp-2700", ["application"], [], [Property("name", "string", required: true)], [], []),
            ["rest-data-source"] = Component("rest-data-source", "restDataSource", "comp-3080", ["application"], ["operation", "parameter"], [Property("name", "string", required: true)], [], []),
            ["rest-module"] = Component("rest-module", "restModule", string.Empty, ["application"], ["rest-handler"], [Property("name", "string", required: true)], [], []),
            ["rest-handler"] = Component("rest-handler", "restHandler", string.Empty, ["rest-module", "application"], [], [Property("name", "string", required: true), Property("method", "enum", enumValues: ["GET", "POST", "PUT", "PATCH", "DELETE"])], [], []),
            ["deployment-profile"] = Component("deployment-profile", "deployment", string.Empty, [], [], [Property("name", "string", required: true), Property("application-id", "integer")], [], []),
        };

        return new OracleApexLanguageReferenceCatalog
        {
            ApexVersion = "26.1",
            GrammarVersion = "26.1",
            Provenance = new OracleApexLanguageReferenceProvenance
            {
                SourceKind = "official-normalized-index",
                SourceLocation = "https://docs.oracle.com/en/database/oracle/apex/26.1/apxln/index.html",
                GrammarLocation = "https://docs.oracle.com/en/database/oracle/apex/26.1/apxln/apexlang.ebnf",
                ApexVersion = "26.1",
                ImportedUtc = DateTimeOffset.UtcNow,
            },
            Components = components,
        };
    }

    private static OracleApexLanguageReferenceComponent Component(string canonicalName, string displayName, string anchor, IReadOnlyList<string> parents, IReadOnlyList<string> children, IReadOnlyList<OracleApexLanguageReferenceProperty> directProperties, IReadOnlyList<OracleApexLanguageReferencePropertyGroup> propertyGroups, IReadOnlyList<string> examples)
        => new()
        {
            CanonicalName = canonicalName,
            DisplayName = displayName,
            DocumentationAnchor = anchor,
            ParentComponents = parents,
            ChildComponents = children,
            DirectProperties = directProperties,
            PropertyGroups = propertyGroups,
            CanonicalExamples = examples,
        };

    private static OracleApexLanguageReferenceProperty Property(string name, string dataType, bool required = false, string defaultValue = "", IReadOnlyList<string>? enumValues = null, string appliesWhen = "", string maxLength = "", string numericBounds = "")
        => new()
        {
            Name = NormalizePropertyPath(name).Contains('.') ? NormalizePropertyPath(name)[(NormalizePropertyPath(name).LastIndexOf('.') + 1)..] : NormalizePropertyPath(name),
            PropertyPath = NormalizePropertyPath(name),
            DataType = dataType,
            Required = required,
            DefaultValue = defaultValue,
            EnumValues = enumValues ?? Array.Empty<string>(),
            AppliesWhen = appliesWhen,
            MaxLength = maxLength,
            NumericBounds = numericBounds,
        };

    private static OracleApexLanguageReferencePropertyGroup Group(string name, IReadOnlyList<OracleApexLanguageReferenceProperty> properties)
        => new() { Name = name, Properties = properties };

    private static string NormalizePropertyPath(string value)
        => string.Join('.', value.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim().Replace("_", "-", StringComparison.Ordinal).Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant()));
}
