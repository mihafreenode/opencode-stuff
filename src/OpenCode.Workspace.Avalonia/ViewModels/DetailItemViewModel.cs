namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class DetailItemViewModel
{
    public DetailItemViewModel(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }
    public string Value { get; }
}
