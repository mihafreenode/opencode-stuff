using System.Windows;
using OpenCode.Workspace.Manager.Services;
using OpenCode.Workspace.Manager.ViewModels;

namespace OpenCode.Workspace.Manager;

public partial class SavePointDialog : Window
{
    public SavePointDialog(PoLocalizationService localization, string initialMessage)
    {
        InitializeComponent();
        ViewModel = new SavePointDialogViewModel(localization, initialMessage);
        DataContext = ViewModel;
    }

    public SavePointDialogViewModel ViewModel { get; }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Confirm_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.SavePointMessage))
        {
            return;
        }

        DialogResult = true;
        Close();
    }
}
