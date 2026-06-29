using System.Windows.Input;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class ActionItemViewModel : ObservableObject
{
    private string _label;
    private string _description;
    private string _disabledReason;
    private bool _isEnabled;

    public ActionItemViewModel(string label, string description, bool isEnabled, string disabledReason, ICommand command)
    {
        _label = label;
        _description = description;
        _isEnabled = isEnabled;
        _disabledReason = disabledReason;
        Command = command;
    }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public string Description
    {
        get => _description;
        set
        {
            if (SetProperty(ref _description, value))
            {
                RaisePropertyChanged(nameof(HasDescription));
            }
        }
    }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                RaisePropertyChanged(nameof(ShowDisabledReason));
            }
        }
    }

    public string DisabledReason
    {
        get => _disabledReason;
        set
        {
            if (SetProperty(ref _disabledReason, value))
            {
                RaisePropertyChanged(nameof(ShowDisabledReason));
            }
        }
    }

    public bool ShowDisabledReason => !IsEnabled && !string.IsNullOrWhiteSpace(DisabledReason);

    public string AutomationId => Label switch
    {
        "Attach" => "WorkspaceAction_Attach",
        "Attach Only" => "WorkspaceAction_AttachOnly",
        "Start Only" => "WorkspaceAction_StartOnly",
        "Save Point" => "WorkspaceAction_SavePoint",
        "Backup" => "WorkspaceAction_Backup",
        "Recover" or "Recover Workspace" => "WorkspaceAction_Recover",
        "Troubleshoot Workspace" or "Investigate Problem" => "WorkspaceAction_InvestigateProblem",
        "Publish" => "WorkspaceAction_Publish",
        "Remove" => "WorkspaceAction_RemoveFromList",
        _ => $"WorkspaceAction_{BuildSafeToken(Label)}",
    };

    public string AutomationName => AutomationId;

    public ICommand Command { get; }

    private static string BuildSafeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unnamed";
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }
}
