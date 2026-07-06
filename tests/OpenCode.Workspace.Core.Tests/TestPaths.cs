using System.Runtime.CompilerServices;

namespace OpenCode.Workspace.Core.Tests;

internal static class TestPaths
{
    private static readonly Lazy<string> RepositoryRootLazy = new(() => ResolveRepositoryRoot());
    private static readonly Lazy<string> CatalogRootLazy = new(CreateLocalCatalogMirror);

    public static string RepositoryRoot
        => RepositoryRootLazy.Value;

    public static string CatalogRoot
        => CatalogRootLazy.Value;

    private static string ResolveRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));
        if (File.Exists(Path.Combine(sourceRoot, "OpenCode.Workspace.slnx"))
            && File.Exists(Path.Combine(sourceRoot, "AGENTS.md")))
        {
            return sourceRoot;
        }

        foreach (var candidate in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(candidate);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "OpenCode.Workspace.slnx"))
                    && File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate repository root from test output path.");
    }

    private static string CreateLocalCatalogMirror()
    {
        var sourceRoot = Path.Combine(RepositoryRoot, "catalog");
        var targetRoot = Path.Combine(Path.GetTempPath(), "opencode-core-test-catalog", "catalog");

        if (Directory.Exists(targetRoot))
        {
            Directory.Delete(targetRoot, recursive: true);
        }

        foreach (var sourceDirectory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var directoryName = Path.GetFileName(sourceDirectory);
            var targetDirectory = Path.Combine(targetRoot, directoryName);
            Directory.CreateDirectory(targetDirectory);

            foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*.yaml", SearchOption.TopDirectoryOnly))
            {
                File.Copy(sourceFile, Path.Combine(targetDirectory, Path.GetFileName(sourceFile)), overwrite: true);
            }
        }

        return targetRoot;
    }
}
