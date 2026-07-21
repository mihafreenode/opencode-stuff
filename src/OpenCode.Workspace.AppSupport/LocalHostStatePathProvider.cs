namespace OpenCode.Workspace.AppSupport;

public interface ILocalHostStatePathProvider
{
    string StateRoot { get; }
    string LocalHostRoot { get; }
    string DescriptorPath { get; }
    string LockPath { get; }
    string WorkspaceInstancesRoot { get; }
    string ControllerSessionsRoot { get; }
    string InteractiveSessionsRoot { get; }
    string OperationsRoot { get; }
}

public sealed class LocalHostStateOptions
{
    public string StateRoot { get; init; } = string.Empty;
}

public sealed class DefaultLocalHostStatePathProvider : ILocalHostStatePathProvider
{
    public DefaultLocalHostStatePathProvider(LocalHostStateOptions? options = null)
    {
        var configuredRoot = options?.StateRoot;
        StateRoot = string.IsNullOrWhiteSpace(configuredRoot)
            ? WorkspaceAppDataPaths.GetWorkspaceManagerDataRoot()
            : Path.GetFullPath(configuredRoot);
    }

    public string StateRoot { get; }
    public string LocalHostRoot => Path.Combine(StateRoot, "local-host");
    public string DescriptorPath => Path.Combine(LocalHostRoot, "host.json");
    public string LockPath => Path.Combine(LocalHostRoot, "host.lock");
    public string WorkspaceInstancesRoot => Path.Combine(StateRoot, "workspace-instances");
    public string ControllerSessionsRoot => Path.Combine(StateRoot, "controller-sessions");
    public string InteractiveSessionsRoot => Path.Combine(StateRoot, "interactive-agent-sessions");
    public string OperationsRoot => Path.Combine(StateRoot, "operations");
}
