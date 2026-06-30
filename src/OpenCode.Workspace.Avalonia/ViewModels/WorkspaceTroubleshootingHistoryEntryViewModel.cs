namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class WorkspaceTroubleshootingHistoryEntryViewModel
{
    public WorkspaceTroubleshootingHistoryEntryViewModel(string title, string outcome, string summary, string evidence, string recommendation, string confidence, string estimatedDuration, string source, DateTimeOffset occurredUtc)
    {
        Title = title;
        Outcome = outcome;
        Summary = summary;
        Evidence = evidence;
        Recommendation = recommendation;
        Confidence = confidence;
        EstimatedDuration = estimatedDuration;
        Source = source;
        OccurredLabel = occurredUtc.ToLocalTime().ToString("g");
    }

    public string Title { get; }
    public string Outcome { get; }
    public string Summary { get; }
    public string Evidence { get; }
    public string Recommendation { get; }
    public string Confidence { get; }
    public string EstimatedDuration { get; }
    public string Source { get; }
    public string OccurredLabel { get; }
    public bool HasEvidence => !string.IsNullOrWhiteSpace(Evidence);
    public bool HasRecommendation => !string.IsNullOrWhiteSpace(Recommendation);
    public bool HasConfidence => !string.IsNullOrWhiteSpace(Confidence);
    public bool HasEstimatedDuration => !string.IsNullOrWhiteSpace(EstimatedDuration);
}
