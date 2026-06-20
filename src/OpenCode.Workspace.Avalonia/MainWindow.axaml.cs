using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OpenCode.Workspace.Avalonia.ViewModels;

namespace OpenCode.Workspace.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WorkspaceOperationLogTextChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not WorkspacesPageViewModel viewModel || !viewModel.FollowLatestOutput)
        {
            return;
        }

        textBox.CaretIndex = textBox.Text?.Length ?? 0;
    }
}
