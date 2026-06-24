using Avalonia.Controls;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class AvaloniaWorkspaceInteractionService : IWorkspaceInteractionService
{
    private readonly Window _owner;

    public AvaloniaWorkspaceInteractionService(Window owner)
    {
        _owner = owner;
    }

    public Task<CreateWorkspaceDraft?> ShowCreateWorkspaceDialogAsync(IReadOnlyList<TemplateManifest> templates, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new CreateWorkspaceWindow(templates).ShowDialog<CreateWorkspaceDraft?>(_owner);
    }

    public Task<ExistingRepositoryImportDraft?> ShowOpenExistingRepositoryDialogAsync(Func<string, string, CancellationToken, Task<ExistingGitCheckoutPlan>> inspectRepositoryAsync, Func<string, string, CancellationToken, Task<GitBranchValidationResult>> validateBranchAsync, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new OpenExistingRepositoryWindow(inspectRepositoryAsync, validateBranchAsync).ShowDialog<ExistingRepositoryImportDraft?>(_owner);
    }

    public Task<SavePointDraft?> ShowSavePointDialogAsync(string initialMessage, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new SavePointWindow(initialMessage).ShowDialog<SavePointDraft?>(_owner);
    }

    public Task<bool> ConfirmRecoveryAsync(WorkspaceRecoveryAssessment assessment, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new RecoveryConfirmationWindow(assessment).ShowDialog<bool>(_owner);
    }
}
