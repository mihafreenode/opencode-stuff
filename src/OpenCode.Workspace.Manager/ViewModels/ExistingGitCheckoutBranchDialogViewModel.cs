using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Manager.ViewModels;

public sealed class ExistingGitCheckoutBranchDialogViewModel : ObservableObject
{
    private ExistingGitCheckoutBranchMode _branchMode = ExistingGitCheckoutBranchMode.CreateTemporaryWorkspaceBranch;
    private string _namedBranch = string.Empty;

    public ExistingGitCheckoutBranchDialogViewModel(ExistingGitCheckoutPlan plan)
    {
        Plan = plan;
    }

    public ExistingGitCheckoutPlan Plan { get; }

    public ExistingGitCheckoutBranchMode BranchMode
    {
        get => _branchMode;
        set
        {
            if (SetProperty(ref _branchMode, value))
            {
                RaisePropertyChanged(nameof(UseCurrentBranch));
                RaisePropertyChanged(nameof(CreateTemporaryWorkspaceBranch));
                RaisePropertyChanged(nameof(CreateNamedFeatureBranch));
                RaisePropertyChanged(nameof(ShowNamedBranchInput));
            }
        }
    }

    public string NamedBranch
    {
        get => _namedBranch;
        set => SetProperty(ref _namedBranch, value);
    }

    public bool UseCurrentBranch => BranchMode == ExistingGitCheckoutBranchMode.UseCurrentBranch;
    public bool CreateTemporaryWorkspaceBranch => BranchMode == ExistingGitCheckoutBranchMode.CreateTemporaryWorkspaceBranch;
    public bool CreateNamedFeatureBranch => BranchMode == ExistingGitCheckoutBranchMode.CreateNamedFeatureBranch;
    public bool ShowNamedBranchInput => CreateNamedFeatureBranch;
    public string RepositoryPathSummary => $"Repository path: {Plan.RepositoryPath}";
    public string CurrentBranchSummary => $"Current branch: {Plan.Repository.CurrentBranch}";
    public string DefaultBranchSummary => $"Default branch: {Plan.Repository.DefaultBranch}";
    public string RemoteOriginSummary => string.IsNullOrWhiteSpace(Plan.Repository.RemoteUrl) ? "Remote origin: not configured" : $"Remote origin: {Plan.Repository.RemoteUrl}";
    public string WorkspaceDefinitionSummary => Plan.HasWorkspaceConfiguration
        ? $"Existing workspace configuration found at '{Plan.DiscoveryResult.ConfigurationPath}'. OpenCode will keep using that file and refresh generated runtime files."
        : "No workspace configuration found yet. Continue and OpenCode will create workspace.yaml and runtime files in this repository.";
    public string DirtyStatusSummary => Plan.Repository.HasUncommittedChanges || Plan.Repository.UntrackedFileCount > 0
        ? "Uncommitted local changes present"
        : "Working tree is clean";
    public string AheadBehindSummary => string.IsNullOrWhiteSpace(Plan.Repository.TrackingBranch)
        ? "Ahead / behind: not tracking a remote branch yet"
        : $"Ahead / behind: ahead {Plan.Repository.AheadCount}, behind {Plan.Repository.BehindCount}";

    public ExistingGitCheckoutImportRequest BuildRequest()
        => new()
        {
            RepositoryPath = Plan.RepositoryPath,
            WorkspaceName = Plan.WorkspaceName,
            BranchMode = BranchMode,
            NamedBranch = NamedBranch.Trim(),
        };
}
