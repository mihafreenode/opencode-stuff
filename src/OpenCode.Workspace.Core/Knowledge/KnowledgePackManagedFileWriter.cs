using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Core.Knowledge;

internal sealed class KnowledgePackManagedFileWriter
{
    public ManagedKnowledgeWriteResult WriteFiles(string providerRootPath, IReadOnlyDictionary<string, string> files, ProvisionedKnowledgePackState? previousState, bool explicitRegenerationRequested)
    {
        var generatedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var skippedFiles = new List<string>();

        foreach (var file in files.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = KnowledgePackPathNormalizer.NormalizeRelativePath(file.Key);
            var outputPath = Path.Combine(providerRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var desiredHash = WorkspaceAppliedStateService.ComputeHash(file.Value.Replace("\r\n", "\n", StringComparison.Ordinal));
            generatedHashes[relativePath] = desiredHash;

            var shouldWrite = explicitRegenerationRequested || !File.Exists(outputPath);
            if (!shouldWrite)
            {
                var currentHash = WorkspaceAppliedStateService.ComputeHash(File.ReadAllText(outputPath).Replace("\r\n", "\n", StringComparison.Ordinal));
                shouldWrite = previousState?.GeneratedFileHashes.TryGetValue(relativePath, out var previousHash) == true
                    && string.Equals(currentHash, previousHash, StringComparison.OrdinalIgnoreCase);
            }

            if (!shouldWrite)
            {
                skippedFiles.Add(relativePath);
                warnings.Add($"Knowledge Pack output '{relativePath}' was preserved because it appears to contain user edits.");
                continue;
            }

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outputPath, file.Value.Replace("\r\n", "\n", StringComparison.Ordinal));
        }

        return new ManagedKnowledgeWriteResult
        {
            GeneratedFileHashes = generatedHashes,
            Warnings = warnings,
            SkippedFiles = skippedFiles,
        };
    }
}

internal sealed class ManagedKnowledgeWriteResult
{
    public required Dictionary<string, string> GeneratedFileHashes { get; init; }

    public required List<string> Warnings { get; init; }

    public required List<string> SkippedFiles { get; init; }
}
