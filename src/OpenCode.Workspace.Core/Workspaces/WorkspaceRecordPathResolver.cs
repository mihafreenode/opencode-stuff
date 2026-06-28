using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Workspaces;

public static class WorkspaceRecordPathResolver
{
    public static string GetWorkspaceRoot(WorkspaceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrWhiteSpace(record.RootPath))
        {
            return string.Empty;
        }

        var configurationPath = GetWorkspaceConfigurationPath(record);
        var workspaceRoot = Path.GetDirectoryName(configurationPath);
        return string.IsNullOrWhiteSpace(workspaceRoot) ? record.RootPath : workspaceRoot;
    }

    public static string GetWorkspaceConfigurationPath(WorkspaceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrWhiteSpace(record.RootPath))
        {
            return string.Empty;
        }

        var relativeConfigurationPath = string.IsNullOrWhiteSpace(record.ConfigurationPath)
            ? "workspace.yaml"
            : record.ConfigurationPath.Replace('/', Path.DirectorySeparatorChar);

        var directConfigurationPath = Path.Combine(record.RootPath, relativeConfigurationPath);
        if (File.Exists(directConfigurationPath))
        {
            return directConfigurationPath;
        }

        var childWorkspaceRoot = TryResolveLegacyNamedChildRoot(record);
        if (!string.IsNullOrWhiteSpace(childWorkspaceRoot))
        {
            return Path.Combine(childWorkspaceRoot, Path.GetFileName(relativeConfigurationPath));
        }

        return directConfigurationPath;
    }

    private static string? TryResolveLegacyNamedChildRoot(WorkspaceRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Name) || string.IsNullOrWhiteSpace(record.RootPath) || !Directory.Exists(record.RootPath))
        {
            return null;
        }

        var childRoot = Path.Combine(record.RootPath, record.Name);
        if (!Directory.Exists(childRoot))
        {
            return null;
        }

        foreach (var relativePath in WorkspaceDiscoveryService.SupportedConfigurationPaths)
        {
            var candidate = Path.Combine(childRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return childRoot;
            }
        }

        return null;
    }
}
