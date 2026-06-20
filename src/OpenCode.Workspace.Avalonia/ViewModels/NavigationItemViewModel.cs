using System.Windows.Input;

namespace OpenCode.Workspace.Avalonia.ViewModels;

public sealed class NavigationItemViewModel
{
    public NavigationItemViewModel(string title, PageViewModel page, ICommand selectCommand)
    {
        Title = title;
        Page = page;
        SelectCommand = selectCommand;
    }

    public string Title { get; }
    public PageViewModel Page { get; }
    public ICommand SelectCommand { get; }
}
