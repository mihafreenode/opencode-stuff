using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Avalonia.Services;

public interface IWorkspaceInteractionService
{
    Task<CreateWorkspaceDraft?> ShowCreateWorkspaceDialogAsync(IReadOnlyList<TemplateManifest> templates, CancellationToken cancellationToken = default);
    Task<ExistingRepositoryImportDraft?> ShowOpenExistingRepositoryDialogAsync(Func<string, string, CancellationToken, Task<ExistingGitCheckoutPlan>> inspectRepositoryAsync, Func<string, string, CancellationToken, Task<GitBranchValidationResult>> validateBranchAsync, CancellationToken cancellationToken = default);
    Task<string?> ShowBackupDestinationDialogAsync(string suggestedFileName, CancellationToken cancellationToken = default);
    Task<SavePointDraft?> ShowSavePointDialogAsync(string initialMessage, CancellationToken cancellationToken = default);
    Task<bool> ConfirmRecoveryAsync(WorkspaceRecoveryAssessment assessment, CancellationToken cancellationToken = default);
}
