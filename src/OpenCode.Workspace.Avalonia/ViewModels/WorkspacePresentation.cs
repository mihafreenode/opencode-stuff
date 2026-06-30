namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspacePresentation
{
    public required string Headline { get; init; }
    public required string Summary { get; init; }
    public string CurrentStatus { get; init; } = string.Empty;
    public string CurrentActivity { get; init; } = string.Empty;
    public string ActivitySummary { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public string ServicesSummary { get; init; } = string.Empty;
    public string RecentHistoryNote { get; init; } = string.Empty;
    public ActionItemViewModel? PrimaryAction { get; init; }
    public IReadOnlyList<ActionItemViewModel> SecondaryActions { get; init; } = Array.Empty<ActionItemViewModel>();
    public IReadOnlyList<ActionItemViewModel> AdvancedActions { get; init; } = Array.Empty<ActionItemViewModel>();
}
