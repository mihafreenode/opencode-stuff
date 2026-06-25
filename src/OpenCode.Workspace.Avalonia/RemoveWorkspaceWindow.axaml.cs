using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia;

public partial class RemoveWorkspaceWindow : Window
{
    public RemoveWorkspaceWindow()
        : this(new WorkspaceRemovalPrompt { WorkspaceName = string.Empty, WorkspaceRoot = string.Empty })
    {
    }

    public RemoveWorkspaceWindow(WorkspaceRemovalPrompt prompt)
    {
        InitializeComponent();
        (this.FindControl<TextBlock>("WorkspaceNameTextBlock") ?? throw new InvalidOperationException("WorkspaceNameTextBlock was not found.")).Text = prompt.WorkspaceName;
        (this.FindControl<TextBlock>("WorkspaceRootTextBlock") ?? throw new InvalidOperationException("WorkspaceRootTextBlock was not found.")).Text = prompt.WorkspaceRoot;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(false);

    private void ConfirmClicked(object? sender, RoutedEventArgs e) => Close(true);
}
