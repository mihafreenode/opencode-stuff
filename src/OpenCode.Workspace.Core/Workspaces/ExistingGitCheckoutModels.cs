namespace OpenCode.Workspace.Core.Workspaces;

public enum ExistingGitCheckoutBranchMode
{
    UseCurrentBranch,
    CreateTemporaryWorkspaceBranch,
    CreateNamedFeatureBranch,
}

public sealed class ExistingGitCheckoutPlan
{
    public required string RepositoryPath { get; init; }
    public required string WorkspaceName { get; init; }
    public required GitRepositoryInspection Repository { get; init; }
}

public sealed class ExistingGitCheckoutImportRequest
{
    public required string RepositoryPath { get; init; }
    public required string WorkspaceName { get; init; }
    public required ExistingGitCheckoutBranchMode BranchMode { get; init; }
    public string NamedBranch { get; init; } = string.Empty;
    public bool ReuseExistingNamedBranch { get; init; }
}
