using System.Collections.Generic;

namespace OpenCode.Workspace.Manager.Models;

public sealed class QuickTutorialDocument
{
    public string Title { get; init; } = "Quick Tutorial";
    public string Subtitle { get; init; } = "Short first-run guide for safe workspace use.";
    public List<QuickTutorialStep> Steps { get; init; } = [];
}

public sealed class QuickTutorialStep
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public List<string> Bullets { get; init; } = [];
    public List<string> Highlights { get; init; } = [];
    public List<string> PresenterFlow { get; init; } = [];
    public string EstimatedTime { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public string ImagePath { get; init; } = string.Empty;
    public QuickTutorialImagePlaceholder Image { get; init; } = new();
}

public sealed class QuickTutorialImagePlaceholder
{
    public string Title { get; init; } = string.Empty;
    public string Caption { get; init; } = string.Empty;
    public List<string> Callouts { get; init; } = [];
}

public sealed class QuickTutorialState
{
    public bool HasSeenQuickTutorialPrompt { get; init; }
}
