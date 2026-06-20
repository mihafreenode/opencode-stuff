namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class DiagnosticItemViewModel
{
    public DiagnosticItemViewModel(string title, string statusLabel, string message, string nextStep)
    {
        Title = title;
        StatusLabel = statusLabel;
        Message = message;
        NextStep = nextStep;
    }

    public string Title { get; }
    public string StatusLabel { get; }
    public string Message { get; }
    public string NextStep { get; }
    public bool HasNextStep => !string.IsNullOrWhiteSpace(NextStep);
}
