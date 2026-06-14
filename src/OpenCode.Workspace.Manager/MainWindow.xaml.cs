using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using OpenCode.Workspace.Manager.Services;
using OpenCode.Workspace.Manager.ViewModels;

namespace OpenCode.Workspace.Manager;

public partial class MainWindow : Window
{
    private ScrollViewer? _logScrollViewer;
    private bool _isLogAutoFollowEnabled = true;
    private bool _isAdjustingLogScroll;
    private double _pausedLogVerticalOffset;
    private bool _hasPromptedForQuickTutorial;
    private StartupDiagnosticsService? _diagnostics;

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
            _pausedLogVerticalOffset = _logScrollViewer.VerticalOffset;
        }

        UpdateLogAutoScrollIndicator();
        ScrollLogToBottom();
    }

    private void LogTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLogAutoFollowEnabled)
        {
            Dispatcher.BeginInvoke(ScrollLogToBottom);
            return;
        }

        Dispatcher.BeginInvoke(RestorePausedLogPosition);
    }

    private void LogScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_logScrollViewer is null || _isAdjustingLogScroll)
        {
            return;
        }

        _pausedLogVerticalOffset = _logScrollViewer.VerticalOffset;
        var distanceFromBottom = _logScrollViewer.ExtentHeight - (_logScrollViewer.VerticalOffset + _logScrollViewer.ViewportHeight);
        _isLogAutoFollowEnabled = distanceFromBottom <= 24;
        UpdateLogAutoScrollIndicator();
    }

    private void ScrollLogToBottom()
    {
        if (_logScrollViewer is null)
        {
            return;
        }

        try
        {
            _isAdjustingLogScroll = true;
            _logScrollViewer.ScrollToEnd();
            _pausedLogVerticalOffset = _logScrollViewer.VerticalOffset;
            _isLogAutoFollowEnabled = true;
        }
        finally
        {
            _isAdjustingLogScroll = false;
            UpdateLogAutoScrollIndicator();
        }
    }

    private void RestorePausedLogPosition()
    {
        if (_logScrollViewer is null || _isLogAutoFollowEnabled)
        {
            return;
        }

        try
        {
            _isAdjustingLogScroll = true;
            _logScrollViewer.ScrollToVerticalOffset(_pausedLogVerticalOffset);
        }
        finally
        {
            _isAdjustingLogScroll = false;
            UpdateLogAutoScrollIndicator();
        }
    }

    private void UpdateLogAutoScrollIndicator()
    {
        if (LogAutoScrollPausedIndicator is null)
        {
            return;
        }

        LogAutoScrollPausedIndicator.Visibility = _isLogAutoFollowEnabled
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void OpenCreateWorkspaceDialog_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        _diagnostics?.Log($"Create Workspace clicked. CanStartCreateWorkspaceFlow={viewModel.CanStartCreateWorkspaceFlow} IsBusy={viewModel.IsBusyForDiagnostics} HasRunningWorkspace={viewModel.HasRunningWorkspace}.");

        if (!viewModel.CanStartCreateWorkspaceFlow)
        {
            _diagnostics?.Log("Create Workspace click ignored because flow cannot start.");
            return;
        }

        viewModel.PrepareCreateWorkspaceDialog();

        var dialog = new CreateWorkspaceDialog
        {
            Owner = this,
            DataContext = viewModel,
            DiagnosticsLogger = message =>
            {
                var formatted = $"[create][{DateTimeOffset.Now:O}][thread {Environment.CurrentManagedThreadId}] {message}";
                _diagnostics?.Log(formatted);
                viewModel.AppendCreateDialogDiagnostic(formatted);
            },
        };

        dialog.ShowDialog();
        _diagnostics?.Log("Create Workspace dialog closed.");
    }

    public void BeginPromptForQuickTutorialIfNeeded(StartupDiagnosticsService diagnostics)
    {
        _diagnostics = diagnostics;
        Dispatcher.BeginInvoke(() => PromptForQuickTutorialIfNeeded(), DispatcherPriority.ApplicationIdle);
    }

    private void PromptForQuickTutorialIfNeeded()
    {
        if (_hasPromptedForQuickTutorial || DataContext is not MainWindowViewModel viewModel || !viewModel.ShouldPromptForQuickTutorial())
        {
            _diagnostics?.Log("Quick tutorial prompt skipped.");
            return;
        }

        _hasPromptedForQuickTutorial = true;
        _diagnostics?.Log("Showing quick tutorial prompt.");
        var prompt = new QuickTutorialPromptDialog
        {
            Owner = this,
            ShowInTaskbar = false,
        };

        var startTutorial = prompt.ShowDialog() == true;
        _diagnostics?.Log($"Quick tutorial prompt result: startTutorial={startTutorial}.");
        viewModel.MarkQuickTutorialPromptHandled();
        if (startTutorial)
        {
            ShowQuickTutorial(viewModel);
        }
    }

    private void OpenQuickTutorial_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            ShowQuickTutorial(viewModel);
        }
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

    private void ShowQuickTutorial(MainWindowViewModel viewModel)
    {
        _diagnostics?.Log("Opening quick tutorial window.");
        var tutorialWindow = new QuickTutorialWindow
        {
            Owner = this,
            DataContext = viewModel.CreateQuickTutorialViewModel(),
            ShowInTaskbar = false,
        };

        tutorialWindow.ShowDialog();
        _diagnostics?.Log("Quick tutorial window closed.");
    }
}
