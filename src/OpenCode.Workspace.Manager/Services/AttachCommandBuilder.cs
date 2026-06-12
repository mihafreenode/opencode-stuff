using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Manager.Services;

/// <summary>
/// Builds the attach command in one place so both the launcher and tests can
/// validate the exact handoff contract without scraping WPF view model code.
/// </summary>
public sealed class AttachCommandBuilder
{
    public WindowsTerminalCommand Build(WorkspaceSnapshot snapshot)
    {
        return new WindowsTerminalCommand
        {
            Title = snapshot.Definition.Workspace.Name,
            FileName = "wt.exe",
            Arguments =
            [
                "powershell.exe",
                "-NoExit",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                snapshot.Paths.AttachWrapperScriptPath,
            ],
            CommandText = $"wt.exe powershell.exe -NoExit -ExecutionPolicy Bypass -File \"{snapshot.Paths.AttachWrapperScriptPath}\"",
        };
    }
}

public sealed class WindowsTerminalCommand
{
    public required string Title { get; init; }
    public required string FileName { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public required string CommandText { get; init; }
}
