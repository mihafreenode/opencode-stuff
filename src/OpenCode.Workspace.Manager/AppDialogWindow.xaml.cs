using System.Windows;
using OpenCode.Workspace.Manager.Services;

namespace OpenCode.Workspace.Manager;

public partial class AppDialogWindow : Window
{
    public AppDialogWindow()
    {
        InitializeComponent();
    }

    public AppDialogResult Result { get; private set; } = AppDialogResult.None;

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Ok;
        DialogResult = true;
        Close();
    }

    private void Yes_OnClick(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Yes;
        DialogResult = true;
        Close();
    }

    private void No_OnClick(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.No;
        DialogResult = false;
        Close();
    }
}
