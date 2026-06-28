using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia;

public partial class RecoveryConfirmationWindow : Window
{
    private readonly TextBlock _workspaceNameTextBlock;
    private readonly TextBlock _statusTextBlock;
    private readonly TextBlock _summaryTextBlock;
    private readonly TextBlock _confirmationTextBlock;
    private readonly ItemsControl _recoverActionsItemsControl;
    private readonly ItemsControl _detectedProblemsItemsControl;
    private readonly ItemsControl _willNotItemsControl;
    private readonly Border _manualActionBorder;
    private readonly TextBlock _manualActionOverviewTextBlock;
    private readonly ItemsControl _manualActionsItemsControl;
    private readonly TextBlock _advancedDetailsTextBlock;

    public RecoveryConfirmationWindow()
        : this(new WorkspaceRecoveryAssessment
        {
            Title = "Recover Workspace",
            Summary = string.Empty,
            Findings = Array.Empty<string>(),
            ConfirmationMessage = string.Empty,
            WorkspaceName = string.Empty,
        })
    {
    }

    public RecoveryConfirmationWindow(WorkspaceRecoveryAssessment assessment)
    {
        InitializeComponent();
        AppWindowIcons.Apply(this);
        _workspaceNameTextBlock = this.FindControl<TextBlock>("WorkspaceNameTextBlock") ?? throw new InvalidOperationException("WorkspaceNameTextBlock was not found.");
        _statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock") ?? throw new InvalidOperationException("StatusTextBlock was not found.");
        _summaryTextBlock = this.FindControl<TextBlock>("SummaryTextBlock") ?? throw new InvalidOperationException("SummaryTextBlock was not found.");
        _confirmationTextBlock = this.FindControl<TextBlock>("ConfirmationTextBlock") ?? throw new InvalidOperationException("ConfirmationTextBlock was not found.");
        _recoverActionsItemsControl = this.FindControl<ItemsControl>("RecoverActionsItemsControl") ?? throw new InvalidOperationException("RecoverActionsItemsControl was not found.");
        _detectedProblemsItemsControl = this.FindControl<ItemsControl>("DetectedProblemsItemsControl") ?? throw new InvalidOperationException("DetectedProblemsItemsControl was not found.");
        _willNotItemsControl = this.FindControl<ItemsControl>("WillNotItemsControl") ?? throw new InvalidOperationException("WillNotItemsControl was not found.");
        _manualActionBorder = this.FindControl<Border>("ManualActionBorder") ?? throw new InvalidOperationException("ManualActionBorder was not found.");
        _manualActionOverviewTextBlock = this.FindControl<TextBlock>("ManualActionOverviewTextBlock") ?? throw new InvalidOperationException("ManualActionOverviewTextBlock was not found.");
        _manualActionsItemsControl = this.FindControl<ItemsControl>("ManualActionsItemsControl") ?? throw new InvalidOperationException("ManualActionsItemsControl was not found.");
        _advancedDetailsTextBlock = this.FindControl<TextBlock>("AdvancedDetailsTextBlock") ?? throw new InvalidOperationException("AdvancedDetailsTextBlock was not found.");
        _workspaceNameTextBlock.Text = string.IsNullOrWhiteSpace(assessment.WorkspaceName) ? assessment.Title.Replace("Recover ", string.Empty, StringComparison.Ordinal) : assessment.WorkspaceName;
        _statusTextBlock.Text = string.IsNullOrWhiteSpace(assessment.StatusSummary) ? "Workspace needs repair" : assessment.StatusSummary;
        _summaryTextBlock.Text = string.IsNullOrWhiteSpace(assessment.Summary) ? "Recover can repair generated runtime state without deleting your project files." : assessment.Summary;
        _confirmationTextBlock.Text = assessment.ConfirmationMessage;
        _recoverActionsItemsControl.ItemsSource = BuildListItems(assessment.RecoverActions.Count == 0 ? DefaultRecoverActions() : assessment.RecoverActions, "[ok] ");
        _detectedProblemsItemsControl.ItemsSource = BuildListItems(assessment.DetectedProblems.Count == 0 ? assessment.Findings : assessment.DetectedProblems, "[!] ");
        _willNotItemsControl.ItemsSource = BuildListItems(assessment.WillNotChange.Count == 0 ? DefaultWillNotItems() : assessment.WillNotChange, "- ");
        _manualActionBorder.IsVisible = !string.IsNullOrWhiteSpace(assessment.ManualActionSummary) || assessment.ManualActions.Count > 0;
        _manualActionOverviewTextBlock.Text = assessment.ManualActionSummary;
        _manualActionsItemsControl.ItemsSource = BuildListItems(assessment.ManualActions, "- ");
        _advancedDetailsTextBlock.Text = string.IsNullOrWhiteSpace(assessment.AdvancedDetails)
            ? string.Join(Environment.NewLine, assessment.Findings)
            : assessment.AdvancedDetails;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private static IReadOnlyList<TextBlock> BuildListItems(IReadOnlyList<string> values, string prefix)
        => values.Select(item => new TextBlock { Text = $"{prefix}{item}", TextWrapping = TextWrapping.Wrap }).ToList();

    private static IReadOnlyList<string> DefaultRecoverActions()
        =>
        [
            "Regenerate runtime files",
            "Refresh Docker Compose state",
            "Rebuild runtime metadata",
            "Validate generated scripts",
            "Keep your project files",
        ];

    private static IReadOnlyList<string> DefaultWillNotItems()
        =>
        [
            "Delete project files",
            "Modify Git history",
            "Delete documents",
            "Remove untracked work",
        ];

    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(false);
    private void ConfirmClicked(object? sender, RoutedEventArgs e) => Close(true);
}
