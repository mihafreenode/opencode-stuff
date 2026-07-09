using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class OracleApexSemanticEditor
{
    private readonly OracleApexWorkspaceIndexBuilder _workspaceIndexBuilder;
    private readonly OracleApexComponentCatalog _componentCatalog;

    public OracleApexSemanticEditor(OracleApexWorkspaceIndexBuilder? workspaceIndexBuilder = null, OracleApexComponentCatalog? componentCatalog = null)
    {
        _componentCatalog = componentCatalog ?? OracleApexComponentCatalog.Default;
        _workspaceIndexBuilder = workspaceIndexBuilder ?? new OracleApexWorkspaceIndexBuilder(new OracleApexSemanticModelBuilder(_componentCatalog));
    }

    public OracleApexSemanticEditResult Apply(string rootPath, OracleApexEnvironmentPreferences environment, string environmentName, params OracleApexSemanticEditOperation[] operations)
        => Apply(rootPath, environment, environmentName, (IReadOnlyList<OracleApexSemanticEditOperation>)operations);

    public OracleApexSemanticEditResult Apply(string rootPath, OracleApexEnvironmentPreferences environment, string environmentName, IReadOnlyList<OracleApexSemanticEditOperation> operations)
    {
        var changedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = _workspaceIndexBuilder.Build(rootPath, environment, environmentName);

        foreach (var operation in operations)
        {
            var backups = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            try
            {
                ApplyOperation(rootPath, environment, environmentName, operation, index, backups, changedFiles);
                var refreshed = _workspaceIndexBuilder.Build(rootPath, environment, environmentName);
                var errors = refreshed.Diagnostics.Where(item => string.Equals(item.Severity, OracleApexSemanticDiagnosticSeverity.Error.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();
                if (errors.Count > 0)
                {
                    RestoreBackups(backups);
                    var rollbackIndex = _workspaceIndexBuilder.Build(rootPath, environment, environmentName);
                    return new OracleApexSemanticEditResult
                    {
                        IsSuccess = false,
                        Message = $"Semantic edit '{operation.Kind}' produced an invalid Oracle APEX application.",
                        Diagnostics = new OracleApexSemanticEditDiagnostics { Entries = errors },
                        WorkspaceIndex = rollbackIndex,
                        ChangedFiles = changedFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(),
                    };
                }

                index = refreshed;
            }
            catch (Exception exception)
            {
                RestoreBackups(backups);
                var rollbackIndex = _workspaceIndexBuilder.Build(rootPath, environment, environmentName);
                return new OracleApexSemanticEditResult
                {
                    IsSuccess = false,
                    Message = exception.Message,
                    Diagnostics = new OracleApexSemanticEditDiagnostics
                    {
                        Entries =
                        [
                            new OracleApexWorkspaceIndexDiagnostic
                            {
                                Severity = OracleApexSemanticDiagnosticSeverity.Error.ToString(),
                                Code = "semantic-edit-failed",
                                Message = exception.Message,
                            },
                        ],
                    },
                    WorkspaceIndex = rollbackIndex,
                    ChangedFiles = changedFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(),
                };
            }
        }

        return new OracleApexSemanticEditResult
        {
            IsSuccess = true,
            Message = operations.Count == 0 ? "No semantic edit operations were applied." : $"Applied {operations.Count} semantic edit operation(s).",
            Diagnostics = new OracleApexSemanticEditDiagnostics { Entries = index.Diagnostics },
            WorkspaceIndex = index,
            ChangedFiles = changedFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    private void ApplyOperation(string rootPath, OracleApexEnvironmentPreferences environment, string environmentName, OracleApexSemanticEditOperation operation, OracleApexWorkspaceIndex index, IDictionary<string, string?> backups, ISet<string> changedFiles)
    {
        switch (operation.Kind)
        {
            case OracleApexSemanticEditKind.AddPage:
                AddPage(rootPath, environment, operation, index, backups, changedFiles);
                break;
            case OracleApexSemanticEditKind.RemovePage:
                RemovePage(rootPath, environment, operation, index, backups, changedFiles);
                break;
            case OracleApexSemanticEditKind.RenamePage:
                RenamePage(rootPath, environment, operation, index, backups, changedFiles);
                break;
            case OracleApexSemanticEditKind.AddRegion:
                AddChildBlock(rootPath, environment, operation, index, backups, changedFiles, "region");
                break;
            case OracleApexSemanticEditKind.RemoveRegion:
                RemoveBlock(rootPath, environment, RequireEntry(index, "region", operation.TargetIdentifier, operation.ParentIdentifier), backups, changedFiles);
                break;
            case OracleApexSemanticEditKind.MoveRegion:
                MoveRegion(rootPath, environment, operation, index, backups, changedFiles);
                break;
            case OracleApexSemanticEditKind.RenameRegion:
                RenameNode(rootPath, environment, operation, index, backups, changedFiles, "region", ["name", "title"]);
                break;
            case OracleApexSemanticEditKind.AddItem:
                AddChildBlock(rootPath, environment, operation, index, backups, changedFiles, "item");
                break;
            case OracleApexSemanticEditKind.RemoveItem:
                RemoveBlock(rootPath, environment, RequireEntry(index, "item", operation.TargetIdentifier, operation.ParentIdentifier), backups, changedFiles);
                break;
            case OracleApexSemanticEditKind.RenameItem:
                RenameNode(rootPath, environment, operation, index, backups, changedFiles, "item", ["name"]);
                UpdateReferences(rootPath, environment, index, backups, changedFiles, "item", operation.TargetIdentifier, operation.NewIdentifier, ["item"]);
                break;
            case OracleApexSemanticEditKind.AddButton:
                AddChildBlock(rootPath, environment, operation, index, backups, changedFiles, "button");
                break;
            case OracleApexSemanticEditKind.AddProcess:
                AddChildBlock(rootPath, environment, operation, index, backups, changedFiles, "process");
                break;
            case OracleApexSemanticEditKind.AddDynamicAction:
                AddChildBlock(rootPath, environment, operation, index, backups, changedFiles, "dynamic-action");
                break;
            case OracleApexSemanticEditKind.AddSharedComponent:
                AddSharedComponent(rootPath, environment, operation, index, backups, changedFiles);
                break;
            case OracleApexSemanticEditKind.RenameSharedComponent:
                RenameSharedComponent(rootPath, environment, operation, index, backups, changedFiles);
                break;
            case OracleApexSemanticEditKind.AddNavigationEntry:
                AddChildBlock(rootPath, environment, operation, index, backups, changedFiles, "navigation-entry");
                break;
            case OracleApexSemanticEditKind.RenameNavigationEntry:
                RenameNode(rootPath, environment, operation, index, backups, changedFiles, "navigation-entry", ["label"]);
                UpdateReferences(rootPath, environment, index, backups, changedFiles, "navigation-entry", operation.TargetIdentifier, operation.NewIdentifier, ["parent-entry"]);
                break;
            default:
                throw new InvalidOperationException($"Semantic edit operation '{operation.Kind}' is not supported.");
        }
    }

    private void AddPage(string rootPath, OracleApexEnvironmentPreferences environment, OracleApexSemanticEditOperation operation, OracleApexWorkspaceIndex index, IDictionary<string, string?> backups, ISet<string> changedFiles)
    {
        var properties = MergeProperties(operation, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = operation.NewIdentifier,
            ["alias"] = operation.Properties.TryGetValue("alias", out var alias) ? alias : Slug(operation.NewIdentifier).ToUpperInvariant(),
        });
        EnsureRequiredProperties("page", properties);
        var pageId = ReadRequiredInt(properties, "id");
        var aliasSlug = Slug(properties["alias"]);
        var sourcePath = GetSourcePath(rootPath, environment);
        var pageFilePath = Path.Combine(sourcePath, "pages", $"p{pageId:D5}-{aliasSlug}.apx");
        BackupFile(backups, pageFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(pageFilePath)!);
        WriteFile(pageFilePath, BuildBlockText("page", operation.NewIdentifier, properties, 0));
        changedFiles.Add(pageFilePath);
    }

    private void RenamePage(string rootPath, OracleApexEnvironmentPreferences environment, OracleApexSemanticEditOperation operation, OracleApexWorkspaceIndex index, IDictionary<string, string?> backups, ISet<string> changedFiles)
    {
        var entry = RequireEntry(index, "page", operation.TargetIdentifier);
        UpdateProperty(rootPath, environment, entry, "name", operation.NewIdentifier, backups, changedFiles);
        if (operation.Properties.TryGetValue("alias", out var alias))
        {
            UpdateProperty(rootPath, environment, entry, "alias", alias, backups, changedFiles);
        }
    }

    private void RemovePage(string rootPath, OracleApexEnvironmentPreferences environment, OracleApexSemanticEditOperation operation, OracleApexWorkspaceIndex index, IDictionary<string, string?> backups, ISet<string> changedFiles)
    {
        var entry = RequireEntry(index, "page", operation.TargetIdentifier);
        var pageId = entry.Properties.TryGetValue("id", out var idValue) ? idValue : string.Empty;
        RemoveNodeFile(rootPath, environment, entry, backups, changedFiles);

        if (string.IsNullOrWhiteSpace(pageId))
        {
            return;
        }

        foreach (var referencingEntry in index.Entries.Where(item => item.NodeId != entry.NodeId && (string.Equals(item.Properties.TryGetValue("target-page", out var targetPage) ? targetPage : null, pageId, StringComparison.OrdinalIgnoreCase) || string.Equals(item.Properties.TryGetValue("parent-page", out var parentPage) ? parentPage : null, pageId, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            if (referencingEntry.Properties.ContainsKey("target-page"))
            {
                RemoveProperty(rootPath, environment, referencingEntry, "target-page", backups, changedFiles);
            }

            if (referencingEntry.Properties.ContainsKey("parent-page"))
            {
                RemoveProperty(rootPath, environment, referencingEntry, "parent-page", backups, changedFiles);
            }
        }
    }

    private void MoveRegion(string rootPath, OracleApexEnvironmentPreferences environment, OracleApexSemanticEditOperation operation, OracleApexWorkspaceIndex index, IDictionary<string, string?> backups, ISet<string> changedFiles)
    {
        var region = RequireEntry(index, "region", operation.TargetIdentifier, operation.ParentIdentifier);
        var destinationPage = RequireEntry(index, "page", operation.DestinationParentIdentifier);
        EnsureChildPlacement(destinationPage.SemanticType, "region");

        var sourceFilePath = GetAbsolutePath(rootPath, environment, region.SourceFile);
        var destinationFilePath = GetAbsolutePath(rootPath, environment, destinationPage.SourceFile);
        BackupFile(backups, sourceFilePath);
        BackupFile(backups, destinationFilePath);
        var blockLines = ReadFileLines(sourceFilePath).Skip(region.Line - 1).Take(region.EndLine - region.Line + 1).ToList();
        RemoveRange(sourceFilePath, region.Line, region.EndLine);
        InsertBeforeClosing(destinationFilePath, destinationPage.EndLine, blockLines, ComputeChildIndent(destinationFilePath, destinationPage.Line));
        changedFiles.Add(sourceFilePath);
        changedFiles.Add(destinationFilePath);
    }

    private void AddSharedComponent(string rootPath, OracleApexEnvironmentPreferences environment, OracleApexSemanticEditOperation operation, OracleApexWorkspaceIndex index, IDictionary<string, string?> backups, ISet<string> changedFiles)
    {
        if (string.IsNullOrWhiteSpace(operation.ComponentType))
        {
            throw new InvalidOperationException("AddSharedComponent requires a component type.");
        }

        var componentType = operation.ComponentType.Trim();
        EnsureComponentExists(componentType);
        EnsureChildPlacement("application", componentType);
        var properties = MergeProperties(operation, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["name"] = operation.NewIdentifier });
        EnsureRequiredProperties(componentType, properties);

        var sourcePath = GetSourcePath(rootPath, environment);
        var relativeDirectory = GetSharedComponentDirectory(componentType);
        var filePath = Path.Combine(sourcePath, relativeDirectory.Replace('/', Path.DirectorySeparatorChar), Slug(operation.NewIdentifier) + ".apx");
        BackupFile(backups, filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        WriteFile(filePath, BuildBlockText(ToBlockType(componentType), operation.NewIdentifier, properties, 0));
        changedFiles.Add(filePath);
    }

    private void RenameSharedComponent(string rootPath, OracleApexEnvironmentPreferences environment, OracleApexSemanticEditOperation operation, OracleApexWorkspaceIndex index, IDictionary<string, string?> backups, ISet<string> changedFiles)
    {
        var entry = RequireEntry(index, operation.ComponentType, operation.TargetIdentifier);
        UpdateProperty(rootPath, environment, entry, "name", operation.NewIdentifier, backups, changedFiles);
        UpdateReferences(rootPath, environment, index, backups, changedFiles, operation.ComponentType, operation.TargetIdentifier, operation.NewIdentifier, SharedReferencePropertiesFor(operation.ComponentType));
    }

    private void AddChildBlock(string rootPath, OracleApexEnvironmentPreferences environment, OracleApexSemanticEditOperation operation, OracleApexWorkspaceIndex index, IDictionary<string, string?> backups, ISet<string> changedFiles, string childType)
    {
        var parentType = string.IsNullOrWhiteSpace(operation.ParentSemanticType) ? DefaultParentTypeFor(childType) : operation.ParentSemanticType;
        var parent = RequireEntry(index, parentType, operation.ParentIdentifier);
        EnsureChildPlacement(parent.SemanticType, childType);
        var properties = MergeProperties(operation, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [PrimaryNamePropertyFor(childType)] = operation.NewIdentifier });
        EnsureRequiredProperties(childType, properties);
        EnsureNoDuplicate(index, childType, operation.NewIdentifier, parent.NodeId);

        var filePath = GetAbsolutePath(rootPath, environment, parent.SourceFile);
        BackupFile(backups, filePath);
        var indent = ComputeChildIndent(filePath, parent.Line);
        var blockLines = BuildBlockLines(childType, operation.NewIdentifier, properties, indent);
        InsertBeforeClosing(filePath, parent.EndLine, blockLines, indent);
        changedFiles.Add(filePath);
    }

    private void RenameNode(string rootPath, OracleApexEnvironmentPreferences environment, OracleApexSemanticEditOperation operation, OracleApexWorkspaceIndex index, IDictionary<string, string?> backups, ISet<string> changedFiles, string semanticType, IReadOnlyList<string> candidateProperties)
    {
        var entry = RequireEntry(index, semanticType, operation.TargetIdentifier, operation.ParentIdentifier);
        var matched = candidateProperties.Where(name => entry.Properties.ContainsKey(name)).ToList();
        if (matched.Count == 0)
        {
            matched.Add(candidateProperties[0]);
        }

        foreach (var propertyName in matched)
        {
            UpdateProperty(rootPath, environment, entry, propertyName, operation.NewIdentifier, backups, changedFiles);
        }
    }

    private void RemoveBlock(string rootPath, OracleApexEnvironmentPreferences environment, OracleApexWorkspaceIndexEntry entry, IDictionary<string, string?> backups, ISet<string> changedFiles)
    {
        var filePath = GetAbsolutePath(rootPath, environment, entry.SourceFile);
        BackupFile(backups, filePath);
        RemoveRange(filePath, entry.Line, entry.EndLine);
        changedFiles.Add(filePath);
    }

    private void RemoveNodeFile(string rootPath, OracleApexEnvironmentPreferences environment, OracleApexWorkspaceIndexEntry entry, IDictionary<string, string?> backups, ISet<string> changedFiles)
    {
        var filePath = GetAbsolutePath(rootPath, environment, entry.SourceFile);
        BackupFile(backups, filePath);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            changedFiles.Add(filePath);
        }
    }

    private void UpdateReferences(string rootPath, OracleApexEnvironmentPreferences environment, OracleApexWorkspaceIndex index, IDictionary<string, string?> backups, ISet<string> changedFiles, string semanticType, string oldIdentifier, string newIdentifier, IReadOnlyList<string> propertyNames)
    {
        foreach (var entry in index.Entries.Where(item => propertyNames.Any(property => string.Equals(item.Properties.TryGetValue(property, out var value) ? value : null, oldIdentifier, StringComparison.OrdinalIgnoreCase))))
        {
            foreach (var propertyName in propertyNames)
            {
                if (entry.Properties.TryGetValue(propertyName, out var value) && string.Equals(value, oldIdentifier, StringComparison.OrdinalIgnoreCase))
                {
                    UpdateProperty(rootPath, environment, entry, propertyName, newIdentifier, backups, changedFiles);
                }
            }
        }
    }

    private void UpdateProperty(string rootPath, OracleApexEnvironmentPreferences environment, OracleApexWorkspaceIndexEntry entry, string propertyName, string propertyValue, IDictionary<string, string?> backups, ISet<string> changedFiles)
    {
        var filePath = GetAbsolutePath(rootPath, environment, entry.SourceFile);
        BackupFile(backups, filePath);
        var lines = ReadFileLines(filePath);
        var updated = false;
        for (var index = entry.Line; index < Math.Min(entry.EndLine - 1, lines.Count); index++)
        {
            var trimmed = lines[index].TrimStart();
            if (!trimmed.StartsWith(propertyName + ":", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var indent = lines[index][..(lines[index].Length - trimmed.Length)];
            lines[index] = $"{indent}{propertyName}: {propertyValue}";
            updated = true;
            break;
        }

        if (!updated)
        {
            var indent = new string(' ', ComputeChildIndent(filePath, entry.Line));
            lines.Insert(entry.EndLine - 1, $"{indent}{propertyName}: {propertyValue}");
        }

        WriteLines(filePath, lines);
        changedFiles.Add(filePath);
    }

    private void RemoveProperty(string rootPath, OracleApexEnvironmentPreferences environment, OracleApexWorkspaceIndexEntry entry, string propertyName, IDictionary<string, string?> backups, ISet<string> changedFiles)
    {
        var filePath = GetAbsolutePath(rootPath, environment, entry.SourceFile);
        BackupFile(backups, filePath);
        var lines = ReadFileLines(filePath);
        for (var index = entry.Line; index < Math.Min(entry.EndLine - 1, lines.Count); index++)
        {
            var trimmed = lines[index].TrimStart();
            if (!trimmed.StartsWith(propertyName + ":", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lines.RemoveAt(index);
            break;
        }

        WriteLines(filePath, lines);
        changedFiles.Add(filePath);
    }

    private static void InsertBeforeClosing(string filePath, int parentEndLine, IReadOnlyList<string> blockLines, int indent)
    {
        var lines = ReadFileLines(filePath);
        var normalizedBlock = blockLines.Select(line => line.Length == 0 ? string.Empty : new string(' ', indent) + line.TrimStart()).ToList();
        var insertIndex = Math.Max(0, Math.Min(lines.Count, parentEndLine - 1));
        if (insertIndex > 0 && !string.IsNullOrWhiteSpace(lines[insertIndex - 1]))
        {
            normalizedBlock.Insert(0, string.Empty);
        }

        lines.InsertRange(insertIndex, normalizedBlock);
        WriteLines(filePath, lines);
    }

    private static void RemoveRange(string filePath, int startLine, int endLine)
    {
        var lines = ReadFileLines(filePath);
        lines.RemoveRange(startLine - 1, endLine - startLine + 1);
        while (lines.Count > 1)
        {
            var removed = false;
            for (var index = 1; index < lines.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(lines[index]) && string.IsNullOrWhiteSpace(lines[index - 1]))
                {
                    lines.RemoveAt(index);
                    removed = true;
                    break;
                }
            }

            if (!removed)
            {
                break;
            }
        }

        WriteLines(filePath, lines);
    }

    private static List<string> BuildBlockLines(string semanticType, string identifier, IReadOnlyDictionary<string, string> properties, int indent)
        => BuildBlockText(ToBlockType(semanticType), identifier, properties, indent).Split('\n').ToList();

    private static string BuildBlockText(string blockType, string identifier, IReadOnlyDictionary<string, string> properties, int indent)
    {
        var baseIndent = new string(' ', indent);
        var childIndent = new string(' ', indent + 4);
        var lines = new List<string> { $"{baseIndent}{blockType} {Slug(identifier)} (" };
        foreach (var property in properties.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"{childIndent}{property.Key}: {property.Value}");
        }

        lines.Add($"{baseIndent})");
        return string.Join("\n", lines) + "\n";
    }

    private static Dictionary<string, string> MergeProperties(OracleApexSemanticEditOperation operation, IReadOnlyDictionary<string, string> defaults)
    {
        var result = new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in operation.Properties)
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }

    private void EnsureRequiredProperties(string semanticType, IReadOnlyDictionary<string, string> properties)
    {
        EnsureComponentExists(semanticType);
        var component = _componentCatalog.GetComponent(semanticType);
        foreach (var required in component.RequiredProperties)
        {
            if (!properties.TryGetValue(required, out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Component '{semanticType}' requires property '{required}'.");
            }
        }
    }

    private void EnsureChildPlacement(string parentType, string childType)
    {
        EnsureComponentExists(parentType);
        EnsureComponentExists(childType);
        if (!_componentCatalog.GetComponent(parentType).ChildComponents.Contains(childType, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Component '{childType}' is not valid inside '{parentType}'.");
        }
    }

    private void EnsureComponentExists(string semanticType)
    {
        if (!_componentCatalog.TryGetComponent(semanticType, out _))
        {
            throw new InvalidOperationException($"Unknown Oracle APEX component type '{semanticType}'.");
        }
    }

    private static void EnsureNoDuplicate(OracleApexWorkspaceIndex index, string semanticType, string identifier, string parentNodeId)
    {
        if (index.Entries.Any(entry => string.Equals(entry.SemanticType, semanticType, StringComparison.OrdinalIgnoreCase) && string.Equals(entry.Identifier, identifier, StringComparison.OrdinalIgnoreCase) && string.Equals(entry.ParentNodeId, parentNodeId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Component '{semanticType}' with identifier '{identifier}' already exists under the selected parent.");
        }
    }

    private static OracleApexWorkspaceIndexEntry RequireEntry(OracleApexWorkspaceIndex index, string? semanticType, string identifier, string? parentIdentifier = null)
    {
        var matches = index.Entries.Where(entry => (string.IsNullOrWhiteSpace(semanticType) || string.Equals(entry.SemanticType, semanticType, StringComparison.OrdinalIgnoreCase)) && string.Equals(entry.Identifier, identifier, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(parentIdentifier))
        {
            matches = matches.Where(entry => index.Entries.Any(parent => string.Equals(parent.NodeId, entry.ParentNodeId, StringComparison.OrdinalIgnoreCase) && string.Equals(parent.Identifier, parentIdentifier, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Could not find component '{semanticType ?? "any"}' with identifier '{identifier}'."),
            _ => throw new InvalidOperationException($"Component '{semanticType ?? "any"}' with identifier '{identifier}' is ambiguous."),
        };
    }

    private static void BackupFile(IDictionary<string, string?> backups, string filePath)
    {
        if (!backups.ContainsKey(filePath))
        {
            backups[filePath] = File.Exists(filePath) ? File.ReadAllText(filePath) : null;
        }
    }

    private static void RestoreBackups(IReadOnlyDictionary<string, string?> backups)
    {
        foreach (var backup in backups)
        {
            if (backup.Value is null)
            {
                if (File.Exists(backup.Key))
                {
                    File.Delete(backup.Key);
                }
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup.Key)!);
                File.WriteAllText(backup.Key, backup.Value.Replace("\r\n", "\n", StringComparison.Ordinal));
            }
        }
    }

    private static string GetSourcePath(string rootPath, OracleApexEnvironmentPreferences environment)
        => Path.Combine(rootPath, (environment.SourcePath ?? "src/apex").Replace('/', Path.DirectorySeparatorChar));

    private static string GetAbsolutePath(string rootPath, OracleApexEnvironmentPreferences environment, string relativeSourceFile)
        => Path.Combine(GetSourcePath(rootPath, environment), relativeSourceFile.Replace('/', Path.DirectorySeparatorChar));

    private static int ComputeChildIndent(string filePath, int startLine)
    {
        var lines = ReadFileLines(filePath);
        var line = lines[Math.Max(0, startLine - 1)];
        var trimmed = line.TrimStart();
        return (line.Length - trimmed.Length) + 4;
    }

    private static List<string> ReadFileLines(string filePath)
        => File.Exists(filePath)
            ? File.ReadAllText(filePath).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList()
            : [];

    private static void WriteLines(string filePath, IReadOnlyList<string> lines)
        => WriteFile(filePath, string.Join("\n", lines).TrimEnd('\n') + "\n");

    private static void WriteFile(string filePath, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static int ReadRequiredInt(IReadOnlyDictionary<string, string> properties, string key)
        => properties.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Property '{key}' must be a valid integer.");

    private static string Slug(string value)
        => string.Concat(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-')).Trim('-');

    private static string DefaultParentTypeFor(string childType)
        => childType switch
        {
            "navigation-entry" => "navigation-menu",
            _ => "page",
        };

    private static string PrimaryNamePropertyFor(string semanticType)
        => semanticType switch
        {
            "region" => "title",
            "navigation-entry" => "label",
            _ => "name",
        };

    private static string ToBlockType(string semanticType)
        => semanticType switch
        {
            "dynamic-action" => "dynamic action",
            "authorization-scheme" => "authorization scheme",
            "authentication-scheme" => "authentication scheme",
            "navigation-menu" => "navigation menu",
            "navigation-entry" => "entry",
            "build-option" => "build option",
            "static-file" => "static file",
            "lov" => "list of values",
            "deployment-profile" => "deployment",
            _ => semanticType.Replace('-', ' '),
        };

    private static string GetSharedComponentDirectory(string componentType)
        => componentType switch
        {
            "authorization-scheme" => "shared_components/authorization_schemes",
            "authentication-scheme" => "shared_components/authentication_schemes",
            "navigation-menu" => "shared_components/navigation_menus",
            "list" => "shared_components/lists",
            "lov" => "shared_components/lovs",
            "build-option" => "shared_components/build_options",
            "static-file" => "shared_components/static_files",
            "plugin" => "shared_components/plugins",
            "rest-data-source" => "shared_components/rest_data_sources",
            "rest-module" => "shared_components/rest_modules",
            "rest-handler" => "shared_components/rest_handlers",
            _ => throw new InvalidOperationException($"Shared component type '{componentType}' is not supported."),
        };

    private static IReadOnlyList<string> SharedReferencePropertiesFor(string? componentType)
        => componentType switch
        {
            "authorization-scheme" => ["authorization-scheme"],
            "authentication-scheme" => ["authentication-scheme"],
            "list" => ["list"],
            "lov" => ["lov"],
            "build-option" => ["build-option"],
            "plugin" => ["plugin"],
            _ => Array.Empty<string>(),
        };
}

public sealed class OracleApexSemanticEditOperation
{
    public OracleApexSemanticEditKind Kind { get; init; }
    public string ComponentType { get; init; } = string.Empty;
    public string TargetIdentifier { get; init; } = string.Empty;
    public string ParentIdentifier { get; init; } = string.Empty;
    public string ParentSemanticType { get; init; } = string.Empty;
    public string DestinationParentIdentifier { get; init; } = string.Empty;
    public string NewIdentifier { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static OracleApexSemanticEditOperation AddPage(string pageName, IReadOnlyDictionary<string, string> properties)
        => new() { Kind = OracleApexSemanticEditKind.AddPage, NewIdentifier = pageName, Properties = new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase) };

    public static OracleApexSemanticEditOperation RemovePage(string pageName)
        => new() { Kind = OracleApexSemanticEditKind.RemovePage, TargetIdentifier = pageName };

    public static OracleApexSemanticEditOperation RenamePage(string pageName, string newPageName, IReadOnlyDictionary<string, string>? properties = null)
        => new() { Kind = OracleApexSemanticEditKind.RenamePage, TargetIdentifier = pageName, NewIdentifier = newPageName, Properties = properties is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase) };

    public static OracleApexSemanticEditOperation AddRegion(string pageName, string regionName, IReadOnlyDictionary<string, string>? properties = null)
        => new() { Kind = OracleApexSemanticEditKind.AddRegion, ParentIdentifier = pageName, ParentSemanticType = "page", NewIdentifier = regionName, Properties = properties is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase) };

    public static OracleApexSemanticEditOperation RemoveRegion(string pageName, string regionName)
        => new() { Kind = OracleApexSemanticEditKind.RemoveRegion, ParentIdentifier = pageName, TargetIdentifier = regionName };

    public static OracleApexSemanticEditOperation MoveRegion(string sourcePageName, string regionName, string destinationPageName)
        => new() { Kind = OracleApexSemanticEditKind.MoveRegion, ParentIdentifier = sourcePageName, TargetIdentifier = regionName, DestinationParentIdentifier = destinationPageName };

    public static OracleApexSemanticEditOperation RenameRegion(string pageName, string regionName, string newRegionName)
        => new() { Kind = OracleApexSemanticEditKind.RenameRegion, ParentIdentifier = pageName, TargetIdentifier = regionName, NewIdentifier = newRegionName };

    public static OracleApexSemanticEditOperation AddItem(string pageName, string itemName, IReadOnlyDictionary<string, string>? properties = null)
        => new() { Kind = OracleApexSemanticEditKind.AddItem, ParentIdentifier = pageName, ParentSemanticType = "page", NewIdentifier = itemName, Properties = properties is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase) };

    public static OracleApexSemanticEditOperation RemoveItem(string pageName, string itemName)
        => new() { Kind = OracleApexSemanticEditKind.RemoveItem, ParentIdentifier = pageName, TargetIdentifier = itemName };

    public static OracleApexSemanticEditOperation RenameItem(string pageName, string itemName, string newItemName)
        => new() { Kind = OracleApexSemanticEditKind.RenameItem, ParentIdentifier = pageName, TargetIdentifier = itemName, NewIdentifier = newItemName };

    public static OracleApexSemanticEditOperation AddButton(string pageName, string buttonName, IReadOnlyDictionary<string, string>? properties = null)
        => new() { Kind = OracleApexSemanticEditKind.AddButton, ParentIdentifier = pageName, ParentSemanticType = "page", NewIdentifier = buttonName, Properties = properties is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase) };

    public static OracleApexSemanticEditOperation AddProcess(string pageName, string processName, IReadOnlyDictionary<string, string>? properties = null)
        => new() { Kind = OracleApexSemanticEditKind.AddProcess, ParentIdentifier = pageName, ParentSemanticType = "page", NewIdentifier = processName, Properties = properties is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase) };

    public static OracleApexSemanticEditOperation AddDynamicAction(string pageName, string dynamicActionName, IReadOnlyDictionary<string, string>? properties = null)
        => new() { Kind = OracleApexSemanticEditKind.AddDynamicAction, ParentIdentifier = pageName, ParentSemanticType = "page", NewIdentifier = dynamicActionName, Properties = properties is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase) };

    public static OracleApexSemanticEditOperation AddSharedComponent(string componentType, string identifier, IReadOnlyDictionary<string, string>? properties = null)
        => new() { Kind = OracleApexSemanticEditKind.AddSharedComponent, ComponentType = componentType, NewIdentifier = identifier, Properties = properties is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase) };

    public static OracleApexSemanticEditOperation RenameSharedComponent(string componentType, string identifier, string newIdentifier)
        => new() { Kind = OracleApexSemanticEditKind.RenameSharedComponent, ComponentType = componentType, TargetIdentifier = identifier, NewIdentifier = newIdentifier };

    public static OracleApexSemanticEditOperation AddNavigationEntry(string menuName, string entryName, IReadOnlyDictionary<string, string>? properties = null)
        => new() { Kind = OracleApexSemanticEditKind.AddNavigationEntry, ParentIdentifier = menuName, ParentSemanticType = "navigation-menu", NewIdentifier = entryName, Properties = properties is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase) };

    public static OracleApexSemanticEditOperation RenameNavigationEntry(string menuName, string entryName, string newEntryName)
        => new() { Kind = OracleApexSemanticEditKind.RenameNavigationEntry, ParentIdentifier = menuName, TargetIdentifier = entryName, NewIdentifier = newEntryName };
}

public enum OracleApexSemanticEditKind
{
    AddPage,
    RemovePage,
    RenamePage,
    AddRegion,
    RemoveRegion,
    MoveRegion,
    RenameRegion,
    AddItem,
    RemoveItem,
    RenameItem,
    AddButton,
    AddProcess,
    AddDynamicAction,
    AddSharedComponent,
    RenameSharedComponent,
    AddNavigationEntry,
    RenameNavigationEntry,
}

public sealed class OracleApexSemanticEditResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public OracleApexSemanticEditDiagnostics Diagnostics { get; init; } = new();
    public OracleApexWorkspaceIndex WorkspaceIndex { get; init; } = new();
    public IReadOnlyList<string> ChangedFiles { get; init; } = Array.Empty<string>();
}

public sealed class OracleApexSemanticEditDiagnostics
{
    public IReadOnlyList<OracleApexWorkspaceIndexDiagnostic> Entries { get; init; } = Array.Empty<OracleApexWorkspaceIndexDiagnostic>();
}
