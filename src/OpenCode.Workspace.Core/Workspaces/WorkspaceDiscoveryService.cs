using YamlDotNet.Core;

namespace OpenCode.Workspace.Core.Workspaces;

public enum WorkspaceDiscoveryStatus
{
    NotFound,
    Found,
    Invalid,
}

public sealed class WorkspaceDiscoveryResult
{
    public WorkspaceDiscoveryStatus Status { get; init; }
    public string? ConfigurationPath { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class WorkspaceDiscoveryService
{
    public static readonly IReadOnlyList<string> SupportedConfigurationPaths =
    [
        "workspace.yaml",
        "workspace.yml",
        ".opencode/profile.yaml",
        ".opencode/profile.yml",
    ];

    public WorkspaceDiscoveryResult Discover(string repositoryRoot)
    {
        foreach (var relativePath in SupportedConfigurationPaths)
        {
            var fullPath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            try
            {
                using var reader = File.OpenText(fullPath);
                var parser = new Parser(reader);
                while (parser.MoveNext())
                {
                }

                return new WorkspaceDiscoveryResult
                {
                    Status = WorkspaceDiscoveryStatus.Found,
                    ConfigurationPath = relativePath,
                };
            }
            catch (Exception exception) when (exception is YamlException || exception is IOException || exception is UnauthorizedAccessException)
            {
                return new WorkspaceDiscoveryResult
                {
                    Status = WorkspaceDiscoveryStatus.Invalid,
                    ConfigurationPath = relativePath,
                    ErrorMessage = exception.Message,
                };
            }
        }

        return new WorkspaceDiscoveryResult
        {
            Status = WorkspaceDiscoveryStatus.NotFound,
        };
    }
}
