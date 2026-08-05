using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.LocalClient;

namespace OpenCode.Workspace.Avalonia.Tests;

public sealed class DesktopInteractiveSessionArchitectureTests
{
    [Fact]
    public void ArchitectureGuard_DesktopInteractiveSessionService_DoesNotOwnHeartbeatLoop()
    {
        var repoRoot = GetRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "Services", "DesktopInteractiveSessionServices.cs"));

        Assert.DoesNotContain("PeriodicTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HeartbeatInteractiveSessionAttachmentAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestCloseAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuard_WorkspacesPageViewModel_DoesNotOwnHeartbeatLoop()
    {
        var repoRoot = GetRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "OpenCode.Workspace.Avalonia", "ViewModels", "WorkspacesPageViewModel.cs"));

        Assert.DoesNotContain("HeartbeatInteractiveSessionAttachmentAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PeriodicTimer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsDesktopTerminalLauncher_UsesStructuredArgumentList()
    {
        var descriptor = new ApprovedTerminalLaunchDescriptor
        {
            FileName = "wt.exe",
            WorkingDirectory = "C:\\Users\\tester",
            Arguments = ["new-tab", "--title", "Session", "--", "OpenCode.Workspace.Cli.exe", "interactive-session", "attach"],
        };

        var startInfo = WindowsDesktopTerminalLauncher.CreateStartInfo(descriptor);

        Assert.Equal("wt.exe", startInfo.FileName);
        Assert.Empty(startInfo.Arguments);
        Assert.Equal(descriptor.Arguments.Count, startInfo.ArgumentList.Count);
        Assert.Equal("interactive-session", startInfo.ArgumentList[5]);
    }

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
