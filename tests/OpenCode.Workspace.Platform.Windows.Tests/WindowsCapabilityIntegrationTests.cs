using System.IO;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Platform.Windows;

namespace OpenCode.Workspace.Platform.Windows.Tests;

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
    public void AttachCommandGeneration_UsesExpectedNativeSessionAttachContract()
    {
        var builder = new AttachCommandBuilder();
        const string rootPath = "C:\\Users\\miha.pirnat\\OneDrive - Kopa, racunalniski inzeniring d.d\\Dokumenti\\Delovni prostori, stranke\\Smisel zaščite";
        var command = builder.Build(new OpenCode.Workspace.Core.Models.WorkspaceSnapshot
        {
            Record = new OpenCode.Workspace.Core.Models.WorkspaceRecord { Name = "smoke data workspace", RootPath = rootPath, CreatedUtc = DateTimeOffset.UtcNow, LastOpenedUtc = DateTimeOffset.UtcNow },
            Definition = new OpenCode.Workspace.Core.Models.WorkspaceDefinition
            {
                Workspace = new OpenCode.Workspace.Core.Models.WorkspaceMetadata { Name = "smoke data workspace" },
            },
            Paths = OpenCode.Workspace.Core.Workspaces.WorkspacePathBuilder.Build(rootPath),
            ConfigurationPath = "workspace.yaml",
            RuntimeState = OpenCode.Workspace.Core.Models.WorkspaceRuntimeState.Running,
            Safety = new OpenCode.Workspace.Core.Models.WorkspaceSafetySnapshot
            {
                OverallStatus = OpenCode.Workspace.Core.Models.WorkspaceSafetyLevel.Protected,
                Headline = "Protected",
                Message = "Test snapshot",
                LocalRecovery = new OpenCode.Workspace.Core.Models.WorkspaceLocalRecoverySnapshot(),
                Backup = new OpenCode.Workspace.Core.Models.WorkspaceBackupSnapshot(),
                IgnorePolicy = new OpenCode.Workspace.Core.Models.WorkspaceIgnorePolicyReview(),
                AdvancedGit = new OpenCode.Workspace.Core.Models.WorkspaceAdvancedGitSnapshot(),
            },
            Session = new OpenCode.Workspace.Core.Models.WorkspaceSessionSnapshot
            {
                SessionName = "smoke-data-workspace",
                State = OpenCode.Workspace.Core.Models.WorkspaceSessionState.Resumable,
            },
        });

        Assert.Equal("wt.exe", command.FileName);
        Assert.Equal("OpenCode Stuff - smoke data workspace", command.Title);
        Assert.Equal("new-tab", command.Arguments[0]);
        Assert.Equal("--title", command.Arguments[1]);
        Assert.Equal("OpenCode Stuff - smoke data workspace", command.Arguments[2]);
        Assert.Equal("--", command.Arguments[3]);
        Assert.Equal("powershell.exe", command.Arguments[4]);
        Assert.Equal("-NoExit", command.Arguments[5]);
        Assert.Equal("-ExecutionPolicy", command.Arguments[6]);
        Assert.Equal("Bypass", command.Arguments[7]);
        Assert.Equal("-File", command.Arguments[8]);
        Assert.Contains("attach-workspace.ps1", command.Arguments[9]);
        Assert.Contains("OneDrive - Kopa, racunalniski inzeniring d.d", command.Arguments[9]);
        Assert.Contains("Smisel zaščite", command.Arguments[9]);
        Assert.Contains("attach-workspace.ps1", command.CommandText);
        Assert.Contains("wt.exe new-tab --title \"OpenCode Stuff - smoke data workspace\" -- powershell.exe -NoExit -ExecutionPolicy Bypass -File", command.CommandText);
        Assert.DoesNotContain("wt.exe powershell.exe", command.CommandText, StringComparison.Ordinal);
        Assert.False(command.CommandText.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("powershell.exe -NoExit -ExecutionPolicy Bypass -File \"", command.FallbackCommandText);

        var startInfo = WindowsTerminalLauncher.CreateStartInfo(command);
        Assert.EndsWith("wt.exe", startInfo.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(string.IsNullOrWhiteSpace(startInfo.Arguments));
        Assert.Equal(command.Arguments.Count, startInfo.ArgumentList.Count);
        Assert.Equal(command.Arguments[9], startInfo.ArgumentList[9]);
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

    [Fact]
    public void WorkspacePathBuilder_PreservesWindowsAttachPathsWhileAddingRuntimeStatePath()
    {
        var paths = WorkspacePathBuilder.Build("C:\\Workspaces\\Demo");

        Assert.EndsWith("attach-workspace.ps1", paths.AttachWrapperScriptPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("terminal-diagnostics.ps1", paths.TerminalDiagnosticsScriptPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".opencode\\local\\runtime-state.yaml", paths.RuntimeStatePath, StringComparison.OrdinalIgnoreCase);
    }
}
