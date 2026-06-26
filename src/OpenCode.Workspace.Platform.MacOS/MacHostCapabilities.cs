using System.Runtime.InteropServices;
using OpenCode.Workspace.Platform;

namespace OpenCode.Workspace.Platform.MacOS;

public sealed class MacHostCapabilities : IHostCapabilities
{
    private readonly ICommandProbe _commandProbe;

    public MacHostCapabilities(ICommandProbe commandProbe)
    {
        _commandProbe = commandProbe;
    }

    public PlatformKind Platform => PlatformKind.MacOS;

    public async Task<HostCapabilityReport> DetectAsync(CancellationToken cancellationToken = default)
    {
        var fontInventory = await _commandProbe.RunAsync("system_profiler", ["SPFontsDataType"], cancellationToken);
        var fontText = fontInventory.StandardOutput;

        var sections = new List<HostCapabilitySection>
        {
            new()
            {
                Id = "fonts",
                DisplayName = "Fonts",
                Entries =
                [
                    DetectFont("font.jetbrains-mono", "JetBrains Mono", fontText),
                    DetectFont("font.nerd-fonts", "Nerd Fonts", fontText, "Nerd Font"),
                    DetectFont("font.cascadia-code", "Cascadia Code", fontText),
                ],
            },
            new()
            {
                Id = "terminals",
                DisplayName = "Terminals",
                Entries =
                [
                    await DetectTerminalAsync("terminal.terminal-app", "Terminal.app", "test -d '/Applications/Utilities/Terminal.app'", cancellationToken),
                    await DetectTerminalAsync("terminal.iterm2", "iTerm2", "test -d '/Applications/iTerm.app'", cancellationToken),
                    await DetectTerminalAsync("terminal.wezterm", "WezTerm", "command -v wezterm", cancellationToken),
                ],
            },
            new()
            {
                Id = "containers",
                DisplayName = "Container runtime",
                Entries =
                [
                    await DetectTerminalAsync("container.docker", "Docker", "command -v docker", cancellationToken),
                    await DetectTerminalAsync("container.podman", "Podman", "command -v podman", cancellationToken),
                ],
            },
            new()
            {
                Id = "tools",
                DisplayName = "Tools",
                Entries =
                [
                    await DetectTerminalAsync("tool.git", "Git", "command -v git", cancellationToken),
                    new HostCapabilityEntry
                    {
                        Id = "terminal.profile-support",
                        DisplayName = "Managed terminal profile support",
                        Status = HostCapabilityStatus.Unavailable,
                        Summary = "Managed Windows Terminal profiles are not available on macOS.",
                    },
                ],
            },
            new()
            {
                Id = "package-managers",
                DisplayName = "Package managers",
                Entries =
                [
                    await DetectTerminalAsync("package.brew", "Homebrew", "command -v brew", cancellationToken),
                ],
            },
        };

        return new HostCapabilityReport
        {
            Platform = Platform,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            Sections = sections,
        };
    }

    private static HostCapabilityEntry DetectFont(string id, string displayName, string fontInventory, string? alternateMatch = null)
    {
        var match = displayName;
        var found = !string.IsNullOrWhiteSpace(fontInventory)
            && (fontInventory.Contains(match, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(alternateMatch) && fontInventory.Contains(alternateMatch, StringComparison.OrdinalIgnoreCase)));

        return new HostCapabilityEntry
        {
            Id = id,
            DisplayName = displayName,
            Status = found ? HostCapabilityStatus.Available : HostCapabilityStatus.Unavailable,
            Summary = found ? $"{displayName} is available." : $"{displayName} was not detected.",
        };
    }

    private async Task<HostCapabilityEntry> DetectTerminalAsync(string id, string displayName, string shellCheck, CancellationToken cancellationToken)
    {
        var result = await _commandProbe.RunAsync("sh", ["-lc", shellCheck], cancellationToken);
        return new HostCapabilityEntry
        {
            Id = id,
            DisplayName = displayName,
            Status = result.IsSuccess ? HostCapabilityStatus.Available : HostCapabilityStatus.Unavailable,
            Summary = result.IsSuccess ? $"{displayName} is available." : $"{displayName} was not detected.",
            Details = result.IsSuccess ? result.StandardOutput.Trim() : string.IsNullOrWhiteSpace(result.FailureMessage) ? result.StandardError : result.FailureMessage,
        };
    }
}
