using System.Runtime.InteropServices;
using OpenCode.Workspace.Platform;

namespace OpenCode.Workspace.Platform.Linux;

public sealed class LinuxHostCapabilities : IHostCapabilities
{
    private readonly ICommandProbe _commandProbe;

    public LinuxHostCapabilities(ICommandProbe commandProbe)
    {
        _commandProbe = commandProbe;
    }

    public PlatformKind Platform => PlatformKind.Linux;

    public async Task<HostCapabilityReport> DetectAsync(CancellationToken cancellationToken = default)
    {
        var sections = new List<HostCapabilitySection>
        {
            new()
            {
                Id = "fonts",
                DisplayName = "Fonts",
                Entries =
                [
                    await DetectFontAsync("font.jetbrains-mono", "JetBrains Mono", "JetBrains Mono", cancellationToken),
                    await DetectFontAsync("font.nerd-fonts", "Nerd Fonts", "JetBrainsMono Nerd Font", cancellationToken),
                    await DetectFontAsync("font.cascadia-code", "Cascadia Code", "Cascadia Code", cancellationToken),
                ],
            },
            new()
            {
                Id = "terminals",
                DisplayName = "Terminals",
                Entries =
                [
                    await DetectCommandAsync("terminal.gnome", "GNOME Terminal", "gnome-terminal", cancellationToken),
                    await DetectCommandAsync("terminal.konsole", "Konsole", "konsole", cancellationToken),
                    await DetectCommandAsync("terminal.xfce4", "Xfce Terminal", "xfce4-terminal", cancellationToken),
                    await DetectCommandAsync("terminal.kitty", "Kitty", "kitty", cancellationToken),
                    await DetectCommandAsync("terminal.wezterm", "WezTerm", "wezterm", cancellationToken),
                    await DetectCommandAsync("terminal.xterm", "xterm", "xterm", cancellationToken),
                ],
            },
            new()
            {
                Id = "containers",
                DisplayName = "Container runtime",
                Entries =
                [
                    await DetectCommandAsync("container.docker", "Docker", "docker", cancellationToken),
                    await DetectCommandAsync("container.podman", "Podman", "podman", cancellationToken),
                ],
            },
            new()
            {
                Id = "tools",
                DisplayName = "Tools",
                Entries =
                [
                    await DetectCommandAsync("tool.git", "Git", "git", cancellationToken),
                    new HostCapabilityEntry
                    {
                        Id = "terminal.profile-support",
                        DisplayName = "Managed terminal profile support",
                        Status = HostCapabilityStatus.Unavailable,
                        Summary = "Managed Windows Terminal profiles are not available on Linux.",
                    },
                ],
            },
            new()
            {
                Id = "package-managers",
                DisplayName = "Package managers",
                Entries =
                [
                    await DetectCommandAsync("package.apt", "apt", "apt", cancellationToken),
                    await DetectCommandAsync("package.dnf", "dnf", "dnf", cancellationToken),
                    await DetectCommandAsync("package.pacman", "pacman", "pacman", cancellationToken),
                    await DetectCommandAsync("package.zypper", "zypper", "zypper", cancellationToken),
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

    private async Task<HostCapabilityEntry> DetectFontAsync(string id, string displayName, string fontMatchName, CancellationToken cancellationToken)
    {
        var result = await _commandProbe.RunAsync("fc-match", [fontMatchName], cancellationToken);
        if (!result.IsSuccess)
        {
            return new HostCapabilityEntry
            {
                Id = id,
                DisplayName = displayName,
                Status = HostCapabilityStatus.Unavailable,
                Summary = $"{displayName} was not detected.",
                Details = string.IsNullOrWhiteSpace(result.FailureMessage) ? result.StandardError : result.FailureMessage,
            };
        }

        var detected = result.StandardOutput.Trim();
        return new HostCapabilityEntry
        {
            Id = id,
            DisplayName = displayName,
            Status = detected.Contains(fontMatchName, StringComparison.OrdinalIgnoreCase) || detected.Contains(displayName, StringComparison.OrdinalIgnoreCase)
                ? HostCapabilityStatus.Available
                : HostCapabilityStatus.Warning,
            Summary = detected.Contains(fontMatchName, StringComparison.OrdinalIgnoreCase) || detected.Contains(displayName, StringComparison.OrdinalIgnoreCase)
                ? $"{displayName} is available."
                : $"Fontconfig resolved '{fontMatchName}' to '{detected}'.",
            Details = detected,
        };
    }

    private async Task<HostCapabilityEntry> DetectCommandAsync(string id, string displayName, string commandName, CancellationToken cancellationToken)
    {
        var result = await _commandProbe.RunAsync("sh", ["-lc", $"command -v {commandName}"], cancellationToken);
        return new HostCapabilityEntry
        {
            Id = id,
            DisplayName = displayName,
            Status = result.IsSuccess ? HostCapabilityStatus.Available : HostCapabilityStatus.Unavailable,
            Summary = result.IsSuccess ? $"{displayName} is available." : $"{displayName} was not found on PATH.",
            Details = result.IsSuccess ? result.StandardOutput.Trim() : string.IsNullOrWhiteSpace(result.FailureMessage) ? result.StandardError : result.FailureMessage,
        };
    }
}
