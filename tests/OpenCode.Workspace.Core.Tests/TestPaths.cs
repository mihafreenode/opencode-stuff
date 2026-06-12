namespace OpenCode.Workspace.Core.Tests;

internal static class TestPaths
{
    public static string RepositoryRoot
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "OpenCode.Workspace.Manager.slnx")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root from test output path.");
        }
    }
}
