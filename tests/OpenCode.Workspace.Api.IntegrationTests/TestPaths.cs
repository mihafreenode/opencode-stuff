using System.Runtime.CompilerServices;

namespace OpenCode.Workspace.Api.IntegrationTests;

internal static class TestPaths
{
    public static string RepositoryRoot { get; } = ResolveRepositoryRoot();

    private static string ResolveRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));
        if (File.Exists(Path.Combine(sourceRoot, "OpenCode.Workspace.slnx")))
        {
            return sourceRoot;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
