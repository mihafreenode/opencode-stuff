using System.IO;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Manager.Services;

namespace OpenCode.Workspace.Windows.Tests;

public sealed class WindowsCapabilityIntegrationTests
{
    [Fact]
    public async Task DockerDesktopDetection_ReturnsExplicitAvailabilityState()
    {
        var capabilities = new WindowsHostCapabilities(new ProcessRunner());
        var result = await capabilities.CheckDockerDesktopAsync();

        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Fact]
    public async Task WindowsTerminalDetection_ReturnsExplicitAvailabilityState()
    {
        var capabilities = new WindowsHostCapabilities(new ProcessRunner());
        var result = await capabilities.CheckWindowsTerminalAsync();

        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Fact]
    public void NerdFontDetection_ReturnsExplicitAvailabilityState()
    {
        var capabilities = new WindowsHostCapabilities(new ProcessRunner());
        var result = capabilities.CheckNerdFont();

        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Fact]
    public void PreferredTerminalFace_ResolvesToActualJetBrainsFontFaceName()
    {
        var capabilities = new WindowsHostCapabilities(new ProcessRunner());
        var face = capabilities.ResolvePreferredTerminalFace("JetBrainsMono Nerd Font");

        Assert.False(string.IsNullOrWhiteSpace(face));
        Assert.Contains("JetBrains", face, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TerminalProfileGeneration_IsDeterministic()
    {
        var generator = new TerminalProfileGenerator();
        var profile = generator.Generate("Smoke Workspace");

        Assert.Contains("Smoke Workspace", profile);
        Assert.Contains("JetBrainsMono Nerd Font", profile);
        Assert.Contains("colorScheme", profile);
    }

    [Fact]
    public void AttachCommandGeneration_UsesExpectedScreenRestoreContract()
    {
        var builder = new AttachCommandBuilder();
        var command = builder.Build(new OpenCode.Workspace.Core.Models.WorkspaceSnapshot
        {
            Record = new OpenCode.Workspace.Core.Models.WorkspaceRecord { Name = "smoke data workspace", RootPath = "C:\\Workspaces With Spaces\\smoke data workspace", CreatedUtc = DateTimeOffset.UtcNow, LastOpenedUtc = DateTimeOffset.UtcNow },
            Definition = new OpenCode.Workspace.Core.Models.WorkspaceDefinition
            {
                Workspace = new OpenCode.Workspace.Core.Models.WorkspaceMetadata { Name = "smoke data workspace" },
            },
            Paths = OpenCode.Workspace.Core.Workspaces.WorkspacePathBuilder.Build("C:\\Workspaces With Spaces\\smoke data workspace"),
            RuntimeState = OpenCode.Workspace.Core.Models.WorkspaceRuntimeState.Running,
        });

        Assert.Equal("wt.exe", command.FileName);
        Assert.Equal("powershell.exe", command.Arguments[0]);
        Assert.Equal("-NoExit", command.Arguments[1]);
        Assert.Equal("-ExecutionPolicy", command.Arguments[2]);
        Assert.Equal("Bypass", command.Arguments[3]);
        Assert.Equal("-File", command.Arguments[4]);
        Assert.Contains("attach-workspace.ps1", command.Arguments[5]);
        Assert.Contains("Workspaces With Spaces", command.Arguments[5]);
        Assert.Contains("attach-workspace.ps1", command.CommandText);
        Assert.Contains("wt.exe powershell.exe -NoExit -ExecutionPolicy Bypass -File", command.CommandText);
    }

    [Fact]
    public void ManagedProfileFragment_IsGeneratedWithoutTouchingUnrelatedProfiles()
    {
        var manager = new WindowsTerminalProfileManager();
        manager.EnsureManagedProfile(
            new OpenCode.Workspace.Core.Models.WorkspaceDefinition
            {
                Workspace = new OpenCode.Workspace.Core.Models.WorkspaceMetadata { Name = "profile-smoke" },
            },
            new OpenCode.Workspace.Core.Models.TerminalFontPreferences { Family = "JetBrainsMono Nerd Font" });

        Assert.True(manager.ManagedProfileExists());
        var fragment = File.ReadAllText(manager.GetFragmentFilePath());
        Assert.Contains("OpenCode Stuff - profile-smoke", fragment);
        Assert.Contains("JetBrainsMono Nerd Font", fragment);
    }
}
