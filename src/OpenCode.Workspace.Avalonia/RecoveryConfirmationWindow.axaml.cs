using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using OpenCode.Workspace.Avalonia.Services;

namespace OpenCode.Workspace.Avalonia;

public partial class RecoveryConfirmationWindow : Window
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(7);

    private readonly Func<CancellationToken, Task<WorkspaceRecoveryAssessment>> _refreshAssessmentAsync;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Button _refreshButton;
    private readonly Button _confirmButton;
    private readonly TextBlock _workspaceNameTextBlock;
    private readonly TextBlock _statusTextBlock;
    private readonly TextBlock _lastCheckedTextBlock;
    private readonly TextBlock _summaryTextBlock;
    private readonly TextBlock _confirmationTextBlock;
    private readonly ItemsControl _recoverActionsItemsControl;
    private readonly ItemsControl _currentProblemsItemsControl;
    private readonly ItemsControl _previousFailureItemsControl;
    private readonly ItemsControl _willNotItemsControl;
    private readonly Border _manualActionBorder;
    private readonly TextBlock _manualActionOverviewTextBlock;
    private readonly ItemsControl _manualActionsItemsControl;
    private readonly TextBlock _advancedDetailsTextBlock;
    private CancellationTokenSource? _refreshCts;
    private bool _isRefreshing;

    public RecoveryConfirmationWindow()
        : this(new WorkspaceRecoveryAssessment
        {
            Title = "Recover Workspace",
            Summary = string.Empty,
            Findings = Array.Empty<string>(),
            ConfirmationMessage = string.Empty,
            WorkspaceName = string.Empty,
        }, _ => Task.FromResult(new WorkspaceRecoveryAssessment
        {
            Title = "Recover Workspace",
            Summary = string.Empty,
            Findings = Array.Empty<string>(),
            ConfirmationMessage = string.Empty,
            WorkspaceName = string.Empty,
        }))
    {
    }

    public RecoveryConfirmationWindow(WorkspaceRecoveryAssessment assessment, Func<CancellationToken, Task<WorkspaceRecoveryAssessment>> refreshAssessmentAsync)
    {
        InitializeComponent();
        AppWindowIcons.Apply(this);
        _refreshAssessmentAsync = refreshAssessmentAsync;
        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += RefreshTimerTick;
        Opened += OnOpened;
        Closed += OnClosed;
        _refreshButton = this.FindControl<Button>("RefreshButton") ?? throw new InvalidOperationException("RefreshButton was not found.");
        _confirmButton = this.FindControl<Button>("RecoverWorkspaceConfirmButton") ?? this.FindControl<Button>("Recover Workspace Confirm") ?? throw new InvalidOperationException("RecoverWorkspaceConfirmButton was not found.");
        _workspaceNameTextBlock = this.FindControl<TextBlock>("WorkspaceNameTextBlock") ?? throw new InvalidOperationException("WorkspaceNameTextBlock was not found.");
        _statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock") ?? throw new InvalidOperationException("StatusTextBlock was not found.");
        _lastCheckedTextBlock = this.FindControl<TextBlock>("LastCheckedTextBlock") ?? throw new InvalidOperationException("LastCheckedTextBlock was not found.");
        _summaryTextBlock = this.FindControl<TextBlock>("SummaryTextBlock") ?? throw new InvalidOperationException("SummaryTextBlock was not found.");
        _confirmationTextBlock = this.FindControl<TextBlock>("ConfirmationTextBlock") ?? throw new InvalidOperationException("ConfirmationTextBlock was not found.");
        _recoverActionsItemsControl = this.FindControl<ItemsControl>("RecoverActionsItemsControl") ?? throw new InvalidOperationException("RecoverActionsItemsControl was not found.");
        _currentProblemsItemsControl = this.FindControl<ItemsControl>("CurrentProblemsItemsControl") ?? throw new InvalidOperationException("CurrentProblemsItemsControl was not found.");
        _previousFailureItemsControl = this.FindControl<ItemsControl>("PreviousFailureItemsControl") ?? throw new InvalidOperationException("PreviousFailureItemsControl was not found.");
        _willNotItemsControl = this.FindControl<ItemsControl>("WillNotItemsControl") ?? throw new InvalidOperationException("WillNotItemsControl was not found.");
        _manualActionBorder = this.FindControl<Border>("ManualActionBorder") ?? throw new InvalidOperationException("ManualActionBorder was not found.");
        _manualActionOverviewTextBlock = this.FindControl<TextBlock>("ManualActionOverviewTextBlock") ?? throw new InvalidOperationException("ManualActionOverviewTextBlock was not found.");
        _manualActionsItemsControl = this.FindControl<ItemsControl>("ManualActionsItemsControl") ?? throw new InvalidOperationException("ManualActionsItemsControl was not found.");
        _advancedDetailsTextBlock = this.FindControl<TextBlock>("AdvancedDetailsTextBlock") ?? throw new InvalidOperationException("AdvancedDetailsTextBlock was not found.");
        ApplyAssessment(assessment, isLoading: true);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _refreshTimer.Start();
        _ = RefreshAssessmentAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = null;
    }

    private async void RefreshTimerTick(object? sender, EventArgs e)
    {
        await RefreshAssessmentAsync();
    }

    private async Task RefreshAssessmentAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();

        try
        {
            ApplyLoadingState();
            var assessment = await _refreshAssessmentAsync(_refreshCts.Token);
            ApplyAssessment(assessment, isLoading: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ApplyAssessment(
                new WorkspaceRecoveryAssessment
                {
                    Title = "Recover Workspace",
                    Summary = "Unable to check current state right now.",
                    Findings = [exception.Message],
                    ConfirmationMessage = "Run workspace recovery now?",
                    WorkspaceName = _workspaceNameTextBlock.Text ?? string.Empty,
                    StatusSummary = "Checking current state failed",
                    CurrentProblems = ["Unable to check current state"],
                    PreviousFailureContext = Array.Empty<string>(),
                    AdvancedDetails = exception.ToString(),
                },
                isLoading: false);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void ApplyLoadingState()
    {
        _statusTextBlock.Text = "Checking current state...";
        _summaryTextBlock.Text = "Checking current state...";
        _lastCheckedTextBlock.Text = string.Empty;
        _refreshButton.IsEnabled = false;
        _confirmButton.IsEnabled = false;
    }

    private void ApplyAssessment(WorkspaceRecoveryAssessment assessment, bool isLoading)
    {
        _workspaceNameTextBlock.Text = string.IsNullOrWhiteSpace(assessment.WorkspaceName) ? assessment.Title.Replace("Recover ", string.Empty, StringComparison.Ordinal) : assessment.WorkspaceName;
        _statusTextBlock.Text = string.IsNullOrWhiteSpace(assessment.StatusSummary) ? (isLoading ? "Checking current state..." : "Workspace needs repair") : assessment.StatusSummary;
        _summaryTextBlock.Text = string.IsNullOrWhiteSpace(assessment.Summary) ? "Recover can repair generated runtime state without deleting your project files." : assessment.Summary;
        _lastCheckedTextBlock.Text = assessment.LastCheckedAt is null ? string.Empty : $"Last checked {assessment.LastCheckedAt.Value:HH:mm:ss}";
        _confirmationTextBlock.Text = assessment.ConfirmationMessage;
        _recoverActionsItemsControl.ItemsSource = BuildListItems(assessment.RecoverActions.Count == 0 ? DefaultRecoverActions() : assessment.RecoverActions, "[ok] ");
        _currentProblemsItemsControl.ItemsSource = BuildListItems(assessment.CurrentProblems.Count == 0 ? assessment.Findings : assessment.CurrentProblems, "[!] ");
        _previousFailureItemsControl.ItemsSource = BuildListItems(assessment.PreviousFailureContext, "- ");
        _willNotItemsControl.ItemsSource = BuildListItems(assessment.WillNotChange.Count == 0 ? DefaultWillNotItems() : assessment.WillNotChange, "- ");
        _manualActionBorder.IsVisible = !string.IsNullOrWhiteSpace(assessment.ManualActionSummary) || assessment.ManualActions.Count > 0;
        _manualActionOverviewTextBlock.Text = assessment.ManualActionSummary;
        _manualActionsItemsControl.ItemsSource = BuildListItems(assessment.ManualActions, "- ");
        _advancedDetailsTextBlock.Text = string.IsNullOrWhiteSpace(assessment.AdvancedDetails)
            ? string.Join(Environment.NewLine, assessment.Findings)
            : assessment.AdvancedDetails;
        _refreshButton.IsEnabled = !isLoading;
        _confirmButton.IsEnabled = !isLoading;
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
    private async void RefreshClicked(object? sender, RoutedEventArgs e) => await RefreshAssessmentAsync();
}
