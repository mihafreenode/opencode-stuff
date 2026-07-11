using System.Text.RegularExpressions;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class OracleApexSemanticModelBuilder
{
    private static readonly string[] KnownBlockTypes =
    [
        "app",
        "authentication",
        "authorization",
        "buildOption",
        "dynamicAction",
        "pageItem",
        "restDataSource",
        "restModule",
        "restHandler",
        "appProcess",
        "appItem",
        "appComputation",
        "validation",
        "authorization scheme",
        "authentication scheme",
        "navigation menu",
        "navigation entry",
        "list of values",
        "dynamic action",
        "build option",
        "static file",
        "rest data source",
        "rest module",
        "rest handler",
        "deployment",
        "computation",
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
    private static readonly Regex DatabaseReferencePattern = new(@"\b(?:from|join|update|into|merge\s+into|delete\s+from)\s+([A-Za-z][A-Za-z0-9_$#\.]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PlSqlQualifiedIdentifierPattern = new(@"\b([A-Za-z][A-Za-z0-9_$#]*)\.([A-Za-z][A-Za-z0-9_$#]*)\b", RegexOptions.Compiled);
    private static readonly Regex RestEndpointPattern = new(@"\b(?:https?://[^\s""']+|/[A-Za-z0-9_\-./]+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "JOIN", "LEFT", "RIGHT", "FULL", "OUTER", "INNER", "ON", "UPDATE", "DELETE", "INSERT", "INTO", "VALUES",
        "BEGIN", "END", "DECLARE", "CREATE", "OR", "REPLACE", "PACKAGE", "PROCEDURE", "FUNCTION", "RETURN", "MERGE", "WHEN", "THEN", "ELSE",
    };

    private readonly OracleApexComponentCatalog _catalog;

    public OracleApexSemanticModelBuilder(OracleApexComponentCatalog? catalog = null)
        => _catalog = catalog ?? OracleApexComponentCatalog.Default;

    public OracleApexSemanticModel Build(string sourcePath)
    {
        var diagnostics = new List<OracleApexSemanticDiagnostic>();
        var rawFiles = new List<RawSemanticFile>();

        if (!Directory.Exists(sourcePath))
        {
            diagnostics.Add(new OracleApexSemanticDiagnostic
            {
                Severity = OracleApexSemanticDiagnosticSeverity.Error,
                Code = "source-path-missing",
                Message = $"Oracle APEX source path '{sourcePath}' does not exist.",
            });
            return new OracleApexSemanticModel(null, Array.Empty<OracleApexSemanticNode>(), diagnostics);
        }

        foreach (var filePath in Directory.GetFiles(sourcePath, "*.apx", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetRelativePath(sourcePath, filePath).Replace(Path.DirectorySeparatorChar, '/').StartsWith("deployments/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rawFiles.Add(ParseFile(sourcePath, filePath, diagnostics));
        }

        var applicationFile = rawFiles.FirstOrDefault(file => file.Roots.Any(root => root.SemanticType == "application"));
        if (applicationFile is null)
        {
            diagnostics.Add(new OracleApexSemanticDiagnostic
            {
                Severity = OracleApexSemanticDiagnosticSeverity.Error,
                Code = "application-missing",
                Message = "APEXlang package does not contain an application block.",
            });
            return new OracleApexSemanticModel(null, Array.Empty<OracleApexSemanticNode>(), diagnostics);
        }

        var nodeIndex = new Dictionary<string, OracleApexSemanticNode>(StringComparer.OrdinalIgnoreCase);
        var nodes = new List<OracleApexSemanticNode>();
        var application = BuildNode(applicationFile.Roots.First(root => root.SemanticType == "application"), null, nodeIndex, nodes, diagnostics);

        foreach (var rawFile in rawFiles.Where(file => !ReferenceEquals(file, applicationFile)))
        {
            foreach (var rawRoot in rawFile.Roots)
            {
                AttachToApplication(rawRoot, application, nodeIndex, nodes, diagnostics);
            }
        }

        PopulateRelationships(nodes, diagnostics);
        Validate(nodes, diagnostics);
        return new OracleApexSemanticModel(application, nodes, diagnostics.OrderBy(item => item.SourceFile, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Line).ToList());
    }

    public IReadOnlyList<OracleApexSemanticDeploymentProfile> BuildDeploymentProfiles(string sourcePath)
    {
        if (!Directory.Exists(sourcePath))
        {
            return Array.Empty<OracleApexSemanticDeploymentProfile>();
        }

        var deploymentsRoot = Path.Combine(sourcePath, "deployments");
        if (!Directory.Exists(deploymentsRoot))
        {
            return Array.Empty<OracleApexSemanticDeploymentProfile>();
        }

        var diagnostics = new List<OracleApexSemanticDiagnostic>();
        var profiles = new List<OracleApexSemanticDeploymentProfile>();
        foreach (var filePath in Directory.GetFiles(deploymentsRoot, "*.apx", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var parsed = ParseFile(sourcePath, filePath, diagnostics);
            var root = parsed.Roots.FirstOrDefault();
            if (root is null)
            {
                profiles.Add(new OracleApexSemanticDeploymentProfile
                {
                    Name = Path.GetFileNameWithoutExtension(filePath),
                    SourceFile = parsed.RelativePath,
                    AbsolutePath = filePath,
                    IsValid = false,
                    ValidationMessage = $"Deployment file '{parsed.RelativePath}' does not contain a deployment block.",
                });
                continue;
            }

            var isValid = string.Equals(root.SemanticType, "deployment-profile", StringComparison.OrdinalIgnoreCase);
            profiles.Add(new OracleApexSemanticDeploymentProfile
            {
                Name = ResolveIdentifier(root),
                SourceFile = parsed.RelativePath,
                AbsolutePath = filePath,
                Line = root.Line,
                Column = root.Column,
                Properties = new Dictionary<string, string>(root.Properties, StringComparer.OrdinalIgnoreCase),
                ReferencedObjects = ExtractReferences(root),
                IsValid = isValid,
                ValidationMessage = isValid
                    ? $"Deployment file '{parsed.RelativePath}' is valid."
                    : $"Expected a deployment root block in '{parsed.RelativePath}'.",
            });
        }

        return profiles;
    }

    private RawSemanticFile ParseFile(string sourceRoot, string filePath, ICollection<OracleApexSemanticDiagnostic> diagnostics)
    {
        var roots = new List<RawNode>();
        var stack = new Stack<RawNode>();
        var groupStack = new Stack<string>();
        var lines = File.ReadAllLines(filePath);

        for (var index = 0; index < lines.Length; index++)
        {
            var rawLine = lines[index];
            var trimmed = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (trimmed == ")")
            {
                if (stack.Count == 0)
                {
                    diagnostics.Add(Diagnostic(filePath, index + 1, Column(rawLine, trimmed), "unexpected-close", "Unexpected closing token in APEXlang file."));
                    continue;
                }

                var closed = stack.Pop();
                closed.EndLine = index + 1;
                closed.EndColumn = Column(rawLine, trimmed);
                continue;
            }

            if (trimmed == "}")
            {
                if (groupStack.Count > 0)
                {
                    groupStack.Pop();
                }
                else
                {
                    diagnostics.Add(Diagnostic(filePath, index + 1, Column(rawLine, trimmed), "unexpected-group-close", "Unexpected closing group token in APEXlang file."));
                }

                continue;
            }

            if (TryParseBlockStart(trimmed, out var blockType, out var blockName))
            {
                var semanticType = ToSemanticType(blockType);
                var node = new RawNode
                {
                    SemanticType = semanticType,
                    RawType = NormalizeToken(blockType),
                    Identifier = CleanValue(blockName),
                    SourceFile = Path.GetRelativePath(sourceRoot, filePath).Replace(Path.DirectorySeparatorChar, '/'),
                    Line = index + 1,
                    Column = Column(rawLine, trimmed),
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
                groupStack.Clear();
                continue;
            }

            if (trimmed.EndsWith('{'))
            {
                if (stack.Count == 0)
                {
                    diagnostics.Add(Diagnostic(filePath, index + 1, Column(rawLine, trimmed), "orphan-group", "Property group exists outside any known APEXlang component block."));
                }
                else
                {
                    groupStack.Push(CleanValue(trimmed[..^1]).Trim());
                }

                continue;
            }

            if (stack.Count == 0)
            {
                diagnostics.Add(Diagnostic(filePath, index + 1, Column(rawLine, trimmed), "orphan-content", "Content exists outside any known APEXlang component block."));
                continue;
            }

            var current = stack.Peek();
            current.TextLines.Add(trimmed);
            var propertyMatch = PropertyPattern.Match(trimmed);
            if (propertyMatch.Success)
            {
                var propertyKey = NormalizeToken(propertyMatch.Groups["key"].Value);
                if (groupStack.Count > 0)
                {
                    propertyKey = string.Join('.', groupStack.Reverse().Select(NormalizeToken).Append(propertyKey));
                }

                current.Properties[propertyKey] = CleanValue(propertyMatch.Groups["value"].Value);
            }
        }

        if (stack.Count > 0)
        {
            foreach (var node in stack)
            {
                diagnostics.Add(new OracleApexSemanticDiagnostic
                {
                    Severity = OracleApexSemanticDiagnosticSeverity.Error,
                    Code = "unclosed-component",
                    Message = $"Component '{node.SemanticType}' is not closed properly.",
                    SourceFile = node.SourceFile,
                    Line = node.Line,
                    Column = node.Column,
                });
            }
        }

        return new RawSemanticFile { FilePath = filePath, RelativePath = Path.GetRelativePath(sourceRoot, filePath).Replace(Path.DirectorySeparatorChar, '/'), Roots = roots };
    }

    private void AttachToApplication(RawNode rawRoot, OracleApexSemanticNode application, IDictionary<string, OracleApexSemanticNode> nodeIndex, ICollection<OracleApexSemanticNode> nodes, ICollection<OracleApexSemanticDiagnostic> diagnostics)
    {
        if (!_catalog.TryGetComponent(rawRoot.SemanticType, out var component) || component is null)
        {
            diagnostics.Add(new OracleApexSemanticDiagnostic
            {
                Severity = OracleApexSemanticDiagnosticSeverity.Error,
                Code = "unknown-component",
                Message = $"Unsupported Oracle APEX component '{rawRoot.SemanticType}'.",
                SourceFile = rawRoot.SourceFile,
                Line = rawRoot.Line,
                Column = rawRoot.Column,
            });
            return;
        }

        if (!component.ParentComponents.Contains("application", StringComparer.OrdinalIgnoreCase))
        {
            diagnostics.Add(new OracleApexSemanticDiagnostic
            {
                Severity = OracleApexSemanticDiagnosticSeverity.Error,
                Code = "invalid-root-component",
                Message = $"Component '{rawRoot.SemanticType}' cannot appear as a root application child.",
                SourceFile = rawRoot.SourceFile,
                Line = rawRoot.Line,
                Column = rawRoot.Column,
            });
            return;
        }

        var node = BuildNode(rawRoot, application, nodeIndex, nodes, diagnostics);
        application.AddChild(node);
    }

    private OracleApexSemanticNode BuildNode(RawNode raw, OracleApexSemanticNode? parent, IDictionary<string, OracleApexSemanticNode> nodeIndex, ICollection<OracleApexSemanticNode> nodes, ICollection<OracleApexSemanticDiagnostic> diagnostics)
    {
        var identifier = ResolveIdentifier(raw);
        var nodeId = $"{raw.SourceFile}:{raw.Line}:{raw.SemanticType}:{identifier}";
        var node = new OracleApexSemanticNode(nodeId, raw.SemanticType, identifier, raw.SourceFile, raw.Line, raw.Column, raw.EndLine, raw.EndColumn, parent);
        node.SetProperties(raw.Properties);
        node.SetReferencedObjects(ExtractReferences(raw));

        if (!nodeIndex.TryAdd(node.NodeId, node))
        {
            diagnostics.Add(new OracleApexSemanticDiagnostic
            {
                Severity = OracleApexSemanticDiagnosticSeverity.Error,
                Code = "duplicate-node-id",
                Message = $"Duplicate semantic node id '{node.NodeId}' was produced.",
                SourceFile = raw.SourceFile,
                Line = raw.Line,
                Column = raw.Column,
            });
        }

        nodes.Add(node);

        foreach (var child in raw.Children)
        {
            ValidateParentChild(node.SemanticType, child, diagnostics);
            var childNode = BuildNode(child, node, nodeIndex, nodes, diagnostics);
            node.AddChild(childNode);
        }

        ValidateRequiredProperties(node, diagnostics);
        return node;
    }

    private void PopulateRelationships(IReadOnlyList<OracleApexSemanticNode> nodes, ICollection<OracleApexSemanticDiagnostic> diagnostics)
    {
        var pagesById = nodes.Where(node => node.SemanticType == "page")
            .Where(node => int.TryParse(node.GetProperty("id"), out _))
            .GroupBy(node => int.Parse(node.GetProperty("id")!))
            .ToDictionary(group => group.Key, group => group.First());
        var sharedComponentsByName = nodes
            .Where(node => node.SemanticType is "authorization-scheme" or "authentication-scheme" or "navigation-menu" or "list" or "lov" or "build-option" or "plugin")
            .GroupBy(node => node.Identifier, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            if (node.SemanticType == "page" && int.TryParse(node.GetProperty("parent-page"), out var parentPageIdValue) && !pagesById.ContainsKey(parentPageIdValue))
            {
                diagnostics.Add(node.Diagnostic("missing-parent-reference", $"Page '{node.Identifier}' references missing parent page '{parentPageIdValue}'."));
            }

            if (node.SemanticType is "branch" or "navigation-entry" && int.TryParse(node.GetProperty("target-page"), out var targetPageIdValue) && !pagesById.ContainsKey(targetPageIdValue))
            {
                diagnostics.Add(node.Diagnostic("invalid-navigation-reference", $"Component '{node.Identifier}' references missing target page '{targetPageIdValue}'."));
            }

            foreach (var propertyName in new[] { "authorization-scheme", "authentication-scheme", "lov", "list", "build-option", "plugin" })
            {
                var propertyValue = node.GetProperty(propertyName);
                if (string.IsNullOrWhiteSpace(propertyValue))
                {
                    continue;
                }

                if (!sharedComponentsByName.ContainsKey(propertyValue))
                {
                    diagnostics.Add(node.Diagnostic("unresolved-shared-component", $"Component '{node.Identifier}' references missing shared component '{propertyValue}' via '{propertyName}'."));
                }
            }
        }

        foreach (var menu in nodes.Where(node => node.SemanticType == "navigation-menu"))
        {
            var entriesByName = menu.Children.Where(child => child.SemanticType == "navigation-entry").ToDictionary(child => child.Identifier, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in menu.Children.Where(child => child.SemanticType == "navigation-entry"))
            {
                var parentEntry = entry.GetProperty("parent-entry");
                if (!string.IsNullOrWhiteSpace(parentEntry) && !entriesByName.ContainsKey(parentEntry))
                {
                    diagnostics.Add(entry.Diagnostic("missing-parent-reference", $"Navigation entry '{entry.Identifier}' references missing parent entry '{parentEntry}'."));
                }
            }
        }
    }

    private void Validate(IReadOnlyList<OracleApexSemanticNode> nodes, ICollection<OracleApexSemanticDiagnostic> diagnostics)
    {
        foreach (var duplicate in nodes.Where(node => node.SemanticType == "page")
                     .GroupBy(node => node.GetProperty("alias") ?? node.Identifier, StringComparer.OrdinalIgnoreCase)
                     .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1))
        {
            foreach (var node in duplicate)
            {
                diagnostics.Add(node.Diagnostic("duplicate-page-alias", $"Duplicate page alias '{duplicate.Key}' was found."));
            }
        }

        foreach (var page in nodes.Where(node => node.SemanticType == "page"))
        {
            foreach (var duplicateRegion in page.Children.Where(child => child.SemanticType == "region")
                         .GroupBy(child => child.GetProperty("name") ?? child.Identifier, StringComparer.OrdinalIgnoreCase)
                         .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1))
            {
                foreach (var region in duplicateRegion)
                {
                    diagnostics.Add(region.Diagnostic("duplicate-region-identifier", $"Duplicate region identifier '{duplicateRegion.Key}' was found on page '{page.Identifier}'."));
                }
            }

            foreach (var duplicateItem in page.Children.Where(child => child.SemanticType == "item")
                         .GroupBy(child => child.Identifier, StringComparer.OrdinalIgnoreCase)
                         .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1))
            {
                foreach (var item in duplicateItem)
                {
                    diagnostics.Add(item.Diagnostic("duplicate-item-name", $"Duplicate item name '{duplicateItem.Key}' was found on page '{page.Identifier}'."));
                }
            }
        }

        ValidateCircularPageReferences(nodes, diagnostics);
        ValidateCircularNavigationReferences(nodes, diagnostics);
    }

    private void ValidateCircularPageReferences(IReadOnlyList<OracleApexSemanticNode> nodes, ICollection<OracleApexSemanticDiagnostic> diagnostics)
    {
        var pagesById = nodes.Where(node => node.SemanticType == "page")
            .Where(node => int.TryParse(node.GetProperty("id"), out _))
            .GroupBy(node => int.Parse(node.GetProperty("id")!))
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var page in pagesById.Values)
        {
            var seen = new HashSet<int>();
            var current = page;
            while (int.TryParse(current.GetProperty("parent-page"), out var parentPageId) && pagesById.TryGetValue(parentPageId, out var parentPage))
            {
                if (!seen.Add(parentPageId))
                {
                    diagnostics.Add(page.Diagnostic("circular-page-reference", $"Page '{page.Identifier}' participates in a circular parent-page reference."));
                    break;
                }

                current = parentPage;
            }
        }
    }

    private void ValidateCircularNavigationReferences(IReadOnlyList<OracleApexSemanticNode> nodes, ICollection<OracleApexSemanticDiagnostic> diagnostics)
    {
        foreach (var menu in nodes.Where(node => node.SemanticType == "navigation-menu"))
        {
            var entries = menu.Children.Where(child => child.SemanticType == "navigation-entry").ToDictionary(child => child.Identifier, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries.Values)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var current = entry;
                while (!string.IsNullOrWhiteSpace(current.GetProperty("parent-entry")) && entries.TryGetValue(current.GetProperty("parent-entry")!, out var parentEntry))
                {
                    if (!seen.Add(parentEntry.Identifier))
                    {
                        diagnostics.Add(entry.Diagnostic("circular-navigation-reference", $"Navigation entry '{entry.Identifier}' participates in a circular parent-entry reference."));
                        break;
                    }

                    current = parentEntry;
                }
            }
        }
    }

    private void ValidateRequiredProperties(OracleApexSemanticNode node, ICollection<OracleApexSemanticDiagnostic> diagnostics)
    {
        if (!_catalog.TryGetComponent(node.SemanticType, out var component) || component is null)
        {
            return;
        }

        foreach (var requiredProperty in component.RequiredProperties)
        {
            if (string.IsNullOrWhiteSpace(node.GetProperty(requiredProperty)) && string.IsNullOrWhiteSpace(node.Identifier))
            {
                diagnostics.Add(node.Diagnostic("missing-required-property", $"Component '{node.SemanticType}' is missing required property '{requiredProperty}'."));
            }
            else if (requiredProperty != "name" && string.IsNullOrWhiteSpace(node.GetProperty(requiredProperty)))
            {
                diagnostics.Add(node.Diagnostic("missing-required-property", $"Component '{node.Identifier}' is missing required property '{requiredProperty}'."));
            }
        }
    }

    private void ValidateParentChild(string parentType, RawNode child, ICollection<OracleApexSemanticDiagnostic> diagnostics)
    {
        if (!_catalog.TryGetComponent(parentType, out var parentComponent) || parentComponent is null)
        {
            return;
        }

        if (!parentComponent.ChildComponents.Contains(child.SemanticType, StringComparer.OrdinalIgnoreCase))
        {
            var identifier = ResolveIdentifier(child);
            diagnostics.Add(new OracleApexSemanticDiagnostic
            {
                Severity = OracleApexSemanticDiagnosticSeverity.Error,
                Code = "invalid-child-component",
                Message = $"Component '{child.SemanticType}' is not valid inside '{parentType}'.",
                SourceFile = child.SourceFile,
                Line = child.Line,
                Column = child.Column,
                NodeId = $"{child.SourceFile}:{child.Line}:{child.SemanticType}:{identifier}",
                SemanticType = child.SemanticType,
            });
        }
    }

    private static IReadOnlyList<string> ExtractReferences(RawNode raw)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = string.Join("\n", raw.TextLines.Concat(raw.Properties.Select(pair => $"{pair.Key}: {pair.Value}")));

        foreach (Match match in DatabaseReferencePattern.Matches(text))
        {
            references.Add(match.Groups[1].Value.ToUpperInvariant());
        }

        foreach (Match match in PlSqlQualifiedIdentifierPattern.Matches(text.ToUpperInvariant()))
        {
            var identifier = $"{match.Groups[1].Value}.{match.Groups[2].Value}";
            if (!SqlKeywords.Contains(match.Groups[1].Value))
            {
                references.Add(identifier);
            }
        }

        foreach (Match match in RestEndpointPattern.Matches(text))
        {
            references.Add(match.Value);
        }

        return references.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
    }

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

    private static string ResolveIdentifier(RawNode raw)
        => raw.SemanticType switch
        {
            "region" when raw.Properties.TryGetValue("title", out var regionTitle) && !string.IsNullOrWhiteSpace(regionTitle) => regionTitle,
            "navigation-entry" when raw.Properties.TryGetValue("label", out var label) && !string.IsNullOrWhiteSpace(label) => label,
            _ when raw.Properties.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name) => name,
            _ when raw.Properties.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title) => title,
            _ when raw.Properties.TryGetValue("label", out var fallbackLabel) && !string.IsNullOrWhiteSpace(fallbackLabel) => fallbackLabel,
            _ => string.IsNullOrWhiteSpace(raw.Identifier) ? raw.SemanticType : raw.Identifier,
        };

    private static string ToSemanticType(string blockType)
        => NormalizeToken(blockType) switch
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
            "appprocess" => "process",
            "appitem" => "item",
            "appcomputation" => "computation",
            "authorization-scheme" => "authorization-scheme",
            "authentication-scheme" => "authentication-scheme",
            "navigation-menu" => "navigation-menu",
            "entry" or "navigation-entry" => "navigation-entry",
            "list-of-values" => "lov",
            "dynamic-action" => "dynamic-action",
            "build-option" => "build-option",
            "static-file" => "static-file",
            "rest-data-source" => "rest-data-source",
            "rest-module" => "rest-module",
            "rest-handler" => "rest-handler",
            "deployment" => "deployment-profile",
            "buildoptiona" or "buildoptionb" => "build-option",
            "validationa" or "validationb" => "validation",
            _ => NormalizeToken(blockType),
        };

    private static string NormalizeToken(string value)
        => value.Trim().Replace('_', '-').Replace(' ', '-').ToLowerInvariant();

    private static string CleanValue(string value)
        => value.Trim().Trim('"', '\'');

    private static int Column(string rawLine, string trimmed)
        => Math.Max(1, rawLine.IndexOf(trimmed, StringComparison.Ordinal) + 1);

    private static OracleApexSemanticDiagnostic Diagnostic(string filePath, int line, int column, string code, string message)
        => new()
        {
            Severity = OracleApexSemanticDiagnosticSeverity.Error,
            Code = code,
            Message = message,
            SourceFile = filePath,
            Line = line,
            Column = column,
        };

    private sealed class RawSemanticFile
    {
        public string FilePath { get; init; } = string.Empty;
        public string RelativePath { get; init; } = string.Empty;
        public List<RawNode> Roots { get; init; } = [];
    }

    private sealed class RawNode
    {
        public string SemanticType { get; init; } = string.Empty;
        public string RawType { get; init; } = string.Empty;
        public string Identifier { get; init; } = string.Empty;
        public string SourceFile { get; init; } = string.Empty;
        public int Line { get; init; }
        public int Column { get; init; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }
        public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> TextLines { get; } = [];
        public List<RawNode> Children { get; } = [];
    }
}

