using System;
using System.Windows;
using Microsoft.Win32;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Manager.Services;
using OpenCode.Workspace.Manager.ViewModels;

namespace OpenCode.Workspace.Manager;

public partial class CreateWorkspaceDialog : Window
{
    private readonly PoLocalizationService _localization = new(System.IO.Path.Combine(AppContext.BaseDirectory, "Localization"), PoLocalizationService.DetectLanguageCode());
    public Action<string>? DiagnosticsLogger { get; init; }

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

        var buttonSource = sender is FrameworkElement { Name: "TemplateCardCreateButton" }
            ? "TemplateCardCreateButton"
            : "HeaderCreateButton";

        DiagnosticsLogger?.Invoke($"Create Workspace command entered via {buttonSource}.");

        if (viewModel.SelectedWorkspaceSourceType == WorkspaceSourceType.ExistingGitCheckout)
        {
            await ImportExistingGitCheckoutAsync(viewModel);
            return;
        }

        var created = await viewModel.CreateWorkspaceFromDialogAsync(buttonSource, DiagnosticsLogger);
        if (!created)
        {
            DiagnosticsLogger?.Invoke($"Create Workspace command returned without completion via {buttonSource}.");
            return;
        }

        DiagnosticsLogger?.Invoke($"Create Workspace dialog close requested via {buttonSource}.");
        DialogResult = true;
        Close();
        DiagnosticsLogger?.Invoke($"Create Workspace command completed via {buttonSource}.");
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

    private async void BrowseExistingRepository_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
            var selectedPath = dialog.FolderName;
            viewModel.ExistingRepositoryPath = selectedPath;
            if (string.IsNullOrWhiteSpace(viewModel.NewWorkspaceName))
            {
                viewModel.NewWorkspaceName = System.IO.Path.GetFileName(selectedPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            }

            await TryLoadRepositoryConfigurationAsync(viewModel, selectedPath);
        }
    }

    private async Task ImportExistingGitCheckoutAsync(MainWindowViewModel viewModel)
    {
        var plan = await viewModel.InspectExistingGitCheckoutFromDialogAsync();
        if (plan.DiscoveryResult.Status == WorkspaceDiscoveryStatus.Invalid)
        {
            await ShowInvalidRepositoryConfigurationAsync(plan);
            return;
        }

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

    private async Task TryLoadRepositoryConfigurationAsync(MainWindowViewModel viewModel, string repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            return;
        }

        try
        {
            var plan = await viewModel.LoadExistingRepositoryConfigurationAsync(repositoryPath);
            if (plan.DiscoveryResult.Status == WorkspaceDiscoveryStatus.Invalid)
            {
                await ShowInvalidRepositoryConfigurationAsync(plan);
            }
        }
        catch
        {
        }
    }

    private async Task ShowInvalidRepositoryConfigurationAsync(ExistingGitCheckoutPlan plan)
    {
        var configurationPath = plan.DiscoveryResult.ConfigurationPath ?? "workspace configuration";
        var errorMessage = plan.DiscoveryResult.ErrorMessage ?? "The configuration could not be loaded.";
        var message = $"Invalid workspace configuration found.{Environment.NewLine}{Environment.NewLine}Path:{Environment.NewLine}{configurationPath}{Environment.NewLine}{Environment.NewLine}The repository already contains workspace settings, but the configuration could not be loaded.{Environment.NewLine}{Environment.NewLine}{errorMessage}{Environment.NewLine}{Environment.NewLine}Please fix the configuration file and try again.{Environment.NewLine}{Environment.NewLine}The application will not replace this file automatically.";
        var choice = AppDialogService.ShowOpenFileCancel(this, _localization, "Invalid workspace configuration found.", message);
        if (choice != AppDialogResult.OpenFile)
        {
            return;
        }

        var filePath = System.IO.Path.Combine(plan.RepositoryPath, configurationPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true,
            });
        }
        catch
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true,
                });
            }
            catch
            {
            }
        }
    }
}
