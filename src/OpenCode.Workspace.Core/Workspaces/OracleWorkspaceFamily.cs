using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public enum OracleWorkspaceKind
{
    None,
    PlSql,
    Apex,
    ApexLang,
}

public static class OracleWorkspaceFamily
{
    public const string OraclePlSqlTemplateId = "oracle-plsql-demo";
    public const string OracleApexTemplateId = "oracle-apex-demo";
    public const string OracleApexLangTemplateId = "oracle-apexlang-demo";

    public const string OracleBaseFeatureId = "oracle-demo";
    public const string OracleApexFeatureId = "oracle-apex-demo";
    public const string OracleApexLangFeatureId = "oracle-apexlang-demo";

    public const string OracleDatabaseServiceId = "oracle-demo";
    public const string OracleOrdsServiceId = "oracle-ords";

    public static OracleWorkspaceKind Detect(WorkspaceDefinition definition)
    {
        if (definition is null)
        {
            return OracleWorkspaceKind.None;
        }

        if (Contains(definition.Features, OracleApexLangFeatureId))
        {
            return OracleWorkspaceKind.ApexLang;
        }

        if (Contains(definition.Features, OracleApexFeatureId) || Contains(definition.Services, OracleOrdsServiceId))
        {
            return OracleWorkspaceKind.Apex;
        }

        return Contains(definition.Features, OracleBaseFeatureId) || Contains(definition.Services, OracleDatabaseServiceId)
            ? OracleWorkspaceKind.PlSql
            : OracleWorkspaceKind.None;
    }

    public static OracleWorkspaceKind Detect(TemplateManifest template)
    {
        if (template is null)
        {
            return OracleWorkspaceKind.None;
        }

        if (string.Equals(template.Id, OracleApexLangTemplateId, StringComparison.OrdinalIgnoreCase))
        {
            return OracleWorkspaceKind.ApexLang;
        }

        if (string.Equals(template.Id, OracleApexTemplateId, StringComparison.OrdinalIgnoreCase))
        {
            return OracleWorkspaceKind.Apex;
        }

        return string.Equals(template.Id, OraclePlSqlTemplateId, StringComparison.OrdinalIgnoreCase)
            ? OracleWorkspaceKind.PlSql
            : Detect(new WorkspaceDefinition { Features = template.Features, Services = template.Services });
    }

    public static bool IsOracleWorkspace(WorkspaceDefinition definition)
        => Detect(definition) != OracleWorkspaceKind.None;

    public static bool IsOracleWorkspace(TemplateManifest template)
        => Detect(template) != OracleWorkspaceKind.None;

    public static bool HasApex(WorkspaceDefinition definition)
        => Detect(definition) is OracleWorkspaceKind.Apex or OracleWorkspaceKind.ApexLang;

    public static bool HasApex(TemplateManifest template)
        => Detect(template) is OracleWorkspaceKind.Apex or OracleWorkspaceKind.ApexLang;

    public static bool HasApexLang(WorkspaceDefinition definition)
        => Detect(definition) == OracleWorkspaceKind.ApexLang;

    private static bool Contains(IEnumerable<string> values, string expected)
        => values.Contains(expected, StringComparer.OrdinalIgnoreCase);
}
