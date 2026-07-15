using System.Runtime.CompilerServices;

namespace OpenCode.Workspace.Mcp.Tests;

internal static class TestPaths
{
    private static readonly Lazy<string> RepositoryRootLazy = new(() => ResolveRepositoryRoot());

    public static string RepositoryRoot => RepositoryRootLazy.Value;

    private static string ResolveRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));
        if (File.Exists(Path.Combine(sourceRoot, "OpenCode.Workspace.slnx")) && File.Exists(Path.Combine(sourceRoot, "AGENTS.md")))
        {
            return sourceRoot;
        }

        throw new InvalidOperationException("Could not locate repository root from test output path.");
    }
}
