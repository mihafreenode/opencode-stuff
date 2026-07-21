using System.Diagnostics;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class DesktopPlatformService : IDesktopPlatformService
{
    public Task OpenPathAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("explorer.exe", $"\"{path}\"")
            : OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("open", $"\"{path}\"")
                : new ProcessStartInfo("xdg-open", $"\"{path}\"");

        startInfo.UseShellExecute = false;
        Process.Start(startInfo);
        return Task.CompletedTask;
    }

    public async Task<WorkspaceSourceNavigationResult> OpenSourceLocationAsync(string path, int line, int column, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var gotoTarget = $"{path}:{Math.Max(1, line)}:{Math.Max(1, column)}";
                var startInfo = new ProcessStartInfo("cmd.exe", $"/c code --goto \"{gotoTarget}\"") { UseShellExecute = false, CreateNoWindow = true };
                using var process = Process.Start(startInfo);
                if (process is not null)
                {
                    return new WorkspaceSourceNavigationResult { Message = $"Opened {gotoTarget}.", UsedFallback = false };
                }
            }
            catch
            {
            }
        }

        await OpenPathAsync(path, cancellationToken);
        return new WorkspaceSourceNavigationResult
        {
            Message = $"Opened '{path}'. Requested location: line {line}, column {column}.",
            UsedFallback = true,
        };
    }
}
