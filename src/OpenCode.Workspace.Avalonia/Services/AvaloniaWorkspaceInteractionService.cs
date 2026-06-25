using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OpenCode.Workspace.AppSupport;
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

    public async Task<string?> ShowBackupDestinationDialogAsync(string suggestedFileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_owner.StorageProvider is null)
        {
            return null;
        }

        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Backup workspace",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "zip",
            FileTypeChoices =
            [
                new FilePickerFileType("Zip archive")
                {
                    Patterns = ["*.zip"],
                    MimeTypes = ["application/zip"],
                },
            ],
        });

        return file?.TryGetLocalPath();
    }

    public async Task<bool> ConfirmRemoveWorkspaceAsync(WorkspaceRemovalPrompt prompt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = new RemoveWorkspaceWindow(prompt);
        return await window.ShowDialog<bool>(_owner);
    }

    public async Task<bool> ConfirmPublishAsync(WorkspacePublishAssessment assessment, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = new PublishConfirmationWindow(assessment);
        return await window.ShowDialog<bool>(_owner);
    }

    public async Task<SavePointDraft?> ShowSavePointDialogAsync(string initialMessage, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartupLog.WriteGlobal("Opening Save Point dialog.");
        var window = new SavePointWindow(initialMessage);
        await window.ShowDialog(_owner);
        StartupLog.WriteGlobal($"Save Point dialog closed. Confirmed: {window.Result is not null}.");
        return window.Result;
    }

    public Task<bool> ConfirmRecoveryAsync(WorkspaceRecoveryAssessment assessment, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new RecoveryConfirmationWindow(assessment).ShowDialog<bool>(_owner);
    }
}
