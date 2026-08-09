using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Api;

public interface IProviderSessionDiscovery
{
    Task<IReadOnlySet<string>> ListWorkspaceSessionIdsAsync(string containerName, CancellationToken cancellationToken);
}

internal sealed class OpenCodeProviderSessionDiscovery(IProcessRunner processes, OpenCodeSessionService sessions) : IProviderSessionDiscovery
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    public async Task<IReadOnlySet<string>> ListWorkspaceSessionIdsAsync(string containerName, CancellationToken cancellationToken)
    {
        var listed = await processes.RunAsync(
            "docker.exe",
            ["exec", "--user", "opencode", "-w", "/workspace", containerName, "env", "HOME=/home/opencode", "opencode", "session", "list"],
            cancellationToken: cancellationToken,
            timeout: CommandTimeout);
        if (listed.ExitCode != 0)
        {
            throw new InvalidOperationException($"OpenCode provider session discovery failed for container '{containerName}': {listed.StandardError}".Trim());
        }

        var matching = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in sessions.ParseSessionList(listed.StandardOutput))
        {
            var exported = await processes.RunAsync(
                "docker.exe",
                ["exec", "--user", "opencode", "-w", "/workspace", containerName, "env", "HOME=/home/opencode", "opencode", "export", item.Id],
                cancellationToken: cancellationToken,
                timeout: CommandTimeout);
            if (exported.ExitCode == 0 && string.Equals(sessions.TryGetSessionDirectory(exported.StandardOutput), "/workspace", StringComparison.Ordinal))
            {
                matching.Add(item.Id);
            }
        }
        return matching;
    }
}
