using System.Windows;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Manager.Services;
using OpenCode.Workspace.Manager.ViewModels;

namespace OpenCode.Workspace.Manager;

public partial class ExistingGitCheckoutBranchDialog : Window
{
    private readonly ExistingGitCheckoutPlan _plan;
    private readonly PoLocalizationService _localization;

    public ExistingGitCheckoutBranchDialog(ExistingGitCheckoutPlan plan, PoLocalizationService localization)
    {
        InitializeComponent();
        _plan = plan;
        _localization = localization;
        DataContext = new ExistingGitCheckoutBranchDialogViewModel(plan);
    }

    public ExistingGitCheckoutImportRequest BuildRequest()
        => ((ExistingGitCheckoutBranchDialogViewModel)DataContext).BuildRequest();

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void UseCurrentBranch_OnChecked(object sender, RoutedEventArgs e)
        => ((ExistingGitCheckoutBranchDialogViewModel)DataContext).BranchMode = ExistingGitCheckoutBranchMode.UseCurrentBranch;

    private void CreateTemporaryWorkspaceBranch_OnChecked(object sender, RoutedEventArgs e)
        => ((ExistingGitCheckoutBranchDialogViewModel)DataContext).BranchMode = ExistingGitCheckoutBranchMode.CreateTemporaryWorkspaceBranch;

    private void CreateNamedFeatureBranch_OnChecked(object sender, RoutedEventArgs e)
        => ((ExistingGitCheckoutBranchDialogViewModel)DataContext).BranchMode = ExistingGitCheckoutBranchMode.CreateNamedFeatureBranch;

    private void Continue_OnClick(object sender, RoutedEventArgs e)
    {
        var viewModel = (ExistingGitCheckoutBranchDialogViewModel)DataContext;
        if (viewModel.BranchMode == ExistingGitCheckoutBranchMode.CreateNamedFeatureBranch && string.IsNullOrWhiteSpace(viewModel.NamedBranch))
        {
            AppDialogService.ShowOk(this, _localization, _localization.Get("dialog.branchNameRequired.title"), _localization.Get("dialog.branchNameRequired.message"));
            return;
        }

        DialogResult = true;
        Close();
    }
}
