using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class OracleApexValidationFeedbackService
{
    private static readonly Regex StructuredDiagnosticPattern = new(@"^(?<severity>ERROR|WARNING|INFO)\s+(?<file>.+?):(?<line>\d+):(?<column>\d+)(?:\s+\[(?<code>[^\]]+)\])?(?:\s+component=(?<component>[^\s]+))?(?:\s+property=(?<property>[^\s]+))?\s*[:-]\s*(?<message>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PositionalDiagnosticPattern = new(@"(?<file>[^:\r\n]+\.apx)[:(](?<line>\d+)[,:](?<column>\d+)\)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CompilerCodePattern = new(@"\b(?<code>(?:ORA|APEX|SP2)-\d+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PropertyPattern = new(@"(?:property|attribute)\s+'(?<property>[^']+)'", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex IdentifierPattern = new(@"'(?<identifier>[^']+)'", RegexOptions.Compiled);

    public OracleApexValidationResult BuildValidationResult(ProcessResult? processResult, OracleApexWorkspaceIndex index, OracleApexEditPlan plan)
    {
        if (processResult is null)
        {
            return new OracleApexValidationResult { IsSuccess = true, Summary = "Validation completed without process output." };
        }

        var diagnostics = ParseDiagnostics(processResult, index.SourcePath);
        var mappings = diagnostics.Select(diagnostic => MapDiagnostic(diagnostic, index, plan)).ToList();
        return new OracleApexValidationResult
        {
            IsSuccess = processResult.IsSuccess && diagnostics.All(item => !string.Equals(item.Severity, "Error", StringComparison.OrdinalIgnoreCase)),
            Summary = diagnostics.Count == 0
                ? (processResult.IsSuccess ? "Validation passed." : "Validation failed without mapped compiler diagnostics.")
                : $"Validation reported {diagnostics.Count} compiler diagnostic(s).",
            Diagnostics = diagnostics,
            Mappings = mappings,
        };
    }

    public OracleApexValidationResult MapValidationResult(OracleApexValidationResult validation, OracleApexWorkspaceIndex index, OracleApexEditPlan plan)
    {
        var diagnostics = validation.Diagnostics.Select(diagnostic => NormalizeDiagnostic(diagnostic, index.SourcePath)).ToList();
        return new OracleApexValidationResult
        {
            IsSuccess = validation.IsSuccess,
            Summary = validation.Summary,
            Diagnostics = diagnostics,
            Mappings = diagnostics.Select(diagnostic => MapDiagnostic(diagnostic, index, plan)).ToList(),
        };
    }

    public void PersistEvidence(WorkspaceSnapshot snapshot, OracleApexValidationResult validation, OracleApexEditPlan? repairPlan = null)
    {
        var knowledgeRoot = Path.Combine(snapshot.Paths.OpencodePath, "knowledge", "apex-assistant");
        Directory.CreateDirectory(knowledgeRoot);
        var statePath = Path.Combine(knowledgeRoot, "evidence.json");
        var state = File.Exists(statePath)
            ? JsonSerializer.Deserialize<OracleApexAssistantWorkspaceEvidenceState>(File.ReadAllText(statePath)) ?? new OracleApexAssistantWorkspaceEvidenceState()
            : new OracleApexAssistantWorkspaceEvidenceState();

        foreach (var diagnostic in validation.Diagnostics)
        {
            var componentType = string.IsNullOrWhiteSpace(diagnostic.Component) ? "unknown" : diagnostic.Component;
            state.ValidationByComponentType.TryGetValue(componentType, out var current);
            state.ValidationByComponentType[componentType] = new OracleApexComponentValidationEvidence
            {
                SuccessCount = (current?.SuccessCount ?? 0) + (validation.IsSuccess ? 1 : 0),
                FailureCount = (current?.FailureCount ?? 0) + (validation.IsSuccess ? 0 : 1),
            };

            if (string.Equals(diagnostic.Category, "missing-required-property", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(diagnostic.Property))
            {
                state.MissingProperties[diagnostic.Property] = state.MissingProperties.TryGetValue(diagnostic.Property, out var count) ? count + 1 : 1;
            }
        }

        if (!validation.IsSuccess)
        {
            foreach (var mapping in validation.Mappings.Where(item => item.PlannedOperationSequence > 0))
            {
                state.FailedBlueprintOperations[mapping.PlannedOperationTitle] = state.FailedBlueprintOperations.TryGetValue(mapping.PlannedOperationTitle, out var count) ? count + 1 : 1;
            }
        }

        if (repairPlan is not null)
        {
            foreach (var operation in repairPlan.Operations)
            {
                state.AppliedRepairActions[operation.Title] = state.AppliedRepairActions.TryGetValue(operation.Title, out var count) ? count + 1 : 1;
            }
        }

        File.WriteAllText(statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    public OracleApexAssistantWorkspaceEvidenceState ReadEvidence(WorkspaceSnapshot snapshot)
    {
        var statePath = Path.Combine(snapshot.Paths.OpencodePath, "knowledge", "apex-assistant", "evidence.json");
        return File.Exists(statePath)
            ? JsonSerializer.Deserialize<OracleApexAssistantWorkspaceEvidenceState>(File.ReadAllText(statePath)) ?? new OracleApexAssistantWorkspaceEvidenceState()
            : new OracleApexAssistantWorkspaceEvidenceState();
    }

    public void AppendEvidenceEntry(WorkspaceSnapshot snapshot, OracleApexAssistantEvidenceEntry entry)
    {
        var knowledgeRoot = Path.Combine(snapshot.Paths.OpencodePath, "knowledge", "apex-assistant");
        Directory.CreateDirectory(knowledgeRoot);
        var statePath = Path.Combine(knowledgeRoot, "evidence.json");
        var state = ReadEvidence(snapshot);
        var entries = state.Entries.ToList();
        entries.Add(entry);
        if (entries.Count > 20)
        {
            entries = entries.OrderByDescending(item => item.TimestampUtc).Take(20).OrderBy(item => item.TimestampUtc).ToList();
        }

        var updated = new OracleApexAssistantWorkspaceEvidenceState
        {
            ValidationByComponentType = state.ValidationByComponentType,
            MissingProperties = state.MissingProperties,
            FailedBlueprintOperations = state.FailedBlueprintOperations,
            AppliedRepairActions = state.AppliedRepairActions,
            Entries = entries,
        };
        File.WriteAllText(statePath, JsonSerializer.Serialize(updated, new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    public OracleApexAssistantRollbackManifest? ReadRollbackManifest(WorkspaceSnapshot snapshot)
    {
        var manifestPath = Path.Combine(snapshot.Paths.OpencodePath, "knowledge", "apex-assistant", "rollback-manifest.json");
        return File.Exists(manifestPath)
            ? JsonSerializer.Deserialize<OracleApexAssistantRollbackManifest>(File.ReadAllText(manifestPath))
            : null;
    }

    public void WriteRollbackManifest(WorkspaceSnapshot snapshot, OracleApexAssistantRollbackManifest manifest)
    {
        var knowledgeRoot = Path.Combine(snapshot.Paths.OpencodePath, "knowledge", "apex-assistant");
        Directory.CreateDirectory(knowledgeRoot);
        var manifestPath = Path.Combine(knowledgeRoot, "rollback-manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    public OracleApexAssistantRollbackManifest CreateRollbackManifest(WorkspaceSnapshot snapshot, string environmentName, string sourcePath, IReadOnlyList<string> changedFiles)
    {
        var executionId = $"apex-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        var transactionRoot = Path.Combine(snapshot.Paths.OpencodePath, "knowledge", "apex-assistant", "transactions", executionId, "before");
        Directory.CreateDirectory(transactionRoot);
        var files = new List<OracleApexAssistantRollbackFile>();
        foreach (var changedFile in changedFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = NormalizeRelativePath(snapshot.Paths.RootPath, changedFile);
            var absolutePath = ResolveManifestAbsolutePath(snapshot.Paths.RootPath, sourcePath, relativePath);
            var backupRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            var backupAbsolutePath = Path.Combine(transactionRoot, backupRelativePath);
            var existedBefore = File.Exists(absolutePath);
            Directory.CreateDirectory(Path.GetDirectoryName(backupAbsolutePath)!);
            if (existedBefore)
            {
                File.Copy(absolutePath, backupAbsolutePath, overwrite: true);
            }

            files.Add(new OracleApexAssistantRollbackFile
            {
                RelativePath = relativePath,
                OriginalHash = existedBefore ? ComputeFileHash(absolutePath) : string.Empty,
                BackupRelativePath = NormalizeRelativePath(snapshot.Paths.OpencodePath, backupAbsolutePath),
                ExistedBeforeExecution = existedBefore,
            });
        }

        return new OracleApexAssistantRollbackManifest
        {
            ExecutionId = executionId,
            TimestampUtc = DateTimeOffset.UtcNow,
            EnvironmentName = environmentName,
            SourcePath = sourcePath,
            RollbackState = OracleApexAssistantRollbackState.Available,
            Files = files,
        };
    }

    public OracleApexAssistantRollbackManifest FinalizeRollbackManifest(WorkspaceSnapshot snapshot, OracleApexAssistantRollbackManifest manifest)
        => new()
        {
            ExecutionId = manifest.ExecutionId,
            TimestampUtc = manifest.TimestampUtc,
            EnvironmentName = manifest.EnvironmentName,
            SourcePath = manifest.SourcePath,
            RollbackState = manifest.RollbackState,
                RollbackBlockedReason = manifest.RollbackBlockedReason,
                RollbackResult = manifest.RollbackResult,
                Files = manifest.Files.Select(file => new OracleApexAssistantRollbackFile
                {
                    RelativePath = file.RelativePath,
                    OriginalHash = file.OriginalHash,
                    PostExecutionHash = ComputeCurrentHash(snapshot.Paths.RootPath, manifest.SourcePath, file.RelativePath),
                    BackupRelativePath = file.BackupRelativePath,
                    ExistedBeforeExecution = file.ExistedBeforeExecution,
                }).ToList(),
        };

    public (bool IsSafe, string Reason) CanRollback(WorkspaceSnapshot snapshot, OracleApexAssistantRollbackManifest? manifest)
    {
        if (manifest is null)
        {
            return (false, "No assistant rollback manifest is available.");
        }

        if (manifest.RollbackState == OracleApexAssistantRollbackState.Completed)
        {
            return (false, "Rollback already completed for this assistant transaction.");
        }

        var changedAfterExecution = new List<string>();
        foreach (var file in manifest.Files)
        {
            var absolutePath = ResolveManifestAbsolutePath(snapshot.Paths.RootPath, manifest.SourcePath, file.RelativePath);
            var currentHash = File.Exists(absolutePath) ? ComputeFileHash(absolutePath) : string.Empty;
            if (!string.Equals(currentHash, file.PostExecutionHash, StringComparison.OrdinalIgnoreCase))
            {
                changedAfterExecution.Add(file.RelativePath);
            }
        }

        return changedAfterExecution.Count > 0
            ? (false, $"Later edits were detected in: {string.Join(", ", changedAfterExecution)}")
            : (true, string.Empty);
    }

    public static string ComputeFileHash(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static string NormalizeRelativePath(string rootPath, string path)
        => Path.IsPathRooted(path)
            ? Path.GetRelativePath(rootPath, path).Replace(Path.DirectorySeparatorChar, '/')
            : path.Replace(Path.DirectorySeparatorChar, '/');

    private static string ComputeCurrentHash(string rootPath, string sourcePath, string relativePath)
    {
        var absolutePath = ResolveManifestAbsolutePath(rootPath, sourcePath, relativePath);
        return File.Exists(absolutePath) ? ComputeFileHash(absolutePath) : string.Empty;
    }

    private static string ResolveManifestAbsolutePath(string rootPath, string sourcePath, string relativePath)
    {
        var normalizedRelative = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var normalizedSource = sourcePath.Replace('/', Path.DirectorySeparatorChar);
        return normalizedRelative.StartsWith(normalizedSource + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(rootPath, normalizedRelative)
            : Path.Combine(rootPath, normalizedSource, normalizedRelative);
    }

    public OracleApexAssistantWorkspaceSettings ReadWorkspaceSettings(WorkspaceSnapshot snapshot)
    {
        var path = Path.Combine(snapshot.Paths.OpencodePath, "knowledge", "apex-assistant", "settings.json");
        if (!File.Exists(path))
        {
            return new OracleApexAssistantWorkspaceSettings();
        }

        return JsonSerializer.Deserialize<OracleApexAssistantWorkspaceSettings>(File.ReadAllText(path)) ?? new OracleApexAssistantWorkspaceSettings();
    }

    private static List<OracleApexCompilerDiagnostic> ParseDiagnostics(ProcessResult processResult, string sourcePath)
    {
        var diagnostics = new List<OracleApexCompilerDiagnostic>();
        foreach (var rawLine in processResult.StandardErrorLines.Concat(processResult.StandardOutputLines).Select(line => line.Trim()).Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            var structured = StructuredDiagnosticPattern.Match(rawLine);
            if (structured.Success)
            {
                diagnostics.Add(new OracleApexCompilerDiagnostic
                {
                    FilePath = NormalizeFilePath(structured.Groups["file"].Value, sourcePath),
                    Line = ParseInt(structured.Groups["line"].Value),
                    Column = ParseInt(structured.Groups["column"].Value),
                    Component = structured.Groups["component"].Value.Trim(),
                    Property = structured.Groups["property"].Value.Trim(),
                    Severity = NormalizeSeverity(structured.Groups["severity"].Value),
                    CompilerCode = structured.Groups["code"].Value.Trim(),
                    Message = structured.Groups["message"].Value.Trim(),
                    Category = Categorize(rawLine, structured.Groups["property"].Value.Trim()),
                });
                continue;
            }

            var positional = PositionalDiagnosticPattern.Match(rawLine);
            var compilerCode = CompilerCodePattern.Match(rawLine).Groups["code"].Value.Trim();
            var property = PropertyPattern.Match(rawLine).Groups["property"].Value.Trim();
            if (positional.Success || !string.IsNullOrWhiteSpace(compilerCode) || rawLine.Contains("error", StringComparison.OrdinalIgnoreCase) || rawLine.Contains("warning", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new OracleApexCompilerDiagnostic
                {
                    FilePath = positional.Success ? NormalizeFilePath(positional.Groups["file"].Value, sourcePath) : string.Empty,
                    Line = positional.Success ? ParseInt(positional.Groups["line"].Value) : 0,
                    Column = positional.Success ? ParseInt(positional.Groups["column"].Value) : 0,
                    Component = InferComponent(rawLine),
                    Property = property,
                    Severity = rawLine.Contains("warning", StringComparison.OrdinalIgnoreCase) ? "Warning" : "Error",
                    CompilerCode = compilerCode,
                    Message = rawLine,
                    Category = Categorize(rawLine, property),
                });
            }
        }

        return diagnostics;
    }

    private static OracleApexDiagnosticMapping MapDiagnostic(OracleApexCompilerDiagnostic diagnostic, OracleApexWorkspaceIndex index, OracleApexEditPlan plan)
    {
        var entry = ResolveEntry(diagnostic, index);
        var operation = ResolveOperation(diagnostic, entry, index, plan);
        return new OracleApexDiagnosticMapping
        {
            Diagnostic = diagnostic,
            SemanticNodeId = entry?.NodeId ?? string.Empty,
            WorkspaceIdentifier = entry?.Identifier ?? string.Empty,
            WorkspaceSemanticType = entry?.SemanticType ?? string.Empty,
            PlannedOperationSequence = operation?.Sequence ?? 0,
            PlannedOperationTitle = operation?.Title ?? string.Empty,
            BlueprintModule = ResolveBlueprintValue(entry?.Identifier, plan.BlueprintModules),
            BlueprintEntity = ResolveBlueprintValue(entry?.Identifier, plan.BlueprintEntities),
        };
    }

    private static OracleApexWorkspaceIndexEntry? ResolveEntry(OracleApexCompilerDiagnostic diagnostic, OracleApexWorkspaceIndex index)
    {
        var normalizedFile = diagnostic.FilePath.Replace('\\', '/');
        var byLocation = index.Entries
            .Where(entry => string.Equals(entry.SourceFile.Replace('\\', '/'), normalizedFile, StringComparison.OrdinalIgnoreCase)
                && (diagnostic.Line == 0 || (entry.Line <= diagnostic.Line && entry.EndLine >= diagnostic.Line)))
            .OrderByDescending(entry => entry.Line)
            .ThenBy(entry => Math.Max(0, entry.EndLine - entry.Line))
            .FirstOrDefault();
        if (byLocation is not null)
        {
            return byLocation;
        }

        if (!string.IsNullOrWhiteSpace(diagnostic.FilePath))
        {
            return null;
        }

        var quotedIdentifier = IdentifierPattern.Match(diagnostic.Message).Groups["identifier"].Value.Trim();
        return index.Entries.FirstOrDefault(entry => (!string.IsNullOrWhiteSpace(quotedIdentifier) && string.Equals(entry.Identifier, quotedIdentifier, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(diagnostic.Component) && string.Equals(entry.SemanticType, diagnostic.Component, StringComparison.OrdinalIgnoreCase)));
    }

    private static OracleApexPlannedOperation? ResolveOperation(OracleApexCompilerDiagnostic diagnostic, OracleApexWorkspaceIndexEntry? entry, OracleApexWorkspaceIndex index, OracleApexEditPlan plan)
    {
        var workspaceFile = ToWorkspaceRelativePath(diagnostic.FilePath, index.SourcePath, pathIsSourceRelative: true);
        return plan.Operations.FirstOrDefault(operation => (workspaceFile?.Length > 0 && operation.ExpectedChangedFiles.Any(file => string.Equals(ToWorkspaceRelativePath(file, index.SourcePath, pathIsSourceRelative: true), workspaceFile, StringComparison.OrdinalIgnoreCase)))
            || (entry is not null && operation.AffectedSymbols.Any(symbol => string.Equals(symbol, entry.Identifier, StringComparison.OrdinalIgnoreCase)))
            || (!string.IsNullOrWhiteSpace(entry?.SemanticType) && string.Equals(operation.TargetComponentType, entry.SemanticType, StringComparison.OrdinalIgnoreCase)));
    }

    private static string ResolveBlueprintValue(string? identifier, IReadOnlyList<string> values)
        => string.IsNullOrWhiteSpace(identifier)
            ? string.Empty
            : values.FirstOrDefault(value => identifier.Contains(value, StringComparison.OrdinalIgnoreCase) || value.Contains(identifier, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

    private static string NormalizeFilePath(string path, string sourcePath)
    {
        var normalized = NormalizePath(path);
        var sourceSegments = SplitSafePath(sourcePath);
        var pathSegments = SplitSafePath(normalized);
        if (pathSegments is null)
        {
            return normalized;
        }

        if (sourceSegments is { Count: > 0 })
        {
            var sourceIndex = FindSourceRoot(pathSegments, sourceSegments);
            if (sourceIndex >= 0)
            {
                return string.Join('/', pathSegments.Skip(sourceIndex + sourceSegments.Count));
            }
        }

        return normalized;
    }

    private static string? ToWorkspaceRelativePath(string path, string sourcePath, bool pathIsSourceRelative)
    {
        var pathSegments = SplitSafePath(path);
        var sourceSegments = SplitSafePath(sourcePath);
        if (pathSegments is null || sourceSegments is null)
        {
            return null;
        }

        if (sourceSegments.Count == 0)
        {
            return string.Join('/', pathSegments);
        }

        var sourceIndex = FindSourceRoot(pathSegments, sourceSegments);
        if (sourceIndex >= 0)
        {
            return string.Join('/', pathSegments.Skip(sourceIndex));
        }

        if (!pathIsSourceRelative || IsRootedPortable(path))
        {
            return null;
        }

        return string.Join('/', sourceSegments.Concat(pathSegments));
    }

    private static string NormalizePath(string path)
        => path.Trim().Replace('\\', '/');

    private static IReadOnlyList<string>? SplitSafePath(string path)
    {
        var segments = NormalizePath(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == ".."))
        {
            return null;
        }

        return segments.Where(segment => segment != ".").ToList();
    }

    private static int FindSourceRoot(IReadOnlyList<string> path, IReadOnlyList<string> source)
    {
        foreach (var index in new[] { 0, 1, 2 })
        {
            var hasKnownPrefix = index == 0
                || index == 1 && path.Count > 0 && string.Equals(path[0], "workspace", StringComparison.OrdinalIgnoreCase)
                || index == 2 && path.Count > 1 && path[0].Length == 2 && path[0][1] == ':' && string.Equals(path[1], "workspace", StringComparison.OrdinalIgnoreCase);
            if (hasKnownPrefix && index + source.Count <= path.Count
                && source.Select((segment, offset) => string.Equals(path[index + offset], segment, StringComparison.OrdinalIgnoreCase)).All(match => match))
            {
                return index;
            }
        }

        return -1;
    }

    private static OracleApexCompilerDiagnostic NormalizeDiagnostic(OracleApexCompilerDiagnostic diagnostic, string sourcePath)
        => new()
        {
            FilePath = string.IsNullOrWhiteSpace(diagnostic.FilePath) ? string.Empty : NormalizeFilePath(diagnostic.FilePath, sourcePath),
            Line = diagnostic.Line,
            Column = diagnostic.Column,
            Component = diagnostic.Component,
            Property = diagnostic.Property,
            Severity = diagnostic.Severity,
            CompilerCode = diagnostic.CompilerCode,
            Message = diagnostic.Message,
            Category = diagnostic.Category,
        };

    private static bool IsRootedPortable(string path)
    {
        var normalized = NormalizePath(path);
        return normalized.StartsWith("/", StringComparison.Ordinal) || Regex.IsMatch(normalized, @"^[A-Za-z]:/");
    }

    private static string NormalizeSeverity(string value)
        => value.Equals("warning", StringComparison.OrdinalIgnoreCase) ? "Warning" : value.Equals("info", StringComparison.OrdinalIgnoreCase) ? "Info" : "Error";

    private static int ParseInt(string value)
        => int.TryParse(value, out var parsed) ? parsed : 0;

    private static string InferComponent(string line)
    {
        foreach (var component in new[] { "page", "region", "item", "navigation-entry", "navigation-menu", "lov", "authorization-scheme", "authentication-scheme", "process" })
        {
            if (line.Contains(component, StringComparison.OrdinalIgnoreCase))
            {
                return component;
            }
        }

        return string.Empty;
    }

    private static string Categorize(string line, string property)
    {
        var lower = line.ToLowerInvariant();
        if (lower.Contains("missing required", StringComparison.Ordinal) || lower.Contains("required property", StringComparison.Ordinal)) return "missing-required-property";
        if (lower.Contains("invalid enum", StringComparison.Ordinal) || lower.Contains("invalid value", StringComparison.Ordinal)) return "invalid-property-value";
        if (lower.Contains("invalid child", StringComparison.Ordinal) || lower.Contains("parent", StringComparison.Ordinal) && lower.Contains("invalid", StringComparison.Ordinal)) return "invalid-parent-child-placement";
        if (lower.Contains("unresolved", StringComparison.Ordinal) || lower.Contains("missing shared component", StringComparison.Ordinal) || lower.Contains("reference", StringComparison.Ordinal)) return property.Equals("target-page", StringComparison.OrdinalIgnoreCase) ? "invalid-page-target" : "unresolved-component-reference";
        if (lower.Contains("duplicate", StringComparison.Ordinal)) return "duplicate-identifier";
        if (lower.Contains("malformed", StringComparison.Ordinal) || lower.Contains("unexpected", StringComparison.Ordinal)) return "malformed-generated-components";
        return string.Empty;
    }
}
