using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenCode.Workspace.Manager.ViewModels;

namespace OpenCode.Workspace.Manager;

public partial class MainWindow : Window
{
    private ScrollViewer? _logScrollViewer;
    private bool _isLogAutoFollowEnabled = true;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void LogTextBox_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        _logScrollViewer ??= FindVisualChild<ScrollViewer>(textBox);
        if (_logScrollViewer is not null)
        {
            _logScrollViewer.ScrollChanged -= LogScrollViewer_OnScrollChanged;
            _logScrollViewer.ScrollChanged += LogScrollViewer_OnScrollChanged;
        }

        ScrollLogToBottom();
    }

    private void LogTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLogAutoFollowEnabled)
        {
            Dispatcher.BeginInvoke(ScrollLogToBottom);
        }
    }

    private void LogScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_logScrollViewer is null)
        {
            return;
        }

        var distanceFromBottom = _logScrollViewer.ExtentHeight - (_logScrollViewer.VerticalOffset + _logScrollViewer.ViewportHeight);
        _isLogAutoFollowEnabled = distanceFromBottom <= 24;
    }

    private void ScrollLogToBottom()
    {
        _logScrollViewer?.ScrollToEnd();
    }

    private void OpenCreateWorkspaceDialog_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (!viewModel.CanStartCreateWorkspaceFlow)
        {
            return;
        }

        var dialog = new CreateWorkspaceDialog
        {
            Owner = this,
            DataContext = viewModel,
        };

        dialog.ShowDialog();
    }

    private async void WorkspaceListBox_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedWorkspace is null)
        {
            return;
        }

        await viewModel.PrimaryWorkspaceActionCommand.ExecuteAsync();
    }

    private async void WorkspaceListBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainWindowViewModel viewModel || viewModel.SelectedWorkspace is null)
        {
            return;
        }

        e.Handled = true;
        await viewModel.PrimaryWorkspaceActionCommand.ExecuteAsync();
    }

    private void WorkspaceListItem_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void WorkspaceListItem_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not ListBoxItem item || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        item.IsSelected = true;
        item.ContextMenu = BuildWorkspaceContextMenu(viewModel);
    }

    private static ContextMenu BuildWorkspaceContextMenu(MainWindowViewModel viewModel)
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = viewModel.OpenWorkspaceLabel, Command = viewModel.PrimaryWorkspaceActionCommand });
        menu.Items.Add(new MenuItem { Header = viewModel.CreateSavePointLabel, Command = viewModel.CreateSavePointCommand });
        menu.Items.Add(new MenuItem { Header = viewModel.PublishLabel, Command = viewModel.PublishWorkspaceCommand });
        menu.Items.Add(new MenuItem { Header = viewModel.OpenFolderLabel, Command = viewModel.OpenFolderCommand });
        menu.Items.Add(new MenuItem { Header = viewModel.CopyPathLabel, Command = viewModel.CopyWorkspacePathCommand });
        menu.Items.Add(new MenuItem { Header = viewModel.OpenAdvancedGitViewLabel, Command = viewModel.OpenAdvancedGitViewCommand });
        menu.Items.Add(new MenuItem { Header = viewModel.ShutDownLabel, Command = viewModel.ShutDownWorkspaceCommand });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = viewModel.RemoveLabel, Command = viewModel.RemoveWorkspaceCommand });
        return menu;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var nestedChild = FindVisualChild<T>(child);
            if (nestedChild is not null)
            {
                return nestedChild;
            }
        }

        return null;
    }
}
