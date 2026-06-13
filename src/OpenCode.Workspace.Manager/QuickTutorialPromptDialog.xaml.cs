using System.Windows;

namespace OpenCode.Workspace.Manager;

public partial class QuickTutorialPromptDialog : Window
{
    public QuickTutorialPromptDialog()
    {
        InitializeComponent();
    }

    private void StartTutorial_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Skip_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
