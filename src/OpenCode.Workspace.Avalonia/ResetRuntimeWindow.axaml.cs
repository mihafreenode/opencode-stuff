using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia;

public partial class ResetRuntimeWindow : Window
{
    public ResetRuntimeWindow()
        : this(new WorkspaceRuntimeResetPrompt
        {
            WorkspaceName = string.Empty,
            WorkspaceRoot = string.Empty,
            Summary = string.Empty,
        })
    {
    }

    public ResetRuntimeWindow(WorkspaceRuntimeResetPrompt prompt)
    {
        InitializeComponent();
        AppWindowIcons.Apply(this);
        (this.FindControl<TextBlock>("WorkspaceNameTextBlock") ?? throw new InvalidOperationException("WorkspaceNameTextBlock was not found.")).Text = prompt.WorkspaceName;
        (this.FindControl<TextBlock>("WorkspaceRootTextBlock") ?? throw new InvalidOperationException("WorkspaceRootTextBlock was not found.")).Text = prompt.WorkspaceRoot;
        (this.FindControl<TextBlock>("SummaryTextBlock") ?? throw new InvalidOperationException("SummaryTextBlock was not found.")).Text = prompt.Summary;
        (this.FindControl<TextBlock>("ConfirmationTextBlock") ?? throw new InvalidOperationException("ConfirmationTextBlock was not found.")).Text = prompt.ConfirmationMessage;
        (this.FindControl<ItemsControl>("RemovesItemsControl") ?? throw new InvalidOperationException("RemovesItemsControl was not found.")).ItemsSource = BuildItems(prompt.Removes);
        (this.FindControl<ItemsControl>("KeepsItemsControl") ?? throw new InvalidOperationException("KeepsItemsControl was not found.")).ItemsSource = BuildItems(prompt.Keeps);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private static IReadOnlyList<TextBlock> BuildItems(IReadOnlyList<string> values)
        => values.Select(item => new TextBlock { Text = $"- {item}", TextWrapping = TextWrapping.Wrap }).ToList();

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(false);

    private void ConfirmClicked(object? sender, RoutedEventArgs e) => Close(true);
}
