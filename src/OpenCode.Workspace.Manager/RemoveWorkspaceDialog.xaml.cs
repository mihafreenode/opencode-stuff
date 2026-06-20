using System.Windows;
using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.Manager.Services;

namespace OpenCode.Workspace.Manager;

public enum WorkspaceRemovalChoice
{
    RegistrationOnly,
    DockerResources,
    DeleteFiles,
}

public partial class RemoveWorkspaceDialog : Window
{
    private readonly PoLocalizationService _localization;

    public RemoveWorkspaceDialog(PoLocalizationService localization, string workspaceName, string workspacePath)
    {
        InitializeComponent();
        _localization = localization;
        WorkspaceName = workspaceName;
        WorkspacePath = workspacePath;
        SelectedChoice = WorkspaceRemovalChoice.RegistrationOnly;
        DataContext = this;
    }

    public string DialogTitle => _localization.Get("remove.dialog.title");
    public string DialogDescription => _localization.Get("remove.dialog.description");
    public string WorkspaceLabel => _localization.Get("remove.dialog.workspace");
    public string RegistrationOnlyTitle => _localization.Get("remove.option.registrationOnly.title");
    public string RegistrationOnlyFiles => _localization.Get("remove.option.registrationOnly.files");
    public string RegistrationOnlyDocker => _localization.Get("remove.option.registrationOnly.docker");
    public string DockerResourcesTitle => _localization.Get("remove.option.docker.title");
    public string DockerResourcesDescription => _localization.Get("remove.option.docker.description");
    public string DeleteFilesTitle => _localization.Get("remove.option.deleteFiles.title");
    public string DeleteFilesDescription => _localization.Get("remove.option.deleteFiles.description");
    public string CancelLabel => _localization.Get("actions.cancel");
    public string ConfirmLabel => _localization.Get("actions.remove");
    public string WorkspaceName { get; }
    public string WorkspacePath { get; }
    public WorkspaceRemovalChoice SelectedChoice { get; private set; }

    private void RegistrationOnly_OnChecked(object sender, RoutedEventArgs e) => SelectedChoice = WorkspaceRemovalChoice.RegistrationOnly;

    private void DockerResources_OnChecked(object sender, RoutedEventArgs e) => SelectedChoice = WorkspaceRemovalChoice.DockerResources;

    private void DeleteFiles_OnChecked(object sender, RoutedEventArgs e) => SelectedChoice = WorkspaceRemovalChoice.DeleteFiles;

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Confirm_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedChoice == WorkspaceRemovalChoice.DeleteFiles)
        {
            var confirmation = AppDialogService.ShowYesNo(
                this,
                _localization,
                _localization.Get("remove.confirmDeleteFiles.title"),
                string.Format(_localization.Get("remove.confirmDeleteFiles.message"), WorkspaceName, WorkspacePath));

            if (confirmation != AppDialogResult.Yes)
            {
                return;
            }
        }

        DialogResult = true;
        Close();
    }
}
