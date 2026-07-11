using System.Text;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class OracleApexLanguageReferenceCatalogComparer
{
    public OracleApexLanguageReferenceDiffReport Compare(
        OracleApexLanguageReferenceCatalog previous,
        OracleApexLanguageReferenceCatalog current,
        OracleApexLanguageReferenceCompatibilityResult? previousCompatibility = null,
        OracleApexLanguageReferenceCompatibilityResult? currentCompatibility = null)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var differences = new List<OracleApexLanguageReferenceDifference>();
        foreach (var componentName in previous.Components.Keys.Concat(current.Components.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            previous.Components.TryGetValue(componentName, out var previousComponent);
            current.Components.TryGetValue(componentName, out var currentComponent);

            if (previousComponent is null)
            {
                differences.Add(CreateDifference("component-added", componentName, string.Empty, string.Empty, string.Empty, string.Empty, [], [], previous, current, null, currentComponent));
                continue;
            }

            if (currentComponent is null)
            {
                differences.Add(CreateDifference("component-removed", componentName, string.Empty, string.Empty, string.Empty, string.Empty, [], [], previous, current, previousComponent, null));
                continue;
            }

            AddSetDifferences(differences, "parent-added", "parent-removed", componentName, previousComponent.ParentComponents, currentComponent.ParentComponents, previous, current, previousComponent, currentComponent);
            AddSetDifferences(differences, "child-added", "child-removed", componentName, previousComponent.ChildComponents, currentComponent.ChildComponents, previous, current, previousComponent, currentComponent);

            if (!string.Equals(previousComponent.DocumentationAnchor, currentComponent.DocumentationAnchor, StringComparison.Ordinal))
            {
                differences.Add(CreateDifference("documentation-anchor-changed", componentName, string.Empty, string.Empty, previousComponent.DocumentationAnchor, currentComponent.DocumentationAnchor, [], [], previous, current, previousComponent, currentComponent));
            }

            var previousProperties = FlattenProperties(previousComponent);
            var currentProperties = FlattenProperties(currentComponent);
            foreach (var propertyPath in previousProperties.Keys.Concat(currentProperties.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                previousProperties.TryGetValue(propertyPath, out var previousProperty);
                currentProperties.TryGetValue(propertyPath, out var currentProperty);

                if (previousProperty is null)
                {
                    differences.Add(CreateDifference("property-added", componentName, string.Empty, propertyPath, string.Empty, currentProperty?.DataType ?? string.Empty, [], [], previous, current, previousComponent, currentComponent));
                    continue;
                }

                if (currentProperty is null)
                {
                    differences.Add(CreateDifference("property-removed", componentName, string.Empty, propertyPath, previousProperty.DataType, string.Empty, [], [], previous, current, previousComponent, currentComponent));
                    continue;
                }

                if (!string.Equals(previousProperty.DataType, currentProperty.DataType, StringComparison.Ordinal))
                {
                    differences.Add(CreateDifference("property-type-changed", componentName, string.Empty, propertyPath, previousProperty.DataType, currentProperty.DataType, [], [], previous, current, previousComponent, currentComponent));
                }

                if (previousProperty.Required != currentProperty.Required)
                {
                    differences.Add(CreateDifference("property-required-changed", componentName, string.Empty, propertyPath, previousProperty.Required ? "required" : "optional", currentProperty.Required ? "required" : "optional", [], [], previous, current, previousComponent, currentComponent));
                }

                if (!string.Equals(previousProperty.DefaultValue, currentProperty.DefaultValue, StringComparison.Ordinal))
                {
                    differences.Add(CreateDifference("property-default-changed", componentName, string.Empty, propertyPath, previousProperty.DefaultValue, currentProperty.DefaultValue, [], [], previous, current, previousComponent, currentComponent));
                }

                var addedEnumValues = currentProperty.EnumValues.Except(previousProperty.EnumValues, StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList();
                var removedEnumValues = previousProperty.EnumValues.Except(currentProperty.EnumValues, StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList();
                if (addedEnumValues.Count > 0 || removedEnumValues.Count > 0)
                {
                    differences.Add(CreateDifference("property-enum-changed", componentName, string.Empty, propertyPath, string.Join(", ", previousProperty.EnumValues), string.Join(", ", currentProperty.EnumValues), addedEnumValues, removedEnumValues, previous, current, previousComponent, currentComponent));
                }

                var previousConstraint = BuildConstraintSignature(previousProperty);
                var currentConstraint = BuildConstraintSignature(currentProperty);
                if (!string.Equals(previousConstraint, currentConstraint, StringComparison.Ordinal))
                {
                    differences.Add(CreateDifference("property-applicability-or-constraint-changed", componentName, string.Empty, propertyPath, previousConstraint, currentConstraint, [], [], previous, current, previousComponent, currentComponent));
                }
            }

            var previousExamples = string.Join("\n---\n", previousComponent.CanonicalExamples);
            var currentExamples = string.Join("\n---\n", currentComponent.CanonicalExamples);
            if (!string.Equals(previousExamples, currentExamples, StringComparison.Ordinal))
            {
                differences.Add(CreateDifference("canonical-example-changed", componentName, string.Empty, string.Empty, previousExamples, currentExamples, [], [], previous, current, previousComponent, currentComponent));
            }
        }

        return new OracleApexLanguageReferenceDiffReport
        {
            FromApexVersion = previous.ApexVersion,
            ToApexVersion = current.ApexVersion,
            FromGrammarVersion = previous.GrammarVersion,
            ToGrammarVersion = current.GrammarVersion,
            FromProvenance = previous.Provenance,
            ToProvenance = current.Provenance,
            Differences = differences,
            AtlasCompatibility = BuildCompatibilityDiff(previousCompatibility, currentCompatibility),
        };
    }

    public string BuildMarkdown(OracleApexLanguageReferenceDiffReport diff)
    {
        var lines = new List<string>
        {
            "# APEXlang Language Reference Diff",
            string.Empty,
            $"- From version: {diff.FromApexVersion}",
            $"- To version: {diff.ToApexVersion}",
            $"- Total differences: {diff.Differences.Count}",
            $"- Atlas compatibility warnings: {diff.AtlasCompatibility.PreviousWarningCount} -> {diff.AtlasCompatibility.CurrentWarningCount}",
            string.Empty,
        };

        AddSection(lines, diff, "Components", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "component-added", "component-removed" });
        AddSection(lines, diff, "Relationships", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "parent-added", "parent-removed", "child-added", "child-removed" });
        AddSection(lines, diff, "Properties", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "property-added", "property-removed", "property-type-changed", "property-required-changed", "property-default-changed", "property-enum-changed", "property-applicability-or-constraint-changed" });
        AddSection(lines, diff, "Documentation", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "documentation-anchor-changed", "canonical-example-changed" });

        lines.Add("## Atlas Compatibility");
        lines.Add(string.Empty);
        lines.Add(diff.AtlasCompatibility.DriftIncreased
            ? $"- Drift increased from {diff.AtlasCompatibility.PreviousWarningCount} to {diff.AtlasCompatibility.CurrentWarningCount} warnings."
            : $"- Drift did not increase ({diff.AtlasCompatibility.PreviousWarningCount} -> {diff.AtlasCompatibility.CurrentWarningCount}).");

        foreach (var warning in diff.AtlasCompatibility.AddedWarnings.Take(10))
        {
            lines.Add($"- Added warning: {FormatCompatibilityWarning(warning)}");
        }

        return string.Join("\n", lines) + "\n";
    }

    private static void AddSection(List<string> lines, OracleApexLanguageReferenceDiffReport diff, string title, IReadOnlySet<string> kinds)
    {
        var items = diff.Differences.Where(item => kinds.Contains(item.Kind)).ToList();
        if (items.Count == 0)
        {
            return;
        }

        lines.Add($"## {title}");
        lines.Add(string.Empty);
        foreach (var item in items.Take(30))
        {
            lines.Add($"- {FormatDifference(item)}");
        }

        lines.Add(string.Empty);
    }

    private static string FormatDifference(OracleApexLanguageReferenceDifference difference)
    {
        var scope = string.IsNullOrWhiteSpace(difference.PropertyPath)
            ? difference.ComponentName
            : $"{difference.ComponentName}.{difference.PropertyPath}";
        return difference.Kind switch
        {
            "component-added" => $"{scope} was added.",
            "component-removed" => $"{scope} was removed.",
            "parent-added" => $"{scope} now allows parent '{difference.RelatedComponentName}'.",
            "parent-removed" => $"{scope} no longer allows parent '{difference.RelatedComponentName}'.",
            "child-added" => $"{scope} now allows child '{difference.RelatedComponentName}'.",
            "child-removed" => $"{scope} no longer allows child '{difference.RelatedComponentName}'.",
            "property-added" => $"{scope} was added with type '{difference.AfterValue}'.",
            "property-removed" => $"{scope} was removed.",
            "property-type-changed" => $"{scope} changed type from '{difference.BeforeValue}' to '{difference.AfterValue}'.",
            "property-required-changed" => $"{scope} changed from {difference.BeforeValue} to {difference.AfterValue}.",
            "property-default-changed" => $"{scope} default changed from '{difference.BeforeValue}' to '{difference.AfterValue}'.",
            "property-enum-changed" => $"{scope} enum changed; added [{string.Join(", ", difference.AddedValues)}], removed [{string.Join(", ", difference.RemovedValues)}].",
            "property-applicability-or-constraint-changed" => $"{scope} applicability or constraints changed.",
            "documentation-anchor-changed" => $"{scope} documentation anchor changed from '{difference.BeforeValue}' to '{difference.AfterValue}'.",
            "canonical-example-changed" => $"{scope} canonical example changed.",
            _ => $"{scope} changed.",
        };
    }

    private static string FormatCompatibilityWarning(OracleApexLanguageReferenceCompatibilityWarning warning)
        => string.IsNullOrWhiteSpace(warning.PropertyName)
            ? warning.Message
            : $"{warning.ComponentName}.{warning.PropertyName}: {warning.Message}";

    private static string BuildConstraintSignature(OracleApexLanguageReferenceProperty property)
    {
        var builder = new StringBuilder();
        builder.Append($"appliesWhen={property.AppliesWhen};");
        builder.Append($"maxLength={property.MaxLength};");
        builder.Append($"numericBounds={property.NumericBounds};");
        builder.Append($"validation={property.ValidationConstraint};");
        return builder.ToString();
    }

    private static Dictionary<string, OracleApexLanguageReferenceProperty> FlattenProperties(OracleApexLanguageReferenceComponent component)
        => component.DirectProperties
            .Concat(component.PropertyGroups.SelectMany(group => group.Properties))
            .ToDictionary(item => item.PropertyPath, item => item, StringComparer.OrdinalIgnoreCase);

    private static void AddSetDifferences(
        ICollection<OracleApexLanguageReferenceDifference> differences,
        string addedKind,
        string removedKind,
        string componentName,
        IReadOnlyList<string> previousValues,
        IReadOnlyList<string> currentValues,
        OracleApexLanguageReferenceCatalog previous,
        OracleApexLanguageReferenceCatalog current,
        OracleApexLanguageReferenceComponent previousComponent,
        OracleApexLanguageReferenceComponent currentComponent)
    {
        foreach (var added in currentValues.Except(previousValues, StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            differences.Add(CreateDifference(addedKind, componentName, added, string.Empty, string.Empty, string.Empty, [], [], previous, current, previousComponent, currentComponent));
        }

        foreach (var removed in previousValues.Except(currentValues, StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            differences.Add(CreateDifference(removedKind, componentName, removed, string.Empty, string.Empty, string.Empty, [], [], previous, current, previousComponent, currentComponent));
        }
    }

    private static OracleApexLanguageReferenceDifference CreateDifference(
        string kind,
        string componentName,
        string relatedComponentName,
        string propertyPath,
        string beforeValue,
        string afterValue,
        IReadOnlyList<string> addedValues,
        IReadOnlyList<string> removedValues,
        OracleApexLanguageReferenceCatalog previous,
        OracleApexLanguageReferenceCatalog current,
        OracleApexLanguageReferenceComponent? previousComponent,
        OracleApexLanguageReferenceComponent? currentComponent)
        => new()
        {
            Kind = kind,
            ComponentName = componentName,
            RelatedComponentName = relatedComponentName,
            PropertyPath = propertyPath,
            BeforeValue = beforeValue,
            AfterValue = afterValue,
            AddedValues = addedValues,
            RemovedValues = removedValues,
            Provenance = new OracleApexLanguageReferenceDifferenceProvenance
            {
                FromCatalog = previous.Provenance,
                ToCatalog = current.Provenance,
                FromDocumentationReference = BuildDocumentationReference(previous.Provenance.SourceLocation, previousComponent?.DocumentationAnchor),
                ToDocumentationReference = BuildDocumentationReference(current.Provenance.SourceLocation, currentComponent?.DocumentationAnchor),
            },
        };

    private static OracleApexLanguageReferenceAtlasCompatibilityDiff BuildCompatibilityDiff(
        OracleApexLanguageReferenceCompatibilityResult? previousCompatibility,
        OracleApexLanguageReferenceCompatibilityResult? currentCompatibility)
    {
        previousCompatibility ??= new OracleApexLanguageReferenceCompatibilityResult();
        currentCompatibility ??= new OracleApexLanguageReferenceCompatibilityResult();

        var previousWarnings = previousCompatibility.Warnings.ToDictionary(BuildCompatibilityKey, item => item, StringComparer.OrdinalIgnoreCase);
        var currentWarnings = currentCompatibility.Warnings.ToDictionary(BuildCompatibilityKey, item => item, StringComparer.OrdinalIgnoreCase);

        return new OracleApexLanguageReferenceAtlasCompatibilityDiff
        {
            PreviousWarningCount = previousCompatibility.Warnings.Count,
            CurrentWarningCount = currentCompatibility.Warnings.Count,
            DriftIncreased = currentCompatibility.Warnings.Count > previousCompatibility.Warnings.Count,
            AddedWarnings = currentWarnings.Where(item => !previousWarnings.ContainsKey(item.Key)).Select(item => item.Value).OrderBy(item => item.ComponentName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.PropertyName, StringComparer.OrdinalIgnoreCase).ToList(),
            RemovedWarnings = previousWarnings.Where(item => !currentWarnings.ContainsKey(item.Key)).Select(item => item.Value).OrderBy(item => item.ComponentName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.PropertyName, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    private static string BuildCompatibilityKey(OracleApexLanguageReferenceCompatibilityWarning warning)
        => $"{warning.ComponentName}|{warning.PropertyName}|{warning.Message}|{warning.OfficialValue}|{warning.AtlasValue}";

    private static string BuildDocumentationReference(string sourceLocation, string? anchor)
        => string.IsNullOrWhiteSpace(anchor) ? sourceLocation : $"{sourceLocation.TrimEnd('#')}#{anchor}";
}

public sealed class OracleApexLanguageReferenceWorkspaceImpactAnalyzer
{
    private readonly OracleApexLanguageReferenceDiffReport _diff;

    public OracleApexLanguageReferenceWorkspaceImpactAnalyzer(OracleApexLanguageReferenceDiffReport diff)
        => _diff = diff;

    public IReadOnlyList<OracleApexWorkspaceIndexDiagnostic> Analyze(OracleApexSemanticModel semanticModel)
    {
        var diagnostics = new List<OracleApexWorkspaceIndexDiagnostic>();
        var application = semanticModel.Application;
        var declaredVersion = application?.GetProperty("apexlang-version") ?? application?.GetProperty("apex-version");
        if (!string.IsNullOrWhiteSpace(declaredVersion) && !string.Equals(declaredVersion, _diff.ToApexVersion, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateWorkspaceDiagnostic(
                "reference-version-mismatch",
                $"Project metadata targets APEXlang version '{declaredVersion}', but the current normalized reference is '{_diff.ToApexVersion}' and the built-in diff baseline is '{_diff.FromApexVersion}'.",
                application));
        }

        foreach (var node in semanticModel.Nodes)
        {
            foreach (var removedProperty in _diff.Differences.Where(item => item.Kind == "property-removed" && string.Equals(item.ComponentName, node.SemanticType, StringComparison.OrdinalIgnoreCase)))
            {
                if (node.Properties.ContainsKey(removedProperty.PropertyPath))
                {
                    diagnostics.Add(CreateNodeDiagnostic(
                        "reference-property-removed",
                        $"Property '{removedProperty.PropertyPath}' on component '{node.Identifier}' was removed from the official APEXlang reference between versions '{_diff.FromApexVersion}' and '{_diff.ToApexVersion}'.",
                        node));
                }
            }

            foreach (var requiredProperty in _diff.Differences.Where(item => item.Kind == "property-required-changed" && string.Equals(item.ComponentName, node.SemanticType, StringComparison.OrdinalIgnoreCase) && string.Equals(item.BeforeValue, "optional", StringComparison.OrdinalIgnoreCase) && string.Equals(item.AfterValue, "required", StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrWhiteSpace(node.GetProperty(requiredProperty.PropertyPath)))
                {
                    diagnostics.Add(CreateNodeDiagnostic(
                        "reference-property-became-required",
                        $"Property '{requiredProperty.PropertyPath}' on component '{node.Identifier}' became required in official APEXlang version '{_diff.ToApexVersion}'.",
                        node));
                }
            }

            foreach (var enumChange in _diff.Differences.Where(item => item.Kind == "property-enum-changed" && item.RemovedValues.Count > 0 && string.Equals(item.ComponentName, node.SemanticType, StringComparison.OrdinalIgnoreCase)))
            {
                var propertyValue = node.GetProperty(enumChange.PropertyPath);
                if (!string.IsNullOrWhiteSpace(propertyValue) && enumChange.RemovedValues.Contains(propertyValue, StringComparer.OrdinalIgnoreCase))
                {
                    diagnostics.Add(CreateNodeDiagnostic(
                        "reference-enum-value-removed",
                        $"Value '{propertyValue}' for property '{enumChange.PropertyPath}' on component '{node.Identifier}' is no longer valid in official APEXlang version '{_diff.ToApexVersion}'.",
                        node));
                }
            }
        }

        var removedComponents = _diff.Differences.Where(item => item.Kind == "component-removed").Select(item => NormalizeComponentName(item.ComponentName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var diagnostic in semanticModel.Diagnostics.Where(item => item.Code == "unknown-component"))
        {
            var componentName = ExtractQuotedValue(diagnostic.Message);
            if (removedComponents.Contains(componentName))
            {
                diagnostics.Add(new OracleApexWorkspaceIndexDiagnostic
                {
                    Severity = "Warning",
                    Code = "reference-component-removed",
                    Message = $"Component '{componentName}' was removed from the official APEXlang reference between versions '{_diff.FromApexVersion}' and '{_diff.ToApexVersion}'.",
                    SourceFile = diagnostic.SourceFile,
                    Line = diagnostic.Line,
                    Column = diagnostic.Column,
                });
            }
        }

        if (_diff.AtlasCompatibility.DriftIncreased)
        {
            diagnostics.Add(CreateWorkspaceDiagnostic(
                "reference-atlas-drift-increased",
                $"Atlas and official reference drift increased from {_diff.AtlasCompatibility.PreviousWarningCount} to {_diff.AtlasCompatibility.CurrentWarningCount} compatibility warnings.",
                application));
        }

        return diagnostics;
    }

    public IReadOnlyList<string> BuildWarnings(OracleApexWorkspaceIndex index)
        => index.Diagnostics
            .Where(item => item.Code.StartsWith("reference-", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Message)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();

    public OracleApexLanguageReferenceWorkspaceCompatibilitySummary BuildWorkspaceSummary(
        OracleApexWorkspaceIndex index,
        string? atlasVersion,
        string diffJsonPath,
        string diffMarkdownPath,
        int maxFindings = 10)
    {
        var findings = BuildWorkspaceFindings(index)
            .GroupBy(BuildFindingKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(Math.Max(0, maxFindings))
            .ToList();
        var projectVersion = GetProjectVersion(index.SemanticModel);
        return new OracleApexLanguageReferenceWorkspaceCompatibilitySummary
        {
            ProjectVersion = projectVersion,
            ReferenceVersion = _diff.ToApexVersion,
            PreviousReferenceVersion = _diff.FromApexVersion,
            AtlasVersion = string.IsNullOrWhiteSpace(atlasVersion) ? "unknown" : atlasVersion,
            Status = string.IsNullOrWhiteSpace(projectVersion) || string.Equals(projectVersion, _diff.ToApexVersion, StringComparison.OrdinalIgnoreCase)
                ? (findings.Count == 0 ? "Compatible" : "Compatible with warnings")
                : "Version mismatch",
            RelevantFindingCount = findings.Count,
            Findings = findings,
            DiffJsonPath = diffJsonPath,
            DiffMarkdownPath = diffMarkdownPath,
        };
    }

    public OracleApexLanguageReferencePlanCompatibilitySummary AnalyzePlan(OracleApexWorkspaceIndex index, OracleApexEditPlan plan, int maxFindings = 10)
    {
        var findings = new List<OracleApexLanguageReferenceCompatibilityFinding>();
        var projectVersion = GetProjectVersion(index.SemanticModel);
        var projectTargetsCurrentReference = string.IsNullOrWhiteSpace(projectVersion) || string.Equals(projectVersion, _diff.ToApexVersion, StringComparison.OrdinalIgnoreCase);

        foreach (var workspaceFinding in BuildWorkspaceFindings(index))
        {
            if (PlanTouchesFinding(plan, workspaceFinding))
            {
                findings.Add(workspaceFinding);
            }
        }

        foreach (var operation in plan.Operations)
        {
            if (!projectTargetsCurrentReference)
            {
                var newerComponent = _diff.Differences.FirstOrDefault(item => item.Kind == "component-added" && string.Equals(item.ComponentName, operation.TargetComponentType, StringComparison.OrdinalIgnoreCase));
                if (newerComponent is not null)
                {
                    findings.Add(CreateFinding(
                        "plan-component-newer-version-only",
                        "component",
                        operation.TargetComponentType,
                        string.Empty,
                        $"Plan uses component '{operation.TargetComponentType}', which is only available in reference version '{_diff.ToApexVersion}' while the workspace targets '{projectVersion}'.",
                        $"Use a component supported by APEXlang {projectVersion} or upgrade the workspace metadata before applying this plan.",
                        blockingExecution: true,
                        newerComponent.Provenance));
                }
            }

            var removedComponent = _diff.Differences.FirstOrDefault(item => item.Kind == "component-removed" && string.Equals(item.ComponentName, operation.TargetComponentType, StringComparison.OrdinalIgnoreCase));
            if (removedComponent is not null)
            {
                findings.Add(CreateFinding(
                    "plan-component-removed",
                    "component",
                    operation.TargetComponentType,
                    string.Empty,
                    $"Plan uses component '{operation.TargetComponentType}', which was removed from the official APEXlang reference.",
                    "Replace the removed component with a supported construct before applying this plan.",
                    blockingExecution: true,
                    removedComponent.Provenance));
            }

            foreach (var property in operation.Properties)
            {
                var removedProperty = _diff.Differences.FirstOrDefault(item => item.Kind == "property-removed" && string.Equals(item.ComponentName, operation.TargetComponentType, StringComparison.OrdinalIgnoreCase) && string.Equals(item.PropertyPath, property.Key, StringComparison.OrdinalIgnoreCase));
                if (removedProperty is not null)
                {
                    findings.Add(CreateFinding(
                        "plan-property-removed",
                        "property",
                        operation.TargetComponentType,
                        property.Key,
                        $"Plan uses property '{property.Key}' on component '{operation.TargetComponentType}', but that property was removed from the official APEXlang reference.",
                        $"Remove property '{property.Key}' or replace it with a supported property before applying this plan.",
                        blockingExecution: true,
                        removedProperty.Provenance));
                }

                var enumChange = _diff.Differences.FirstOrDefault(item => item.Kind == "property-enum-changed" && string.Equals(item.ComponentName, operation.TargetComponentType, StringComparison.OrdinalIgnoreCase) && string.Equals(item.PropertyPath, property.Key, StringComparison.OrdinalIgnoreCase) && item.RemovedValues.Contains(property.Value, StringComparer.OrdinalIgnoreCase));
                if (enumChange is not null)
                {
                    findings.Add(CreateFinding(
                        "plan-enum-value-removed",
                        "enum",
                        operation.TargetComponentType,
                        property.Key,
                        $"Plan uses enum value '{property.Value}' for property '{property.Key}' on component '{operation.TargetComponentType}', but that value is no longer valid in reference version '{_diff.ToApexVersion}'.",
                        $"Choose one of the supported enum values in '{enumChange.AfterValue}' before applying this plan.",
                        blockingExecution: true,
                        enumChange.Provenance));
                }
            }

            var requiredChanges = _diff.Differences.Where(item => item.Kind == "property-required-changed" && string.Equals(item.ComponentName, operation.TargetComponentType, StringComparison.OrdinalIgnoreCase) && string.Equals(item.BeforeValue, "optional", StringComparison.OrdinalIgnoreCase) && string.Equals(item.AfterValue, "required", StringComparison.OrdinalIgnoreCase));
            foreach (var requiredChange in requiredChanges)
            {
                if (!operation.Properties.ContainsKey(requiredChange.PropertyPath))
                {
                    findings.Add(CreateFinding(
                        "plan-property-became-required",
                        "property",
                        operation.TargetComponentType,
                        requiredChange.PropertyPath,
                        $"Property '{requiredChange.PropertyPath}' is required for component '{operation.TargetComponentType}' in reference version '{_diff.ToApexVersion}'.",
                        $"Set '{requiredChange.PropertyPath}' explicitly or rely on a validated generator path before applying this plan.",
                        blockingExecution: false,
                        requiredChange.Provenance));
                }
            }
        }

        if (_diff.AtlasCompatibility.DriftIncreased)
        {
            findings.Add(CreateFinding(
                "plan-atlas-reference-drift",
                "atlas",
                string.Empty,
                string.Empty,
                $"Atlas and the official reference currently disagree more than before ({_diff.AtlasCompatibility.PreviousWarningCount} -> {_diff.AtlasCompatibility.CurrentWarningCount} warnings).",
                "Treat SQLcl validation as authoritative for this plan.",
                blockingExecution: false,
                CreateGlobalProvenance()));
        }

        var normalizedFindings = findings
            .GroupBy(BuildFindingKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(Math.Max(0, maxFindings))
            .ToList();
        var sqlclImportant = normalizedFindings.Any(item => item.Category is "property" or "enum" or "atlas") || !projectTargetsCurrentReference;
        var shouldBlock = normalizedFindings.Any(item => item.BlockingExecution) && !HasSafeAlternative(plan);

        return new OracleApexLanguageReferencePlanCompatibilitySummary
        {
            TargetApexlangVersion = _diff.ToApexVersion,
            CompatibilityStatus = shouldBlock ? "Blocked" : normalizedFindings.Count == 0 ? "Compatible" : "Review required",
            SqlclValidationIsEspeciallyImportant = sqlclImportant,
            ShouldBlockExecution = shouldBlock,
            Findings = normalizedFindings,
        };
    }

    private List<OracleApexLanguageReferenceCompatibilityFinding> BuildWorkspaceFindings(OracleApexWorkspaceIndex index)
        => index.Diagnostics
            .Where(item => item.Code.StartsWith("reference-", StringComparison.OrdinalIgnoreCase))
            .Select(CreateFindingFromDiagnostic)
            .Where(item => item is not null)
            .Cast<OracleApexLanguageReferenceCompatibilityFinding>()
            .ToList();

    private OracleApexLanguageReferenceCompatibilityFinding? CreateFindingFromDiagnostic(OracleApexWorkspaceIndexDiagnostic diagnostic)
    {
        var provenance = ResolveProvenance(diagnostic);
        return diagnostic.Code switch
        {
            "reference-version-mismatch" => CreateFinding(diagnostic.Code, "version", string.Empty, string.Empty, diagnostic.Message, "Align the project metadata version with the active normalized reference before relying on new constructs.", false, provenance),
            "reference-property-removed" => CreateFinding(diagnostic.Code, "property", diagnostic.SemanticType, ExtractNamedValue(diagnostic.Message, "Property"), diagnostic.Message, "Remove the property or replace it with a supported property through a reviewed semantic plan.", true, provenance),
            "reference-property-became-required" => CreateFinding(diagnostic.Code, "property", diagnostic.SemanticType, ExtractNamedValue(diagnostic.Message, "Property"), diagnostic.Message, "Add the required property through the semantic planner or a review-only migration path before deployment.", false, provenance),
            "reference-enum-value-removed" => CreateFinding(diagnostic.Code, "enum", diagnostic.SemanticType, ExtractNamedValueAfter(diagnostic.Message, "property"), diagnostic.Message, "Replace the invalid enum value with a supported value and validate with SQLcl.", true, provenance),
            "reference-component-removed" => CreateFinding(diagnostic.Code, "component", ExtractNamedValue(diagnostic.Message, "Component"), string.Empty, diagnostic.Message, "Replace the removed component with a supported construct before deployment.", true, provenance),
            "reference-atlas-drift-increased" => CreateFinding(diagnostic.Code, "atlas", string.Empty, string.Empty, diagnostic.Message, "Review Atlas drift details and treat SQLcl validation as authoritative.", false, provenance),
            _ => null,
        };
    }

    private OracleApexLanguageReferenceDifferenceProvenance ResolveProvenance(OracleApexWorkspaceIndexDiagnostic diagnostic)
    {
        var propertyPath = ExtractNamedValue(diagnostic.Message, "Property");
        var componentName = string.IsNullOrWhiteSpace(diagnostic.SemanticType)
            ? ExtractNamedValue(diagnostic.Message, "Component")
            : diagnostic.SemanticType;
        OracleApexLanguageReferenceDifference? difference = diagnostic.Code switch
        {
            "reference-property-removed" => _diff.Differences.FirstOrDefault(item => item.Kind == "property-removed" && string.Equals(item.ComponentName, componentName, StringComparison.OrdinalIgnoreCase) && string.Equals(item.PropertyPath, propertyPath, StringComparison.OrdinalIgnoreCase)),
            "reference-property-became-required" => _diff.Differences.FirstOrDefault(item => item.Kind == "property-required-changed" && string.Equals(item.ComponentName, componentName, StringComparison.OrdinalIgnoreCase) && string.Equals(item.PropertyPath, propertyPath, StringComparison.OrdinalIgnoreCase)),
            "reference-enum-value-removed" => _diff.Differences.FirstOrDefault(item => item.Kind == "property-enum-changed" && string.Equals(item.ComponentName, componentName, StringComparison.OrdinalIgnoreCase) && string.Equals(item.PropertyPath, ExtractNamedValueAfter(diagnostic.Message, "property"), StringComparison.OrdinalIgnoreCase)),
            "reference-component-removed" => _diff.Differences.FirstOrDefault(item => item.Kind == "component-removed" && string.Equals(NormalizeComponentName(item.ComponentName), NormalizeComponentName(ExtractNamedValue(diagnostic.Message, "Component")), StringComparison.OrdinalIgnoreCase)),
            _ => null,
        };

        return difference?.Provenance ?? CreateGlobalProvenance();
    }

    private OracleApexLanguageReferenceCompatibilityFinding CreateFinding(
        string code,
        string category,
        string componentName,
        string propertyPath,
        string message,
        string suggestedMigration,
        bool blockingExecution,
        OracleApexLanguageReferenceDifferenceProvenance provenance)
        => new()
        {
            Code = code,
            Category = category,
            ComponentName = componentName,
            PropertyPath = propertyPath,
            Message = message,
            SuggestedMigration = suggestedMigration,
            BlockingExecution = blockingExecution,
            Provenance = provenance,
        };

    private OracleApexLanguageReferenceDifferenceProvenance CreateGlobalProvenance()
        => new()
        {
            FromCatalog = _diff.FromProvenance,
            ToCatalog = _diff.ToProvenance,
            FromDocumentationReference = _diff.FromProvenance.SourceLocation,
            ToDocumentationReference = _diff.ToProvenance.SourceLocation,
        };

    private static bool PlanTouchesFinding(OracleApexEditPlan plan, OracleApexLanguageReferenceCompatibilityFinding finding)
        => plan.Operations.Any(operation =>
            (string.IsNullOrWhiteSpace(finding.ComponentName) || string.Equals(operation.TargetComponentType, finding.ComponentName, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(finding.PropertyPath) || operation.Properties.ContainsKey(finding.PropertyPath) || operation.Properties.Keys.Any(key => string.Equals(key, finding.PropertyPath, StringComparison.OrdinalIgnoreCase))));

    private static bool HasSafeAlternative(OracleApexEditPlan plan)
        => plan.Alternatives.Any(item => item.IsRecommended);

    private static string BuildFindingKey(OracleApexLanguageReferenceCompatibilityFinding finding)
        => $"{finding.Code}|{finding.ComponentName}|{finding.PropertyPath}";

    private static string GetProjectVersion(OracleApexSemanticModel semanticModel)
        => semanticModel.Application?.GetProperty("apexlang-version") ?? semanticModel.Application?.GetProperty("apex-version") ?? string.Empty;

    private static string ExtractNamedValue(string message, string prefix)
    {
        var marker = prefix + " '";
        var start = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return string.Empty;
        }

        start += marker.Length;
        var end = message.IndexOf('\'', start);
        return end > start ? message[start..end] : string.Empty;
    }

    private static string ExtractNamedValueAfter(string message, string token)
    {
        var tokenIndex = message.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (tokenIndex < 0)
        {
            return string.Empty;
        }

        var quoteStart = message.IndexOf('\'', tokenIndex);
        var quoteEnd = quoteStart >= 0 ? message.IndexOf('\'', quoteStart + 1) : -1;
        return quoteStart >= 0 && quoteEnd > quoteStart ? message[(quoteStart + 1)..quoteEnd] : string.Empty;
    }

    private static OracleApexWorkspaceIndexDiagnostic CreateWorkspaceDiagnostic(string code, string message, OracleApexSemanticNode? application)
        => new()
        {
            Severity = "Warning",
            Code = code,
            Message = message,
            SourceFile = application?.SourceFile ?? string.Empty,
            Line = application?.Line ?? 0,
            Column = application?.Column ?? 0,
            NodeId = application?.NodeId ?? string.Empty,
            SemanticType = application?.SemanticType ?? string.Empty,
        };

    private static OracleApexWorkspaceIndexDiagnostic CreateNodeDiagnostic(string code, string message, OracleApexSemanticNode node)
        => new()
        {
            Severity = "Warning",
            Code = code,
            Message = message,
            SourceFile = node.SourceFile,
            Line = node.Line,
            Column = node.Column,
            NodeId = node.NodeId,
            SemanticType = node.SemanticType,
        };

    private static string ExtractQuotedValue(string message)
    {
        var first = message.IndexOf('\'', StringComparison.Ordinal);
        var second = first >= 0 ? message.IndexOf('\'', first + 1) : -1;
        return first >= 0 && second > first ? NormalizeComponentName(message[(first + 1)..second]) : string.Empty;
    }

    private static string NormalizeComponentName(string value)
        => value.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();
}
