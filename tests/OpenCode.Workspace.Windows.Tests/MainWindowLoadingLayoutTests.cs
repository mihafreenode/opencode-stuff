using System.IO;

namespace OpenCode.Workspace.Windows.Tests;

public sealed class MainWindowLoadingLayoutTests
{
    [Fact]
    public void LoadingState_UsesDedicatedWorkspaceHostLayout()
    {
        var xamlPath = Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Manager", "MainWindow.xaml");
        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("<Border Padding=\"48\"", xaml);
        Assert.Contains("Visibility=\"{Binding ShowWorkspaceLoadingState, Converter={StaticResource BooleanToVisibilityConverter}}\"", xaml);
        Assert.Contains("<Grid MinWidth=\"420\">", xaml);
        Assert.Contains("<StackPanel HorizontalAlignment=\"Center\"", xaml);
        Assert.Contains("VerticalAlignment=\"Center\"", xaml);
        Assert.Contains("Text=\"{Binding WorkspaceLoadingTitle}\"", xaml);
        Assert.Contains("TextWrapping=\"Wrap\"", xaml);
        Assert.Contains("Width=\"280\"", xaml);
        Assert.Contains("<Grid Visibility=\"{Binding ShowWorkspaceSidePanels, Converter={StaticResource BooleanToVisibilityConverter}}\">", xaml);
    }
}