public sealed class OracleApexSemanticModel
{
    public OracleApexSemanticNode? Application { get; }
    public IReadOnlyList<OracleApexSemanticNode> Nodes { get; }
    public IReadOnlyList<OracleApexSemanticDiagnostic> Diagnostics { get; }

    public OracleApexSemanticModel(OracleApexSemanticNode? application, IReadOnlyList<OracleApexSemanticNode> nodes, IReadOnlyList<OracleApexSemanticDiagnostic> diagnostics)
    {
        Application = application;
        Nodes = nodes;
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<OracleApexSemanticNode> GetNodes(string semanticType)
        => Nodes.Where(node => string.Equals(node.SemanticType, semanticType, StringComparison.OrdinalIgnoreCase)).ToList();
}

public sealed class OracleApexSemanticNode
{
    private readonly List<OracleApexSemanticNode> _children = [];
    private IReadOnlyDictionary<string, string> _properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> _referencedObjects = Array.Empty<string>();

    public OracleApexSemanticNode(string nodeId, string semanticType, string identifier, string sourceFile, int line, int column, int endLine, int endColumn, OracleApexSemanticNode? parent)
    {
        NodeId = nodeId;
        SemanticType = semanticType;
        Identifier = identifier;
        SourceFile = sourceFile;
        Line = line;
        Column = column;
        EndLine = endLine;
        EndColumn = endColumn;
        Parent = parent;
    }

