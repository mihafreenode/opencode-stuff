using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia;

public partial class CheckpointWindow : Window
{
    private readonly TextBlock _titleTextBlock;
    private readonly TextBlock _summaryTextBlock;
    private readonly TextBlock _confirmationTextBlock;

    public CheckpointWindow()
        : this(new WorkspaceCheckpointPrompt
        {
            WorkspaceName = string.Empty,
            WorkspaceRoot = string.Empty,
            Summary = string.Empty,
            ConfirmationMessage = string.Empty,
        })
    {
    }

    public CheckpointWindow(WorkspaceCheckpointPrompt prompt)
    {
        InitializeComponent();
        AppWindowIcons.Apply(this);
        _titleTextBlock = this.FindControl<TextBlock>("TitleTextBlock") ?? throw new InvalidOperationException("TitleTextBlock was not found.");
        _summaryTextBlock = this.FindControl<TextBlock>("SummaryTextBlock") ?? throw new InvalidOperationException("SummaryTextBlock was not found.");
        _confirmationTextBlock = this.FindControl<TextBlock>("ConfirmationTextBlock") ?? throw new InvalidOperationException("ConfirmationTextBlock was not found.");
        _titleTextBlock.Text = $"Create checkpoint for {prompt.WorkspaceName}";
        _summaryTextBlock.Text = prompt.Summary;
        _confirmationTextBlock.Text = prompt.ConfirmationMessage;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(false);

    private void ConfirmClicked(object? sender, RoutedEventArgs e) => Close(true);
}
