using System.IO;
using System.Text.Json;
using OpenCode.Workspace.Manager.Models;

namespace OpenCode.Workspace.Manager.Services;

public sealed class QuickTutorialService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _tutorialPath;
    private readonly string _statePath;

    public QuickTutorialService(string applicationBasePath, string applicationDataRoot)
    {
        _tutorialPath = Path.Combine(applicationBasePath, "Tutorial", "quick-start.json");
        _statePath = Path.Combine(applicationDataRoot, "tutorial-state.json");
    }

    public QuickTutorialDocument LoadTutorial()
    {
        using var stream = File.OpenRead(_tutorialPath);
        return JsonSerializer.Deserialize<QuickTutorialDocument>(stream, JsonOptions)
            ?? new QuickTutorialDocument();
    }

    public bool ShouldPromptForQuickTutorial()
        => !LoadState().HasSeenQuickTutorialPrompt;

    public void MarkQuickTutorialPromptHandled()
    {
        var state = new QuickTutorialState
        {
            HasSeenQuickTutorialPrompt = true,
        };

        var stateDirectory = Path.GetDirectoryName(_statePath);
        if (!string.IsNullOrWhiteSpace(stateDirectory))
        {
            Directory.CreateDirectory(stateDirectory);
        }

        File.WriteAllText(_statePath, JsonSerializer.Serialize(state, JsonOptions));
    }

    private QuickTutorialState LoadState()
    {
        if (!File.Exists(_statePath))
        {
            return new QuickTutorialState();
        }

        try
        {
            return JsonSerializer.Deserialize<QuickTutorialState>(File.ReadAllText(_statePath), JsonOptions)
                ?? new QuickTutorialState();
        }
        catch (JsonException)
        {
            return new QuickTutorialState();
        }
        catch (IOException)
        {
            return new QuickTutorialState();
        }
    }
}
