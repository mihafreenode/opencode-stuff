using OpenCode.Workspace.Platform;
using OpenCode.Workspace.Platform.MacOS;

namespace OpenCode.Workspace.Platform.MacOS.Tests;

public sealed class MacHostCapabilitiesTests
{
    [Fact]
    public async Task DetectAsync_ReportsFontsTerminalsAndHomebrew()
    {
        var capabilities = new MacHostCapabilities(new FakeCommandProbe(command => command switch
        {
            "system_profiler SPFontsDataType" => Success("JetBrains Mono\nCascadia Code\nSymbols Nerd Font\n"),
            "sh -lc test -d '/Applications/Utilities/Terminal.app'" => Success(string.Empty),
            "sh -lc test -d '/Applications/iTerm.app'" => Failure(),
            "sh -lc command -v wezterm" => Success("/opt/homebrew/bin/wezterm\n"),
            "sh -lc command -v docker" => Success("/usr/local/bin/docker\n"),
            "sh -lc command -v podman" => Failure(),
            "sh -lc command -v git" => Success("/usr/bin/git\n"),
            "sh -lc command -v brew" => Success("/opt/homebrew/bin/brew\n"),
            _ => Failure($"Unhandled command: {command}"),
        }));

        var report = await capabilities.DetectAsync();

        Assert.Equal(PlatformKind.MacOS, report.Platform);
        Assert.Equal(HostCapabilityStatus.Available, report.FindEntry("font.jetbrains-mono")?.Status);
        Assert.Equal(HostCapabilityStatus.Available, report.FindEntry("terminal.terminal-app")?.Status);
        Assert.Equal(HostCapabilityStatus.Available, report.FindEntry("terminal.wezterm")?.Status);
        Assert.Equal(HostCapabilityStatus.Available, report.FindEntry("package.brew")?.Status);
        Assert.Equal(HostCapabilityStatus.Unavailable, report.FindEntry("terminal.profile-support")?.Status);
    }

    [Fact]
    public void Factory_SelectsMacImplementation_WhenPlatformRuntimeReportsMacOS()
    {
        var factory = new HostCapabilitiesFactory(
            () => new StubHostCapabilities(PlatformKind.Windows),
            () => new StubHostCapabilities(PlatformKind.Linux),
            () => new StubHostCapabilities(PlatformKind.MacOS),
            new FakePlatformRuntime { IsMacOS = true, Architecture = "Arm64" });

        var capabilities = factory.CreateForCurrentPlatform();

        Assert.Equal(PlatformKind.MacOS, capabilities.Platform);
    }

    private sealed class FakeCommandProbe : ICommandProbe
    {
        private readonly Func<string, CommandProbeResult> _handler;

        public FakeCommandProbe(Func<string, CommandProbeResult> handler)
        {
            _handler = handler;
        }

        public Task<CommandProbeResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
            => Task.FromResult(_handler($"{fileName} {string.Join(' ', arguments)}"));
    }

    private sealed class FakePlatformRuntime : IPlatformRuntime
    {
        public bool IsWindows { get; init; }
        public bool IsLinux { get; init; }
        public bool IsMacOS { get; init; }
        public string Architecture { get; init; } = string.Empty;
    }

    private sealed class StubHostCapabilities : IHostCapabilities
    {
        public StubHostCapabilities(PlatformKind platform)
        {
            Platform = platform;
        }

        public PlatformKind Platform { get; }

        public Task<HostCapabilityReport> DetectAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(HostCapabilityReport.Empty(Platform, "Arm64"));
    }

    private static CommandProbeResult Success(string output)
        => new() { IsSuccess = true, ExitCode = 0, StandardOutput = output };

    private static CommandProbeResult Failure(string error = "missing")
        => new() { IsSuccess = false, ExitCode = 1, StandardError = error, FailureMessage = error };
}
