using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia;

public partial class RecoveryConfirmationWindow : Window
{
    private readonly TextBlock _titleTextBlock;
    private readonly TextBlock _summaryTextBlock;
    private readonly TextBlock _confirmationTextBlock;
    private readonly ItemsControl _findingsItemsControl;
    public RecoveryConfirmationWindow()
        : this(new WorkspaceRecoveryAssessment
        {
            Title = "Recover Workspace",
            Summary = string.Empty,
            Findings = Array.Empty<string>(),
            ConfirmationMessage = string.Empty,
        })
    {
    }

    public RecoveryConfirmationWindow(WorkspaceRecoveryAssessment assessment)
    {
        InitializeComponent();
        _titleTextBlock = this.FindControl<TextBlock>("TitleTextBlock") ?? throw new InvalidOperationException("TitleTextBlock was not found.");
        _summaryTextBlock = this.FindControl<TextBlock>("SummaryTextBlock") ?? throw new InvalidOperationException("SummaryTextBlock was not found.");
        _confirmationTextBlock = this.FindControl<TextBlock>("ConfirmationTextBlock") ?? throw new InvalidOperationException("ConfirmationTextBlock was not found.");
        _findingsItemsControl = this.FindControl<ItemsControl>("FindingsItemsControl") ?? throw new InvalidOperationException("FindingsItemsControl was not found.");
        _titleTextBlock.Text = assessment.Title;
        _summaryTextBlock.Text = assessment.Summary;
        _confirmationTextBlock.Text = assessment.ConfirmationMessage;
        _findingsItemsControl.ItemsSource = assessment.Findings.Select(item => new TextBlock { Text = $"- {item}", TextWrapping = TextWrapping.Wrap });
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(false);
    private void ConfirmClicked(object? sender, RoutedEventArgs e) => Close(true);
}
