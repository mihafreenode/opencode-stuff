using System.Drawing.Text;
using System.IO;
using System.Runtime.Versioning;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Platform;

namespace OpenCode.Workspace.Platform.Windows;

/// <summary>
/// Groups Windows-specific host capability checks that are useful both for the UI
/// and for explicit Windows integration tests.
/// </summary>
public interface IWindowsHostCapabilities
{
    Task<PrerequisiteCheckResult> CheckWindowsTerminalAsync(CancellationToken cancellationToken = default);
    string ResolvePreferredTerminalFace(string fontDisplayName);
}

public sealed class WindowsHostCapabilities : IWindowsHostCapabilities, IHostCapabilities
{
    private readonly ICommandProbe _commandProbe;

    public WindowsHostCapabilities(ProcessRunner processRunner)
        : this(new ProcessRunnerCommandProbe(processRunner))
    {
    }

    public WindowsHostCapabilities(ICommandProbe commandProbe)
    {
        _commandProbe = commandProbe;
    }

    public PlatformKind Platform => PlatformKind.Windows;

    public async Task<HostCapabilityReport> DetectAsync(CancellationToken cancellationToken = default)
    {
        var windowsTerminal = await CheckWindowsTerminalAsync(cancellationToken);
        var dockerDesktop = await CheckDockerDesktopAsync(cancellationToken);
        var wslAvailable = await IsWslAvailableAsync(cancellationToken);

        var sections = new List<HostCapabilitySection>
        {
            new()
            {
                Id = "fonts",
                DisplayName = "Fonts",
                Entries =
                [
                    DetectWindowsFont("font.jetbrains-mono", "JetBrains Mono", "JetBrainsMono Nerd Font"),
                    DetectWindowsFont("font.nerd-fonts", "Nerd Fonts", "JetBrainsMono Nerd Font"),
                    DetectWindowsFont("font.cascadia-code", "Cascadia Code", "Cascadia Code"),
                ],
            },
            new()
            {
                Id = "terminals",
                DisplayName = "Terminals",
                Entries =
                [
                    ToCapabilityEntry("terminal.windows-terminal", "Windows Terminal", windowsTerminal),
                ],
            },
            new()
            {
                Id = "containers",
                DisplayName = "Container runtime",
                Entries =
                [
                    ToCapabilityEntry("container.docker", "Docker Desktop", dockerDesktop),
                    await DetectDockerComposeCapabilityAsync(cancellationToken),
                    await DetectCommandCapabilityAsync("container.podman", "Podman", "where", ["podman"], cancellationToken),
                ],
            },
            new()
            {
                Id = "tools",
                DisplayName = "Tools",
                Entries =
                [
                    await DetectCommandCapabilityAsync("tool.git", "Git", "where", ["git"], cancellationToken),
                    await DetectCommandCapabilityAsync("tool.opencode-cli", "OpenCode CLI", "where", ["opencode"], cancellationToken),
                    new HostCapabilityEntry
                    {
                        Id = "tool.wsl",
                        DisplayName = "WSL",
                        Status = wslAvailable ? HostCapabilityStatus.Available : HostCapabilityStatus.Unavailable,
                        Summary = wslAvailable ? "WSL is available." : "WSL was not detected.",
                    },
                    new HostCapabilityEntry
                    {
                        Id = "terminal.profile-support",
                        DisplayName = "Windows Terminal profile support",
                        Status = windowsTerminal.IsAvailable ? HostCapabilityStatus.Available : HostCapabilityStatus.Unavailable,
                        Summary = windowsTerminal.IsAvailable
                            ? "Managed Windows Terminal profiles are supported."
                            : "Managed Windows Terminal profiles are unavailable until Windows Terminal is installed.",
                    },
                ],
            },
            new()
            {
                Id = "package-managers",
                DisplayName = "Package managers",
                Entries =
                [
                    await DetectCommandCapabilityAsync("package.winget", "winget", "where", ["winget"], cancellationToken),
                ],
            },
        };

        return new HostCapabilityReport
        {
            Platform = Platform,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            Sections = sections,
        };
    }

