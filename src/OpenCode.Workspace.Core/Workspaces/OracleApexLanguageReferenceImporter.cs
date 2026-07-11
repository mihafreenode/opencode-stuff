using System.Text.RegularExpressions;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class OracleApexLanguageReferenceImporter
{
    private static readonly Regex TocLinePattern = new(@"^(?<indent>\s*)-\s+\[(?<name>[^\]]+)\]\(#(?<anchor>[^\)]+)\)", RegexOptions.Compiled);
    private static readonly Regex ComponentHeadingPattern = new(@"^###\s+`(?<name>[^`]+)`$", RegexOptions.Compiled);
    private static readonly Regex PropertyHeaderPattern = new(@"^`(?<name>[^`]+)`$", RegexOptions.Compiled);
    private static readonly Regex EnumPattern = new(@"enum=\\?\[?(?<values>[^\]]+)\]?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DetailPattern = new(@"(?<key>maxLen|min|max|Applies when|language)=(?<value>.+?)(?:;|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EbnfComponentPattern = new("^<(?<name>[a-zA-Z0-9\\-]+)>\\s*::=\\s*\"(?<keyword>[^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex EbnfChildPattern = new(@"^<(?<name>[a-zA-Z0-9\-]+)-child-component>\s*::=\s*(?<body>.+)$", RegexOptions.Compiled);
    private static readonly Regex EbnfPropertyPattern = new("^<(?<component>[a-zA-Z0-9\\-]+)(?:-[a-zA-Z0-9\\-]+)?-property>\\s*::=\\s*\"(?<name>[^\"]+)\"\\s*:\\s*<ws>\\s*(?<value>.+?)\\s*\\(\\*\\s*(?<details>.+)\\*\\)$", RegexOptions.Compiled);
    private static readonly Regex EbnfAlternativePropertyPattern = new("^\\|\\s*\"(?<name>[^\"]+)\"\\s*:\\s*<ws>\\s*(?<value>.+?)\\s*\\(\\*\\s*(?<details>.+)\\*\\)$", RegexOptions.Compiled);
    private static readonly Regex ChildReferencePattern = new(@"<(?<name>[a-zA-Z0-9\-]+)>", RegexOptions.Compiled);

    public OracleApexLanguageReferenceCatalog Import(string markdownReference, string ebnfGrammar, OracleApexLanguageReferenceProvenance provenance)
    {
        var components = ParseToc(markdownReference);
        ParseComponentSections(markdownReference, components);
        ParseEbnf(ebnfGrammar, components);

        return new OracleApexLanguageReferenceCatalog
        {
            ApexVersion = provenance.ApexVersion,
            GrammarVersion = ExtractGrammarVersion(ebnfGrammar, provenance.ApexVersion),
            Provenance = provenance,
            Components = components.Values.ToDictionary(item => item.CanonicalName, item => (OracleApexLanguageReferenceComponent)item, StringComparer.OrdinalIgnoreCase),
        };
    }

    private static Dictionary<string, MutableComponent> ParseToc(string markdown)
    {
        var components = new Dictionary<string, MutableComponent>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<(int Level, string Name)>();
        foreach (var line in markdown.Split('\n'))
        {
            var match = TocLinePattern.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var canonicalName = NormalizeComponentName(match.Groups["name"].Value.Trim());
            var level = match.Groups["indent"].Value.Length / 4;
            while (stack.Count > 0 && stack.Peek().Level >= level)
            {
                stack.Pop();
            }

            if (!components.TryGetValue(canonicalName, out var component))
            {
                component = new MutableComponent
                {
                    CanonicalName = canonicalName,
                    DisplayName = match.Groups["name"].Value.Trim(),
                    DocumentationAnchor = match.Groups["anchor"].Value.Trim(),
                };
                components[canonicalName] = component;
            }

            if (stack.Count > 0)
            {
                component.ParentComponents.Add(stack.Peek().Name);
                if (components.TryGetValue(stack.Peek().Name, out var parent))
                {
                    parent.ChildComponents.Add(canonicalName);
                }
            }

            stack.Push((level, canonicalName));
        }

        return components;
    }

    private static void ParseComponentSections(string markdown, IDictionary<string, MutableComponent> components)
    {
        MutableComponent? current = null;
        string? currentGroup = null;
        var mode = string.Empty;
        var lines = markdown.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            var heading = ComponentHeadingPattern.Match(line.Trim());
            if (heading.Success)
            {
                var componentName = NormalizeComponentName(heading.Groups["name"].Value.Trim());
                components.TryGetValue(componentName, out current);
                currentGroup = null;
                mode = string.Empty;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed == "**Direct Properties:**")
            {
                currentGroup = null;
                mode = "properties";
                continue;
            }

            if (trimmed == "**Property Groups:**")
            {
                mode = "groups";
                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (index + 1 < lines.Length && !string.IsNullOrWhiteSpace(lines[index + 1]))
                {
                    var exampleLines = new List<string>();
                    index++;
                    while (index < lines.Length && !lines[index].Trim().StartsWith("```", StringComparison.Ordinal))
                    {
                        exampleLines.Add(lines[index].TrimEnd('\r'));
                        index++;
                    }

                    if (exampleLines.Count > 0)
                    {
                        current.CanonicalExamples.Add(string.Join("\n", exampleLines));
                    }
                }

                continue;
            }

            if (mode == "groups" && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                currentGroup = trimmed[2..].Trim();
                EnsureGroup(current, currentGroup);
                continue;
            }

            if ((mode == "properties" || !string.IsNullOrWhiteSpace(currentGroup)) && PropertyHeaderPattern.IsMatch(trimmed))
            {
                var propertyName = PropertyHeaderPattern.Match(trimmed).Groups["name"].Value.Trim();
                var type = NextNonEmpty(lines, ref index);
                var required = string.Equals(NextNonEmpty(lines, ref index), "Yes", StringComparison.OrdinalIgnoreCase);
                var defaultValue = NextNonEmpty(lines, ref index);
                var details = NextNonEmpty(lines, ref index);
                var property = BuildProperty(propertyName, currentGroup, type, required, defaultValue, details);
                if (string.IsNullOrWhiteSpace(currentGroup))
                {
                    current.DirectProperties[property.PropertyPath] = property;
                }
                else
                {
                    EnsureGroup(current, currentGroup).Properties[property.PropertyPath] = property;
                }
            }
        }
    }

    private static void ParseEbnf(string ebnf, IDictionary<string, MutableComponent> components)
    {
        MutableComponent? currentPropertyComponent = null;
        foreach (var rawLine in ebnf.Split('\n'))
        {
            var line = rawLine.Trim();
            var componentMatch = EbnfComponentPattern.Match(line);
            if (componentMatch.Success)
            {
                var componentName = NormalizeComponentName(componentMatch.Groups["keyword"].Value.Trim());
                if (!components.TryGetValue(componentName, out var component))
                {
                    component = new MutableComponent { CanonicalName = componentName, DisplayName = componentMatch.Groups["keyword"].Value.Trim() };
                    components[componentName] = component;
                }

                currentPropertyComponent = component;
                continue;
            }

            var childMatch = EbnfChildPattern.Match(line);
            if (childMatch.Success)
            {
                var componentName = NormalizeComponentName(childMatch.Groups["name"].Value.Trim());
                if (components.TryGetValue(componentName, out var component))
                {
                    foreach (Match childRef in ChildReferencePattern.Matches(childMatch.Groups["body"].Value))
                    {
                        var childName = NormalizeComponentName(childRef.Groups["name"].Value.Trim());
                        component.ChildComponents.Add(childName);
                        if (!components.TryGetValue(childName, out var childComponent))
                        {
                            childComponent = new MutableComponent { CanonicalName = childName, DisplayName = childName };
                            components[childName] = childComponent;
                        }

                        childComponent.ParentComponents.Add(componentName);
                    }
                }

                continue;
            }

            if (currentPropertyComponent is null)
            {
                continue;
            }

            var propertyMatch = EbnfPropertyPattern.Match(line);
            if (propertyMatch.Success)
            {
                AddOrMergeProperty(currentPropertyComponent, BuildPropertyFromEbnf(propertyMatch.Groups["name"].Value, string.Empty, propertyMatch.Groups["value"].Value, propertyMatch.Groups["details"].Value));
                continue;
            }

            var altMatch = EbnfAlternativePropertyPattern.Match(line);
            if (altMatch.Success)
            {
                AddOrMergeProperty(currentPropertyComponent, BuildPropertyFromEbnf(altMatch.Groups["name"].Value, string.Empty, altMatch.Groups["value"].Value, altMatch.Groups["details"].Value));
            }
        }
    }

    private static OracleApexLanguageReferenceProperty BuildProperty(string propertyName, string? groupName, string type, bool required, string defaultValue, string details)
    {
        var enumValues = ParseEnumValues(details);
        var detailMap = ParseDetailMap(details);
        return new OracleApexLanguageReferenceProperty
        {
            Name = propertyName,
            PropertyPath = NormalizePropertyPath(string.IsNullOrWhiteSpace(groupName) ? propertyName : $"{groupName}.{propertyName}"),
            DataType = type,
            Required = required,
            DefaultValue = defaultValue,
            EnumValues = enumValues,
            AppliesWhen = detailMap.TryGetValue("applies when", out var appliesWhen) ? appliesWhen : string.Empty,
            MaxLength = detailMap.TryGetValue("maxLen", out var maxLen) ? maxLen : string.Empty,
            NumericBounds = string.Join(", ", new[]
            {
                detailMap.TryGetValue("min", out var min) ? $"min={min}" : string.Empty,
                detailMap.TryGetValue("max", out var max) ? $"max={max}" : string.Empty,
            }.Where(item => !string.IsNullOrWhiteSpace(item))),
            ValidationConstraint = details,
        };
    }

    private static OracleApexLanguageReferenceProperty BuildPropertyFromEbnf(string propertyName, string groupName, string typeExpression, string details)
    {
        var enumValues = ParseEnumValues(details);
        return new OracleApexLanguageReferenceProperty
        {
            Name = propertyName.Trim(),
            PropertyPath = NormalizePropertyPath(string.IsNullOrWhiteSpace(groupName) ? propertyName.Trim() : $"{groupName}.{propertyName.Trim()}"),
            DataType = typeExpression.Trim(),
            Required = details.Contains("required", StringComparison.OrdinalIgnoreCase),
            DefaultValue = string.Empty,
            EnumValues = enumValues,
            AppliesWhen = ParseDetailMap(details).TryGetValue("applies when", out var appliesWhen) ? appliesWhen : string.Empty,
            MaxLength = ParseDetailMap(details).TryGetValue("maxLen", out var maxLen) ? maxLen : string.Empty,
            NumericBounds = string.Join(", ", new[]
            {
                ParseDetailMap(details).TryGetValue("min", out var min) ? $"min={min}" : string.Empty,
                ParseDetailMap(details).TryGetValue("max", out var max) ? $"max={max}" : string.Empty,
            }.Where(item => !string.IsNullOrWhiteSpace(item))),
            ValidationConstraint = details.Trim(),
        };
    }

    private static string NextNonEmpty(string[] lines, ref int index)
    {
        while (++index < lines.Length)
        {
            var trimmed = lines[index].Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed;
            }
        }

        return string.Empty;
    }

    private static MutableGroup EnsureGroup(MutableComponent component, string groupName)
    {
        if (!component.PropertyGroups.TryGetValue(groupName, out var group))
        {
            group = new MutableGroup { Name = groupName };
            component.PropertyGroups[groupName] = group;
        }

        return group;
    }

    private static void AddOrMergeProperty(MutableComponent component, OracleApexLanguageReferenceProperty property)
    {
        if (component.DirectProperties.TryGetValue(property.PropertyPath, out var existing))
        {
            component.DirectProperties[property.PropertyPath] = MergeProperty(existing, property);
            return;
        }

        component.DirectProperties[property.PropertyPath] = property;
    }

    private static OracleApexLanguageReferenceProperty MergeProperty(OracleApexLanguageReferenceProperty existing, OracleApexLanguageReferenceProperty incoming)
        => new()
        {
            Name = existing.Name,
            PropertyPath = existing.PropertyPath,
            DataType = string.IsNullOrWhiteSpace(existing.DataType) ? incoming.DataType : existing.DataType,
            Required = existing.Required || incoming.Required,
            DefaultValue = string.IsNullOrWhiteSpace(existing.DefaultValue) ? incoming.DefaultValue : existing.DefaultValue,
            EnumValues = existing.EnumValues.Count == 0 ? incoming.EnumValues : existing.EnumValues,
            AppliesWhen = string.IsNullOrWhiteSpace(existing.AppliesWhen) ? incoming.AppliesWhen : existing.AppliesWhen,
            MaxLength = string.IsNullOrWhiteSpace(existing.MaxLength) ? incoming.MaxLength : existing.MaxLength,
            NumericBounds = string.IsNullOrWhiteSpace(existing.NumericBounds) ? incoming.NumericBounds : existing.NumericBounds,
            ValidationConstraint = string.IsNullOrWhiteSpace(existing.ValidationConstraint) ? incoming.ValidationConstraint : existing.ValidationConstraint,
        };

    private static IReadOnlyList<string> ParseEnumValues(string details)
    {
        var match = EnumPattern.Match(details);
        if (!match.Success)
        {
            return Array.Empty<string>();
        }

        return match.Groups["values"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.Trim().Trim('`', '\\', '[', ']', '"'))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static Dictionary<string, string> ParseDetailMap(string details)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in DetailPattern.Matches(details))
        {
            map[match.Groups["key"].Value] = match.Groups["value"].Value.Trim();
        }

        return map;
    }

    private static string NormalizeComponentName(string value)
    {
        var normalized = value.Trim().Replace("`", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);

        return normalized.ToLowerInvariant() switch
        {
            "app" => "application",
            "authentication" => "authentication-scheme",
            "authorization" => "authorization-scheme",
            "buildoption" => "build-option",
            "dynamicaction" => "dynamic-action",
            "pageitem" => "item",
            "restdatasource" => "rest-data-source",
            "restmodule" => "rest-module",
            "resthandler" => "rest-handler",
            "classicnavigationbarentry" => "navigation-entry",
            _ => normalized.ToLowerInvariant(),
        };
    }

    private static string NormalizePropertyPath(string value)
        => string.Join('.', value.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim().Replace("_", "-", StringComparison.Ordinal).Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant()));

    private static string ExtractGrammarVersion(string ebnf, string fallbackVersion)
        => ebnf.Contains("compatibilityMode", StringComparison.Ordinal) ? fallbackVersion : fallbackVersion;

    private sealed class MutableComponent
    {
        public string CanonicalName { get; init; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string DocumentationAnchor { get; set; } = string.Empty;
        public HashSet<string> ParentComponents { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ChildComponents { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, OracleApexLanguageReferenceProperty> DirectProperties { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, MutableGroup> PropertyGroups { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> CanonicalExamples { get; } = [];

        public static implicit operator OracleApexLanguageReferenceComponent(MutableComponent component)
            => new()
            {
                CanonicalName = component.CanonicalName,
                DisplayName = string.IsNullOrWhiteSpace(component.DisplayName) ? component.CanonicalName : component.DisplayName,
                DocumentationAnchor = component.DocumentationAnchor,
                ParentComponents = component.ParentComponents.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
                ChildComponents = component.ChildComponents.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
                DirectProperties = component.DirectProperties.Values.OrderBy(item => item.PropertyPath, StringComparer.OrdinalIgnoreCase).ToList(),
                PropertyGroups = component.PropertyGroups.Values.Select(group => (OracleApexLanguageReferencePropertyGroup)group).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                CanonicalExamples = component.CanonicalExamples,
            };
    }

    private sealed class MutableGroup
    {
        public string Name { get; init; } = string.Empty;
        public Dictionary<string, OracleApexLanguageReferenceProperty> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static implicit operator OracleApexLanguageReferencePropertyGroup(MutableGroup group)
            => new() { Name = group.Name, Properties = group.Properties.Values.OrderBy(item => item.PropertyPath, StringComparer.OrdinalIgnoreCase).ToList() };
    }
}
