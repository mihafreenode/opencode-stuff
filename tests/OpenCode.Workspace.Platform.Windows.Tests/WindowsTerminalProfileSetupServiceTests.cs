using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Platform.Windows;

namespace OpenCode.Workspace.Platform.Windows.Tests;

public sealed class WindowsTerminalProfileSetupServiceTests
{
    [SkippableFact]
    public async Task EnsureAsync_ReturnsUnavailableWhenWindowsTerminalMissing()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test.");
        var service = new WindowsTerminalProfileSetupService(
            new FakeProfileManager(),
            new FakeHostCapabilities { TerminalCheckResult = PrerequisiteCheckResult.Unavailable("Windows Terminal not installed.") });

        var result = await service.EnsureAsync(CreateDefinition("alpha"));

        Assert.Equal(WindowsTerminalProfileSetupStatus.Unavailable, result.Status);
        Assert.Equal("Windows Terminal not installed.", result.Summary);
    }

    [SkippableFact]
    public async Task EnsureAsync_ReturnsAlreadyConfiguredWhenFontMatches()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test.");
        var profileManager = new FakeProfileManager { ExistingFontFace = "JetBrainsMono Nerd Font" };
        var service = new WindowsTerminalProfileSetupService(
            profileManager,
            new FakeHostCapabilities { ResolvedFontFace = "JetBrainsMono Nerd Font" });

        var result = await service.EnsureAsync(CreateDefinition("alpha"));

        Assert.Equal(WindowsTerminalProfileSetupStatus.AlreadyConfigured, result.Status);
        Assert.Equal(0, profileManager.EnsureCallCount);
    }

    [SkippableFact]
    public async Task EnsureAsync_ReturnsCreatedWhenProfileDoesNotExistYet()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test.");
        var profileManager = new FakeProfileManager();
        var service = new WindowsTerminalProfileSetupService(
            profileManager,
            new FakeHostCapabilities { ResolvedFontFace = "CaskaydiaCove Nerd Font" });

        var result = await service.EnsureAsync(CreateDefinition("alpha"));

        Assert.Equal(WindowsTerminalProfileSetupStatus.Created, result.Status);
        Assert.Equal(1, profileManager.EnsureCallCount);
        Assert.Equal("CaskaydiaCove Nerd Font", result.ResolvedFontFace);
    }

    [SkippableFact]
    public async Task EnsureAsync_ReturnsUpdatedWhenFontChanges()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test.");
        var profileManager = new FakeProfileManager { ExistingFontFace = "Old Font" };
        var service = new WindowsTerminalProfileSetupService(
            profileManager,
            new FakeHostCapabilities { ResolvedFontFace = "JetBrainsMono Nerd Font" });

        var result = await service.EnsureAsync(CreateDefinition("alpha"));

        Assert.Equal(WindowsTerminalProfileSetupStatus.Updated, result.Status);
        Assert.Equal(1, profileManager.EnsureCallCount);
    }

    [SkippableFact]
    public async Task EnsureAsync_ReturnsFailedWhenProfileWriteThrows()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only test.");
        var service = new WindowsTerminalProfileSetupService(
            new FakeProfileManager { EnsureException = new InvalidOperationException("Access denied.") },
            new FakeHostCapabilities());

        var result = await service.EnsureAsync(CreateDefinition("alpha"));

        Assert.Equal(WindowsTerminalProfileSetupStatus.Failed, result.Status);
        Assert.Equal("Windows Terminal profile setup failed.", result.Summary);
        Assert.Equal("Access denied.", result.FailureReason);
    }

    private static WorkspaceDefinition CreateDefinition(string workspaceName)
        => new()
        {
            Workspace = new WorkspaceMetadata { Name = workspaceName },
            Terminal = new TerminalPreferences
            {
                Font = new TerminalFontPreferences { Family = "JetBrainsMono Nerd Font" },
                Prompt = new TerminalPromptPreferences(),
                Utilities = new TerminalUtilityPreferences(),
            },
        };

    private sealed class FakeHostCapabilities : IWindowsHostCapabilities
    {
        public PrerequisiteCheckResult TerminalCheckResult { get; init; } = PrerequisiteCheckResult.Available("Windows Terminal command is available.");
        public string ResolvedFontFace { get; init; } = "JetBrainsMono Nerd Font";

        public Task<PrerequisiteCheckResult> CheckWindowsTerminalAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(TerminalCheckResult);

        public string ResolvePreferredTerminalFace(string fontDisplayName) => ResolvedFontFace;
    }

    private sealed class FakeProfileManager : IWindowsTerminalProfileManager
    {
        public string? ExistingFontFace { get; init; }
        public Exception? EnsureException { get; init; }
        public int EnsureCallCount { get; private set; }

        public string GetFragmentFilePath() => @"C:\Users\test\AppData\Local\Microsoft\Windows Terminal\profiles.json";

        public void EnsureManagedProfile(WorkspaceDefinition definition, TerminalFontPreferences fontPreferences, string resolvedFace)
        {
            EnsureCallCount++;
            if (EnsureException is not null)
            {
                throw EnsureException;
            }
        }

        public string GetProfileName(WorkspaceDefinition definition) => $"OpenCode Stuff - {definition.Workspace.Name}";

        public string? GetConfiguredFontFace(WorkspaceDefinition definition) => ExistingFontFace;
    }
}
