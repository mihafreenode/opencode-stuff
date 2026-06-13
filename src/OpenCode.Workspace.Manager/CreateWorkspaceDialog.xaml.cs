using System.Windows;
using Microsoft.Win32;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Manager.Services;
using OpenCode.Workspace.Manager.ViewModels;

namespace OpenCode.Workspace.Manager;

public partial class CreateWorkspaceDialog : Window
{
    private readonly PoLocalizationService _localization = new(System.IO.Path.Combine(AppContext.BaseDirectory, "Localization"), PoLocalizationService.DetectLanguageCode());

    public CreateWorkspaceDialog()
    {
        InitializeComponent();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void Create_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.SelectedWorkspaceSourceType == WorkspaceSourceType.ExistingGitCheckout)
        {
            await ImportExistingGitCheckoutAsync(viewModel);
            return;
        }

        var created = await viewModel.CreateWorkspaceFromDialogAsync();
        if (!created)
        {
            return;
        }

        DialogResult = true;
        Close();
    }

    private void NewWorkspaceSource_OnChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectedWorkspaceSourceType = WorkspaceSourceType.NewWorkspace;
        }
    }

    private void ExistingGitCheckoutSource_OnChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectedWorkspaceSourceType = WorkspaceSourceType.ExistingGitCheckout;
        }
    }

    private void BrowseExistingRepository_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
            viewModel.ExistingRepositoryPath = dialog.FolderName;
            if (string.IsNullOrWhiteSpace(viewModel.NewWorkspaceName))
            {
                viewModel.NewWorkspaceName = System.IO.Path.GetFileName(dialog.FolderName.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            }
        }
    }

    private async Task ImportExistingGitCheckoutAsync(MainWindowViewModel viewModel)
    {
        var plan = await viewModel.InspectExistingGitCheckoutFromDialogAsync();
        var branchDialog = new ExistingGitCheckoutBranchDialog(plan, _localization)
        {
            Owner = this,
        };

        if (branchDialog.ShowDialog() != true)
        {
            return;
        }

        var request = branchDialog.BuildRequest();
        request = new ExistingGitCheckoutImportRequest
        {
            RepositoryPath = request.RepositoryPath,
            WorkspaceName = request.WorkspaceName,
            BranchMode = request.BranchMode,
            NamedBranch = request.NamedBranch,
            ReuseExistingNamedBranch = request.ReuseExistingNamedBranch,
            InitialDefinition = viewModel.BuildWorkspaceDefinitionFromSelections(request.WorkspaceName),
        };
        if (request.BranchMode == ExistingGitCheckoutBranchMode.CreateNamedFeatureBranch)
        {
            var validation = await viewModel.ValidateExistingGitCheckoutBranchAsync(request.RepositoryPath, request.NamedBranch);
            if (!validation.IsValid)
            {
                AppDialogService.ShowOk(this, _localization, viewModel.GetText("dialog.invalidBranchName.title"), viewModel.GetText("dialog.invalidBranchName.message"));
                return;
            }

            if (validation.BranchExists)
            {
                var useExisting = AppDialogService.ShowYesNo(
                    this,
                    _localization,
                    viewModel.GetText("dialog.branchExists.title"),
                    viewModel.GetText("dialog.branchExists.message"));

                if (useExisting != AppDialogResult.Yes)
                {
                    return;
                }

                request = new ExistingGitCheckoutImportRequest
                {
                    RepositoryPath = request.RepositoryPath,
                    WorkspaceName = request.WorkspaceName,
                    BranchMode = request.BranchMode,
                    NamedBranch = request.NamedBranch,
                    ReuseExistingNamedBranch = true,
                    InitialDefinition = request.InitialDefinition,
                };
            }
        }

        if (plan.Repository.HasUncommittedChanges || plan.Repository.UntrackedFileCount > 0)
        {
            var changingBranches = request.BranchMode != ExistingGitCheckoutBranchMode.UseCurrentBranch;
            if (changingBranches)
            {
                var continueChoice = AppDialogService.ShowYesNo(
                    this,
                    _localization,
                    viewModel.GetText("dialog.uncommittedChanges.title"),
                    viewModel.GetText("dialog.uncommittedChanges.message"));

                if (continueChoice != AppDialogResult.Yes)
                {
                    return;
                }
            }
        }

        var imported = await viewModel.ImportExistingGitCheckoutFromDialogAsync(request);
        if (!imported)
        {
            return;
        }

        DialogResult = true;
        Close();
    }
}
