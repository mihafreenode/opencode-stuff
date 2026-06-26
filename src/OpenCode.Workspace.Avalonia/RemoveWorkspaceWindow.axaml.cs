using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia;

public partial class RemoveWorkspaceWindow : Window
{
    private readonly TextBlock _workspaceNameTextBlock;
    private readonly TextBlock _workspaceRootTextBlock;

    public RemoveWorkspaceWindow()
        : this(new WorkspaceRemovalPrompt { WorkspaceName = string.Empty, WorkspaceRoot = string.Empty })
    {
    }

    public RemoveWorkspaceWindow(WorkspaceRemovalPrompt prompt)
    {
        InitializeComponent();
        _workspaceNameTextBlock = this.FindControl<TextBlock>("WorkspaceNameTextBlock") ?? throw new InvalidOperationException("WorkspaceNameTextBlock was not found.");
        _workspaceRootTextBlock = this.FindControl<TextBlock>("WorkspaceRootTextBlock") ?? throw new InvalidOperationException("WorkspaceRootTextBlock was not found.");
        _workspaceNameTextBlock.Text = prompt.WorkspaceName;
        _workspaceRootTextBlock.Text = prompt.WorkspaceRoot;
        Result = new WorkspaceRemovalDecision { Choice = WorkspaceRemovalChoice.RegistrationOnly };
    }

    public WorkspaceRemovalDecision? Result { get; private set; }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void RegistrationOnlyChecked(object? sender, RoutedEventArgs e)
        => Result = new WorkspaceRemovalDecision { Choice = WorkspaceRemovalChoice.RegistrationOnly };

    private void DockerResourcesChecked(object? sender, RoutedEventArgs e)
        => Result = new WorkspaceRemovalDecision { Choice = WorkspaceRemovalChoice.DockerResources };

    private void DeleteFilesChecked(object? sender, RoutedEventArgs e)
        => Result = new WorkspaceRemovalDecision { Choice = WorkspaceRemovalChoice.DeleteFiles };

    private void CancelClicked(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }

    private void ConfirmClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