    public async Task<PrerequisiteCheckResult> CheckDockerDesktopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _commandProbe.RunAsync("cmd.exe", ["/c", "docker", "info"], cancellationToken);
            return result.IsSuccess
                ? PrerequisiteCheckResult.Available("Docker Desktop is reachable.")
                : PrerequisiteCheckResult.Unavailable("Docker Desktop not installed or not running.");
        }
        catch (Exception exception)
        {
            return PrerequisiteCheckResult.Unavailable($"Docker Desktop not installed or not running. {exception.Message}");
        }
    }

    public async Task<PrerequisiteCheckResult> CheckWindowsTerminalAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _commandProbe.RunAsync("cmd.exe", ["/c", "where", "wt"], cancellationToken);
            return result.IsSuccess
                ? PrerequisiteCheckResult.Available("Windows Terminal command is available.")
                : PrerequisiteCheckResult.Unavailable("Windows Terminal not installed or App Execution Alias disabled.");
        }
        catch (Exception exception)
        {
            return PrerequisiteCheckResult.Unavailable($"Windows Terminal not installed or App Execution Alias disabled. {exception.Message}");
        }
    }

    public PrerequisiteCheckResult CheckNerdFont(string fontDisplayName = "JetBrainsMono Nerd Font")
    {
        var definition = NerdFontCatalog.FindByDisplayName(fontDisplayName);
        if (definition is null)
        {
            return PrerequisiteCheckResult.Unavailable($"The selected Nerd Font '{fontDisplayName}' is not recognized by OpenCode Stuff.");
        }

        if (!OperatingSystem.IsWindows())
        {
            return PrerequisiteCheckResult.Unavailable("Windows font inspection is only available on Windows.");
        }

        var found = HasInstalledWindowsFont(definition.CandidateFaceNames);
        if (found)
        {
            return PrerequisiteCheckResult.Available($"Detected configured Nerd Font '{fontDisplayName}'.");
        }

        var userFontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
        var fileMatch = Directory.Exists(userFontPath) && Directory.GetFiles(userFontPath, "*.ttf", SearchOption.TopDirectoryOnly)
            .Any(file => Path.GetFileName(file).Contains(definition.ArchiveName, StringComparison.OrdinalIgnoreCase));

        return fileMatch
            ? PrerequisiteCheckResult.Unavailable($"Font files for '{fontDisplayName}' exist, but Windows has not registered the font family yet. Reinstall or sign out and back in.")
            : PrerequisiteCheckResult.Unavailable($"Nerd Font '{fontDisplayName}' not installed.");
    }

    public string ResolvePreferredTerminalFace(string fontDisplayName)
    {
        var definition = NerdFontCatalog.FindByDisplayName(fontDisplayName);
        if (definition is null)
        {
            return fontDisplayName;
        }

        if (!OperatingSystem.IsWindows())
        {
            return definition.CandidateFaceNames[0];
        }

        foreach (var candidate in definition.CandidateFaceNames)
        {
            if (HasInstalledWindowsFont([candidate]))
            {
                return candidate;
            }
        }

        return definition.CandidateFaceNames[0];
    }

    public string? FindSqlDeveloperExecutablePath()
    {
        foreach (var candidate in EnumerateSqlDeveloperCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSqlDeveloperCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return
        [
            Path.Combine(localAppData, "sqldeveloper", "sqldeveloper.exe"),
            Path.Combine(localAppData, "Programs", "sqldeveloper", "sqldeveloper.exe"),
            Path.Combine(programFiles, "Oracle", "SQL Developer", "sqldeveloper.exe"),
            Path.Combine(programFiles, "Oracle", "sqldeveloper", "sqldeveloper.exe"),
            Path.Combine(programFilesX86, "Oracle", "SQL Developer", "sqldeveloper.exe"),
            Path.Combine(programFilesX86, "Oracle", "sqldeveloper", "sqldeveloper.exe"),
            Path.Combine("C:\\", "sqldeveloper", "sqldeveloper.exe"),
            Path.Combine("C:\\", "sqldeveloper", "sqldeveloper64W.exe"),
        ];
    }

    private async Task<HostCapabilityEntry> DetectCommandCapabilityAsync(string id, string displayName, string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await _commandProbe.RunAsync(fileName, arguments, cancellationToken);
        return new HostCapabilityEntry
        {
            Id = id,
            DisplayName = displayName,
            Status = result.IsSuccess ? HostCapabilityStatus.Available : HostCapabilityStatus.Unavailable,
            Summary = result.IsSuccess ? $"{displayName} is available." : $"{displayName} was not detected.",
            Details = result.IsSuccess ? result.StandardOutput.Trim() : string.IsNullOrWhiteSpace(result.FailureMessage) ? result.StandardError : result.FailureMessage,
        };
    }

    private async Task<HostCapabilityEntry> DetectDockerComposeCapabilityAsync(CancellationToken cancellationToken)
    {
        var result = await _commandProbe.RunAsync("cmd.exe", ["/c", "docker", "compose", "version"], cancellationToken);
        return new HostCapabilityEntry
        {
            Id = "container.docker-compose",
            DisplayName = "Docker Compose",
            Status = result.IsSuccess ? HostCapabilityStatus.Available : HostCapabilityStatus.Unavailable,
            Summary = result.IsSuccess ? "Docker Compose is available through docker compose." : "Docker Compose was not detected through docker compose.",
            Details = result.IsSuccess ? result.StandardOutput.Trim() : string.IsNullOrWhiteSpace(result.FailureMessage) ? result.StandardError : result.FailureMessage,
        };
    }

    private static HostCapabilityEntry ToCapabilityEntry(string id, string displayName, PrerequisiteCheckResult result)
        => new()
        {
            Id = id,
            DisplayName = displayName,
            Status = result.IsAvailable ? HostCapabilityStatus.Available : HostCapabilityStatus.Unavailable,
            Summary = result.Reason,
        };

    private async Task<bool> IsWslAvailableAsync(CancellationToken cancellationToken)
        => (await _commandProbe.RunAsync("cmd.exe", ["/c", "where", "wsl"], cancellationToken)).IsSuccess;

    private HostCapabilityEntry DetectWindowsFont(string id, string displayName, string fontDisplayName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new HostCapabilityEntry
            {
                Id = id,
                DisplayName = displayName,
                Status = HostCapabilityStatus.Unavailable,
                Summary = "Windows font inspection is only available on Windows.",
            };
        }

        if (NerdFontCatalog.FindByDisplayName(fontDisplayName) is null)
        {
            var installed = HasInstalledWindowsFont([fontDisplayName]);
            return new HostCapabilityEntry
            {
                Id = id,
                DisplayName = displayName,
                Status = installed ? HostCapabilityStatus.Available : HostCapabilityStatus.Unavailable,
                Summary = installed ? $"{displayName} is available." : $"{displayName} was not detected.",
            };
        }

        var result = CheckNerdFont(fontDisplayName);
        return new HostCapabilityEntry
        {
            Id = id,
            DisplayName = displayName,
            Status = result.IsAvailable ? HostCapabilityStatus.Available : HostCapabilityStatus.Unavailable,
            Summary = result.Reason,
        };
    }

    [SupportedOSPlatform("windows")]
    private static bool HasInstalledWindowsFont(IReadOnlyList<string> candidateFaceNames)
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(family => candidateFaceNames.Any(candidate => string.Equals(family.Name, candidate, StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed class PrerequisiteCheckResult
{
    public bool IsAvailable { get; init; }
    public required string Reason { get; init; }

    public static PrerequisiteCheckResult Available(string reason) => new() { IsAvailable = true, Reason = reason };
    public static PrerequisiteCheckResult Unavailable(string reason) => new() { IsAvailable = false, Reason = reason };
}
