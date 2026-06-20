namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class RemoteTargetsPageViewModel : PageViewModel
{
    public RemoteTargetsPageViewModel()
        : base("Remote Targets", "Portable SSH-backed workspaces are planned but not implemented in this preview shell.")
    {
        DetailTitle = "Remote targets planned";
        DetailSummary = "This preview shell focuses on local workspace inspection first.";
        DetailItems.Add(new DetailItemViewModel("Status", "Planned"));
        DetailItems.Add(new DetailItemViewModel("Target type", "Portable SSH-backed workspaces"));
    }
}
