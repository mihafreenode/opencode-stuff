using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Platform.Windows;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class WorkspaceOperationResult
{
    public required WorkspaceSnapshot Snapshot { get; init; }
    public required string Message { get; init; }
    public required OperationTranscript Transcript { get; init; }
}

public sealed class WorkspaceBackupResult
{
    public required WorkspaceSnapshot Snapshot { get; init; }
    public required string Message { get; init; }
    public required OperationTranscript Transcript { get; init; }
    public required WorkspaceBackupExportResult Export { get; init; }
}

public sealed class WorkspacePublishResult
{
    public required WorkspaceSnapshot Snapshot { get; init; }
    public required string Message { get; init; }
    public required OperationTranscript Transcript { get; init; }
    public required WorkspacePublishReview Review { get; init; }
}

public sealed class WorkspaceRemovalPrompt
{
    public required string WorkspaceName { get; init; }
    public required string WorkspaceRoot { get; init; }
}

public sealed class WorkspaceRemovalOperationResult
{
    public required string Message { get; init; }
    public required OperationTranscript Transcript { get; init; }
    public required WorkspaceRemovalResult Removal { get; init; }
}

public sealed class WindowsTerminalProfileOperationResult
{
    public required string Message { get; init; }
    public required WindowsTerminalProfileSetupResult Setup { get; init; }
}

public sealed class WorkspaceRecoveryAssessment
{
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> Findings { get; init; }
    public required string ConfirmationMessage { get; init; }
}

public sealed class CreateWorkspaceDraft
{
    public required string WorkspaceName { get; init; }
    public required string WorkspaceRootPath { get; init; }
    public required TemplateManifest Template { get; init; }
}

public sealed class ExistingRepositoryImportDraft
{
    public required string RepositoryPath { get; init; }
    public required string WorkspaceName { get; init; }
    public required ExistingGitCheckoutBranchMode BranchMode { get; init; }
    public string NamedBranch { get; init; } = string.Empty;
    public bool ReuseExistingNamedBranch { get; init; }
}

public sealed class SavePointDraft
{
    public required string Message { get; init; }
}
