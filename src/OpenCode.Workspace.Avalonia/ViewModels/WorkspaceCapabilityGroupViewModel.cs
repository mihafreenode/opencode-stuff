namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspaceCapabilityGroupViewModel
{
    public WorkspaceCapabilityGroupViewModel(string title, string description, IReadOnlyList<AvailableWorkspaceServiceRowViewModel> services)
    {
        Title = title;
        Description = description;
        Services = services;
    }

    public string Title { get; }
    public string Description { get; }
    public IReadOnlyList<AvailableWorkspaceServiceRowViewModel> Services { get; }
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}
