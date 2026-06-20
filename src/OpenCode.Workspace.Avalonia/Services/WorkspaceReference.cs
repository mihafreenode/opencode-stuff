namespace OpenCode.Workspace.Avalonia.Services;

public sealed record WorkspaceReference(string Name, string RootPath)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? RootPath : Name;
}
