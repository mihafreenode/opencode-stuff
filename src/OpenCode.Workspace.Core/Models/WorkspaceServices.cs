namespace OpenCode.Workspace.Core.Models;

public sealed class WorkspaceServiceInfo
{
    public string ServiceId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string HostUrl { get; init; } = string.Empty;
    public string InternalUrl { get; init; } = string.Empty;
    public string Credentials { get; init; } = string.Empty;
    public string DocsPath { get; init; } = string.Empty;
    public IReadOnlyList<WorkspaceServiceCommandInfo> Commands { get; init; } = Array.Empty<WorkspaceServiceCommandInfo>();
    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
}

public sealed class WorkspaceServiceCommandInfo
{
    public string Label { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
