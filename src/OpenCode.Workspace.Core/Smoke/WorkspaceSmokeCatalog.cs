using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Smoke;

public static class WorkspaceSmokeCatalog
{
    public static readonly string[] CommonValidatorIds =
    [
        "workspace-record-created",
        "generated-files-present",
        "compose-configuration-valid",
        "workspace-container-running",
        "expected-services-running",
        "runtime-inventory-owned",
    ];

    private static readonly HashSet<string> KnownFamilies =
    [
        "lightweight",
        "document-processing",
        "analytics",
        "postgresql",
        "oracle-plsql",
        "oracle-apex",
        "oracle-apexlang",
    ];

    private static readonly HashSet<string> KnownValidatorIds =
    [
        .. CommonValidatorIds,
        "core-tooling",
        "document-processing-tools",
        "analytics-tools",
        "postgresql-runtime",
        "oracle-plsql-runtime",
        "oracle-apex-runtime",
        "oracle-apexlang-runtime",
    ];

    public static IReadOnlyList<WorkspaceSmokeDefinition> BuildDefinitions(IEnumerable<TemplateManifest> templates)
        => templates.Select(BuildDefinition)
            .OrderBy(item => item.TemplateId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static WorkspaceSmokeDefinition BuildDefinition(TemplateManifest template)
    {
        var smoke = template.Smoke ?? new TemplateSmokeManifest();
        var family = string.IsNullOrWhiteSpace(smoke.Family) ? DeriveFamily(template) : smoke.Family.Trim();
        var resourceClass = ParseResourceClass(smoke.ResourceClass, family);
        var timeoutClass = ParseTimeoutClass(smoke.TimeoutClass, resourceClass);
        var supported = smoke.Supported ?? false;
        var expectedServices = (smoke.ExpectedServices.Count == 0 ? DeriveExpectedServices(template) : smoke.ExpectedServices)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var validatorIds = !supported
            ? Array.Empty<string>()
            : CommonValidatorIds.Concat(DeriveDefaultValidatorIds(family)).Concat(smoke.Validators)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return new WorkspaceSmokeDefinition
        {
            TemplateId = template.Id,
            DisplayName = template.DisplayName,
            Family = family,
            Supported = supported,
            UnsupportedReason = smoke.UnsupportedReason?.Trim() ?? string.Empty,
            ResourceClass = resourceClass,
            TimeoutClass = timeoutClass,
            ExpectedServices = expectedServices,
            ValidatorIds = validatorIds,
            Template = template,
        };
    }

    public static IReadOnlyList<string> ValidateTemplateSmokeMetadata(IEnumerable<TemplateManifest> templates, ISet<string> serviceIds)
    {
        var errors = new List<string>();
        foreach (var template in templates)
        {
            var smoke = template.Smoke;
            if (smoke is null || smoke.Supported is null)
            {
                errors.Add($"Template '{template.Id}' is missing smoke coverage metadata.");
                continue;
            }

            var family = string.IsNullOrWhiteSpace(smoke.Family) ? DeriveFamily(template) : smoke.Family.Trim();
            if (!KnownFamilies.Contains(family))
            {
                errors.Add($"Template '{template.Id}' uses unsupported smoke family '{family}'.");
            }

            if (smoke.Supported == false && string.IsNullOrWhiteSpace(smoke.UnsupportedReason))
            {
                errors.Add($"Template '{template.Id}' must declare an unsupportedReason when smoke support is disabled.");
            }

            if (!string.IsNullOrWhiteSpace(smoke.ResourceClass)
                && !Enum.TryParse<WorkspaceSmokeResourceClass>(NormalizeEnumValue(smoke.ResourceClass), ignoreCase: true, out _))
            {
                errors.Add($"Template '{template.Id}' uses unsupported smoke resource class '{smoke.ResourceClass}'.");
            }

            if (!string.IsNullOrWhiteSpace(smoke.TimeoutClass)
                && !Enum.TryParse<WorkspaceSmokeTimeoutClass>(NormalizeEnumValue(smoke.TimeoutClass), ignoreCase: true, out _))
            {
                errors.Add($"Template '{template.Id}' uses unsupported smoke timeout class '{smoke.TimeoutClass}'.");
            }

            foreach (var serviceId in smoke.ExpectedServices)
            {
                if (!string.Equals(serviceId, "workspace", StringComparison.OrdinalIgnoreCase)
                    && !serviceIds.Contains(serviceId))
                {
                    errors.Add($"Template '{template.Id}' smoke metadata references unknown expected service '{serviceId}'.");
                }
            }

            foreach (var validatorId in smoke.Validators)
            {
                if (!KnownValidatorIds.Contains(validatorId))
                {
                    errors.Add($"Template '{template.Id}' smoke metadata references unknown validator '{validatorId}'.");
                }
            }

            if (family == "oracle-plsql" && smoke.Validators.Any(id => id.Contains("apex", StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"Template '{template.Id}' cannot assign APEX validators to an Oracle PL/SQL smoke template.");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> DeriveDefaultValidatorIds(string family)
        => family switch
        {
            "document-processing" => ["core-tooling", "document-processing-tools"],
            "analytics" => ["core-tooling", "analytics-tools"],
            "postgresql" => ["core-tooling", "postgresql-runtime"],
            "oracle-plsql" => ["core-tooling", "oracle-plsql-runtime"],
            "oracle-apex" => ["core-tooling", "oracle-apex-runtime"],
            "oracle-apexlang" => ["core-tooling", "oracle-apexlang-runtime"],
            _ => ["core-tooling"],
        };

    private static WorkspaceSmokeResourceClass ParseResourceClass(string declaredValue, string family)
    {
        if (!string.IsNullOrWhiteSpace(declaredValue)
            && Enum.TryParse<WorkspaceSmokeResourceClass>(NormalizeEnumValue(declaredValue), ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return family switch
        {
            "document-processing" => WorkspaceSmokeResourceClass.DocumentProcessing,
            "analytics" => WorkspaceSmokeResourceClass.Analytics,
            "postgresql" => WorkspaceSmokeResourceClass.Database,
            "oracle-plsql" or "oracle-apex" or "oracle-apexlang" => WorkspaceSmokeResourceClass.OracleExclusive,
            _ => WorkspaceSmokeResourceClass.Lightweight,
        };
    }

    private static WorkspaceSmokeTimeoutClass ParseTimeoutClass(string declaredValue, WorkspaceSmokeResourceClass resourceClass)
    {
        if (!string.IsNullOrWhiteSpace(declaredValue)
            && Enum.TryParse<WorkspaceSmokeTimeoutClass>(NormalizeEnumValue(declaredValue), ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return resourceClass switch
        {
            WorkspaceSmokeResourceClass.OracleExclusive => WorkspaceSmokeTimeoutClass.Extended,
            WorkspaceSmokeResourceClass.Database => WorkspaceSmokeTimeoutClass.Long,
            WorkspaceSmokeResourceClass.DocumentProcessing or WorkspaceSmokeResourceClass.Analytics => WorkspaceSmokeTimeoutClass.Medium,
            _ => WorkspaceSmokeTimeoutClass.Short,
        };
    }

    private static string DeriveFamily(TemplateManifest template)
    {
        if (OracleWorkspaceFamily.Detect(template) == OracleWorkspaceKind.ApexLang)
        {
            return "oracle-apexlang";
        }

        if (OracleWorkspaceFamily.Detect(template) == OracleWorkspaceKind.Apex)
        {
            return "oracle-apex";
        }

        if (OracleWorkspaceFamily.Detect(template) == OracleWorkspaceKind.PlSql)
        {
            return "oracle-plsql";
        }

        if (template.Services.Contains("postgres", StringComparer.OrdinalIgnoreCase))
        {
            return "postgresql";
        }

        if (template.Features.Contains("document-processing", StringComparer.OrdinalIgnoreCase)
            || template.Features.Contains("ocr-processing", StringComparer.OrdinalIgnoreCase))
        {
            return "document-processing";
        }

        if (template.Features.Contains("analytics-reporting", StringComparer.OrdinalIgnoreCase)
            || template.Features.Contains("education-stem-demo", StringComparer.OrdinalIgnoreCase))
        {
            return "analytics";
        }

        return "lightweight";
    }

    private static IReadOnlyList<string> DeriveExpectedServices(TemplateManifest template)
        => new[] { "workspace" }.Concat(template.Services)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string NormalizeEnumValue(string value)
        => value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
}