    public string NodeId { get; }
    public string SemanticType { get; }
    public string Identifier { get; }
    public string SourceFile { get; }
    public int Line { get; }
    public int Column { get; }
    public int EndLine { get; }
    public int EndColumn { get; }
    public OracleApexSemanticNode? Parent { get; }
    public IReadOnlyList<OracleApexSemanticNode> Children => _children;
    public IReadOnlyDictionary<string, string> Properties => _properties;
    public IReadOnlyList<string> ReferencedObjects => _referencedObjects;

    public void AddChild(OracleApexSemanticNode child)
    {
        if (!_children.Contains(child))
        {
            _children.Add(child);
        }
    }

    public void SetProperties(IReadOnlyDictionary<string, string> properties)
        => _properties = new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase);

    public void SetReferencedObjects(IReadOnlyList<string> references)
        => _referencedObjects = references;

    public string? GetProperty(string propertyName)
        => _properties.TryGetValue(propertyName, out var value) ? value : null;

    public OracleApexSemanticDiagnostic Diagnostic(string code, string message)
        => new()
        {
            Severity = OracleApexSemanticDiagnosticSeverity.Error,
            Code = code,
            Message = message,
            SourceFile = SourceFile,
            Line = Line,
            Column = Column,
            NodeId = NodeId,
            SemanticType = SemanticType,
        };
}

public sealed class OracleApexSemanticDiagnostic
{
    public OracleApexSemanticDiagnosticSeverity Severity { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public int Line { get; init; }
    public int Column { get; init; }
    public string NodeId { get; init; } = string.Empty;
    public string SemanticType { get; init; } = string.Empty;
}

public sealed class OracleApexSemanticDeploymentProfile
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

public enum OracleApexSemanticDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}
