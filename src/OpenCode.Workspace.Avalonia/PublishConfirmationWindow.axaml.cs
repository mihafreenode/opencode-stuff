using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OpenCode.Workspace.AppSupport;

namespace OpenCode.Workspace.Avalonia;

public partial class PublishConfirmationWindow : Window
{
    private readonly TextBlock _titleTextBlock;
    private readonly TextBlock _summaryTextBlock;
    private readonly TextBlock _confirmationTextBlock;
    private readonly ItemsControl _findingsItemsControl;
    private readonly ItemsControl _warningsItemsControl;
    private readonly Border _warningsBorder;

    public PublishConfirmationWindow()
        : this(new WorkspacePublishAssessment
        {
            WorkspaceName = string.Empty,
            CurrentBranch = string.Empty,
            Summary = string.Empty,
            ConfirmationMessage = string.Empty,
            Findings = Array.Empty<string>(),
            Warnings = Array.Empty<string>(),
            CanPublish = false,
            IsBlocked = true,
            RequiresConfirmation = false,
            RequiresSavePoint = false,
            HasRemoteConfigured = false,
            RemoteName = string.Empty,
            RemoteBranch = string.Empty,
            AheadCount = 0,
            BehindCount = 0,
        })
    {
    }

    public PublishConfirmationWindow(WorkspacePublishAssessment assessment)
    {
        InitializeComponent();
        _titleTextBlock = this.FindControl<TextBlock>("TitleTextBlock") ?? throw new InvalidOperationException("TitleTextBlock was not found.");
        _summaryTextBlock = this.FindControl<TextBlock>("SummaryTextBlock") ?? throw new InvalidOperationException("SummaryTextBlock was not found.");
        _confirmationTextBlock = this.FindControl<TextBlock>("ConfirmationTextBlock") ?? throw new InvalidOperationException("ConfirmationTextBlock was not found.");
        _findingsItemsControl = this.FindControl<ItemsControl>("FindingsItemsControl") ?? throw new InvalidOperationException("FindingsItemsControl was not found.");
        _warningsItemsControl = this.FindControl<ItemsControl>("WarningsItemsControl") ?? throw new InvalidOperationException("WarningsItemsControl was not found.");
        _warningsBorder = this.FindControl<Border>("WarningsBorder") ?? throw new InvalidOperationException("WarningsBorder was not found.");

        _titleTextBlock.Text = $"Publish {assessment.WorkspaceName}";
        _summaryTextBlock.Text = assessment.Summary;
        _confirmationTextBlock.Text = assessment.ConfirmationMessage;
        _findingsItemsControl.ItemsSource = assessment.Findings.Select(item => new TextBlock { Text = $"- {item}", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap });
        _warningsItemsControl.ItemsSource = assessment.Warnings.Select(item => new TextBlock { Text = $"- {item}", TextWrapping = global::Avalonia.Media.TextWrapping.Wrap });
        _warningsBorder.IsVisible = assessment.Warnings.Count > 0;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(false);

    private void ConfirmClicked(object? sender, RoutedEventArgs e) => Close(true);
}
