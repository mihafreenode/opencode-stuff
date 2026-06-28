using OpenCode.Workspace.Platform;
using OpenCode.Workspace.Platform.Linux;

namespace OpenCode.Workspace.Platform.Linux.Tests;

public sealed class LinuxHostCapabilitiesTests
{
    [Fact]
    public async Task DetectAsync_ReportsFontsTerminalsAndPackageManagers()
    {
        var capabilities = new LinuxHostCapabilities(new FakeCommandProbe(command => command switch
        {
            "fc-match JetBrains Mono" => Success("JetBrains Mono Regular\n"),
            "fc-match JetBrainsMono Nerd Font" => Success("JetBrainsMono Nerd Font Mono\n"),
            "fc-match Cascadia Code" => Success("Cascadia Code Regular\n"),
            "sh -lc command -v gnome-terminal" => Success("/usr/bin/gnome-terminal\n"),
            "sh -lc command -v konsole" => Failure(),
            "sh -lc command -v xfce4-terminal" => Failure(),
            "sh -lc command -v kitty" => Success("/usr/bin/kitty\n"),
            "sh -lc command -v wezterm" => Failure(),
            "sh -lc command -v xterm" => Success("/usr/bin/xterm\n"),
            "sh -lc command -v docker" => Success("/usr/bin/docker\n"),
            "sh -lc docker compose version" => Success("Docker Compose version v2.38.1\n"),
            "sh -lc command -v podman" => Failure(),
            "sh -lc command -v git" => Success("/usr/bin/git\n"),
            "sh -lc command -v opencode" => Success("/usr/local/bin/opencode\n"),
            "sh -lc command -v apt" => Success("/usr/bin/apt\n"),
            "sh -lc command -v dnf" => Failure(),
            "sh -lc command -v pacman" => Failure(),
            "sh -lc command -v zypper" => Failure(),
            _ => Failure($"Unhandled command: {command}"),
        }));

        var report = await capabilities.DetectAsync();

        Assert.Equal(PlatformKind.Linux, report.Platform);
        Assert.Equal(HostCapabilityStatus.Available, report.FindEntry("font.nerd-fonts")?.Status);
        Assert.Equal(HostCapabilityStatus.Available, report.FindEntry("terminal.gnome")?.Status);
        Assert.Equal(HostCapabilityStatus.Available, report.FindEntry("container.docker")?.Status);
        Assert.Equal(HostCapabilityStatus.Available, report.FindEntry("container.docker-compose")?.Status);
        Assert.Equal(HostCapabilityStatus.Available, report.FindEntry("tool.opencode-cli")?.Status);
        Assert.Equal(HostCapabilityStatus.Available, report.FindEntry("package.apt")?.Status);
        Assert.Equal(HostCapabilityStatus.Unavailable, report.FindEntry("terminal.profile-support")?.Status);
    }

    [Fact]
    public void Factory_SelectsLinuxImplementation_WhenPlatformRuntimeReportsLinux()
    {
        var factory = new HostCapabilitiesFactory(
            () => new StubHostCapabilities(PlatformKind.Windows),
            () => new StubHostCapabilities(PlatformKind.Linux),
            () => new StubHostCapabilities(PlatformKind.MacOS),
            new FakePlatformRuntime { IsLinux = true, Architecture = "X64" });

        var capabilities = factory.CreateForCurrentPlatform();

        Assert.Equal(PlatformKind.Linux, capabilities.Platform);
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
            => Task.FromResult(HostCapabilityReport.Empty(Platform, "X64"));
    }

    private static CommandProbeResult Success(string output)
        => new() { IsSuccess = true, ExitCode = 0, StandardOutput = output };

    private static CommandProbeResult Failure(string error = "missing")
        => new() { IsSuccess = false, ExitCode = 1, StandardError = error, FailureMessage = error };
}
