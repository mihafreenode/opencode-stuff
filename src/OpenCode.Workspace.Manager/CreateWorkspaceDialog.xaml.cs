using System.Windows;
using OpenCode.Workspace.Manager.ViewModels;

namespace OpenCode.Workspace.Manager;

public partial class CreateWorkspaceDialog : Window
{
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

        var created = await viewModel.CreateWorkspaceFromDialogAsync();
        if (!created)
        {
            return;
        }

        DialogResult = true;
        Close();
    }
}
