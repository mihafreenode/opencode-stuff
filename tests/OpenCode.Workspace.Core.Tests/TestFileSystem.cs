namespace OpenCode.Workspace.Core.Tests;

internal static class TestFileSystem
{
    public static void DeleteDirectoryIfExists(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Exception? lastException = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                ClearAttributesRecursively(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastException = exception;
                Thread.Sleep(TimeSpan.FromMilliseconds(50 * attempt));
            }
        }

        throw new InvalidOperationException($"Failed to delete temporary test directory '{path}' after multiple attempts.", lastException);
    }

    private static void ClearAttributesRecursively(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(rootPath, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(entry, FileAttributes.Normal);
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
        }

        File.SetAttributes(rootPath, FileAttributes.Normal);
    }
}
