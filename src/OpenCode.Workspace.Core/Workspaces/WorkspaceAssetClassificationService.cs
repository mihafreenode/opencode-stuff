using OpenCode.Workspace.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenCode.Workspace.Core.Workspaces;

public sealed class WorkspaceAssetClassificationService
{
    private readonly ISerializer _serializer;

    private static readonly string[] DurableExact =
    [
        "workspace.yaml",
        "workspace.yml",
        ".opencode/profile.yaml",
        ".opencode/profile.yml",
        "AGENTS.md",
        "history/timeline.yaml",
    ];

    private static readonly string[] DurablePrefixes =
    [
        "docs/",
        "sources/",
        "knowledge/",
        "work/",
        "artifacts/",
        "examples/",
        "samples/",
        "scripts/",
        "tutorial/",
        "catalog/",
        "Localization/",
        "tests/",
        ".local/oracle/downloads/",
    ];

    private static readonly string[] GeneratedExact =
    [
        "compose.yaml",
        ".env",
        "attach-workspace.ps1",
        "terminal-diagnostics.ps1",
        "mounts/config/applied-state.yaml",
        "history/checkpoints/index.yaml",
        "artifacts/index.json",
        "runtimes/default.yaml",
    ];

    private static readonly string[] GeneratedPrefixes =
    [
        "mounts/config/",
        "docs/capabilities/",
        "docs/reference/agent-onboarding/",
    ];

    private static readonly string[] EphemeralExact =
    [
        "attach-diagnostics.log",
    ];

    private static readonly string[] EphemeralPrefixes =
    [
        ".git/",
        ".opencode/local/",
        "mounts/home/",
        "mounts/user/",
        "mounts/inbox/",
        "history/checkpoints/",
        "artifacts/runs/",
        ".cache/",
        "tmp/",
        "temp/",
        "node_modules/",
        "bin/",
        "obj/",
    ];

    public WorkspaceAssetClassificationService()
    {
        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
    }

    public WorkspaceAssetClassification Classify(string relativePath, bool isDirectory)
    {
        var normalizedPath = Normalize(relativePath, isDirectory);

        if (EphemeralExact.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase)
            || EphemeralPrefixes.Any(prefix => normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return new WorkspaceAssetClassification
            {
                Path = normalizedPath,
                IsDirectory = isDirectory,
                AssetClass = WorkspaceAssetClass.Ephemeral,
                Reason = "Disposable runtime, cache, or diagnostics content.",
            };
        }

        if (GeneratedExact.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase)
            || GeneratedPrefixes.Any(prefix => normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return new WorkspaceAssetClassification
            {
                Path = normalizedPath,
                IsDirectory = isDirectory,
                AssetClass = WorkspaceAssetClass.Generated,
                Reason = "Generated platform content that can be recreated from durable inputs.",
            };
        }

        if (DurableExact.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase)
            || DurablePrefixes.Any(prefix => normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return new WorkspaceAssetClassification
            {
                Path = normalizedPath,
                IsDirectory = isDirectory,
                AssetClass = WorkspaceAssetClass.Durable,
                Reason = "Durable user or repository content that should remain understandable and recoverable.",
            };
        }

        return new WorkspaceAssetClassification
        {
            Path = normalizedPath,
            IsDirectory = isDirectory,
            AssetClass = WorkspaceAssetClass.Durable,
            Reason = "Workspace content defaults to durable when it is not clearly generated or ephemeral.",
        };
    }

    public WorkspaceBackupManifest BuildBackupManifest(
        WorkspaceSnapshot snapshot,
        DateTimeOffset exportedUtc,
        string? archiveFileName = null,
        long archiveSizeBytes = 0,
        int? includedFileCount = null,
        int excludedFileCount = 0,
        IReadOnlyList<string>? warnings = null)
    {
        var items = DiscoverWorkspaceItems(snapshot.Paths.RootPath)
            .Select(item => Classify(item.Path, item.IsDirectory))
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WorkspaceBackupManifest
        {
            ArchiveFileName = archiveFileName ?? string.Empty,
            ExportedUtc = exportedUtc,
            ArchiveSizeBytes = archiveSizeBytes,
            WorkspaceName = snapshot.Definition.Workspace.Name,
            WorkspaceId = string.IsNullOrWhiteSpace(snapshot.Definition.Workspace.Id)
                ? WorkspacePathBuilder.Slugify(snapshot.Definition.Workspace.Name)
                : snapshot.Definition.Workspace.Id,
            WorkspaceRoot = snapshot.Paths.RootPath,
            ConfigurationPath = snapshot.ConfigurationPath,
            TimelinePath = snapshot.Paths.TimelinePath,
            LatestSavePointUtc = snapshot.Safety.LocalRecovery.LatestSavePointUtc,
            LatestCheckpointUtc = snapshot.Safety.LocalRecovery.LatestCheckpointUtc,
            IncludedFileCount = includedFileCount ?? items.Count(item => !item.IsDirectory),
            ExcludedFileCount = excludedFileCount,
            Warnings = warnings?.ToList() ?? [],
            SourceOfTruthLocations =
            [
                snapshot.ConfigurationPath,
                "AGENTS.md",
                "docs/",
                "knowledge/",
                "work/",
                "artifacts/",
            ],
            DurableAssetGroups =
            [
                "workspace configuration and repository guidance",
                "source, documentation, knowledge, work, and artifact content",
                "user-authored AGENTS.md content and timeline history",
            ],
            GeneratedAssetGroups =
            [
                "compose, environment, attach, and provisioning files",
                "generated capability, onboarding, and troubleshooting content",
                "applied state, checkpoint index, and runtime descriptors",
            ],
            EphemeralAssetGroups =
            [
                "git internals, runtime mounts, diagnostics logs, checkpoint payloads, and caches",
            ],
            OwnershipNotes =
            [
                "User work is the durable asset. Tools and runtime state are infrastructure.",
                "Generated files are replaceable and should not become the only copy of important work.",
                "Ephemeral content is included in this full snapshot only for operator clarity and local recovery context.",
            ],
            Warning = "This export is a full workspace snapshot. It includes replaceable generated files and ephemeral runtime content in addition to durable user-owned assets.",
            Items = items,
        };
    }

    public string SerializeBackupManifest(WorkspaceBackupManifest manifest)
        => _serializer.Serialize(manifest);

    private static IReadOnlyList<(string Path, bool IsDirectory)> DiscoverWorkspaceItems(string workspaceRootPath)
    {
        var items = new List<(string Path, bool IsDirectory)>();
        VisitDirectory(workspaceRootPath, string.Empty, items);
        return items;
    }

    private static void VisitDirectory(string workspaceRootPath, string relativePath, List<(string Path, bool IsDirectory)> items)
    {
        var directoryPath = string.IsNullOrWhiteSpace(relativePath)
            ? workspaceRootPath
            : Path.Combine(workspaceRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

        foreach (var directory in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(directory);
            var childRelativePath = string.IsNullOrWhiteSpace(relativePath) ? name : $"{relativePath}/{name}";
            items.Add((Normalize(childRelativePath, isDirectory: true), true));
            VisitDirectory(workspaceRootPath, childRelativePath, items);
        }

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            var childRelativePath = string.IsNullOrWhiteSpace(relativePath) ? name : $"{relativePath}/{name}";
            items.Add((Normalize(childRelativePath, isDirectory: false), false));
        }
    }

    private static string Normalize(string relativePath, bool isDirectory)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/').Trim();
        return isDirectory && !normalized.EndsWith("/", StringComparison.Ordinal) ? normalized + "/" : normalized;
    }
}
