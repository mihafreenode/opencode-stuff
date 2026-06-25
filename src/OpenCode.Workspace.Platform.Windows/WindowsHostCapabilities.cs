using System.Drawing.Text;
using System.IO;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Platform.Windows;

/// <summary>
/// Groups Windows-specific host capability checks that are useful both for the UI
/// and for explicit Windows integration tests.
/// </summary>
public sealed class WindowsHostCapabilities
{
    private readonly ProcessRunner _processRunner;

    public WindowsHostCapabilities(ProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<PrerequisiteCheckResult> CheckDockerDesktopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _processRunner.RunAsync("cmd.exe", ["/c", "docker", "info"], cancellationToken: cancellationToken);
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
            var result = await _processRunner.RunAsync("cmd.exe", ["/c", "where", "wt"], cancellationToken: cancellationToken);
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

        using var fonts = new InstalledFontCollection();
        var found = fonts.Families.Any(family => definition.CandidateFaceNames.Any(candidate => string.Equals(family.Name, candidate, StringComparison.OrdinalIgnoreCase)));
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

        using var fonts = new InstalledFontCollection();
        foreach (var candidate in definition.CandidateFaceNames)
        {
            if (fonts.Families.Any(family => string.Equals(family.Name, candidate, StringComparison.OrdinalIgnoreCase)))
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
}

public sealed class PrerequisiteCheckResult
{
    public bool IsAvailable { get; init; }
    public required string Reason { get; init; }

    public static PrerequisiteCheckResult Available(string reason) => new() { IsAvailable = true, Reason = reason };
    public static PrerequisiteCheckResult Unavailable(string reason) => new() { IsAvailable = false, Reason = reason };
}
