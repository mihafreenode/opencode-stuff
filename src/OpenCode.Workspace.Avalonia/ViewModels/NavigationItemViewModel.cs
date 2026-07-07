using System.Windows.Input;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class NavigationItemViewModel : ObservableObject
{
    private bool _isSelected;

    public NavigationItemViewModel(string title, string iconGlyph, PageViewModel page, ICommand selectCommand)
    {
        Title = title;
        IconGlyph = iconGlyph;
        Page = page;
        SelectCommand = selectCommand;
    }

    public string Title { get; }
    public string IconGlyph { get; }
    public PageViewModel Page { get; }
    public ICommand SelectCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
