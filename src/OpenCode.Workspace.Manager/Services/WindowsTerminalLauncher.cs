using System.IO;
using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Manager.Services;

/// <summary>
/// Windows Terminal launching is intentionally isolated here because it is one of
/// the few Windows-only runtime concerns in the MVP. The attach command itself is
/// simple and transparent so contributors can reason about it without knowing WPF
/// internals.
/// </summary>
public sealed class WindowsTerminalLauncher : ITerminalLauncher
{
    private readonly AttachCommandBuilder _attachCommandBuilder;

    public WindowsTerminalLauncher(AttachCommandBuilder attachCommandBuilder)
    {
        _attachCommandBuilder = attachCommandBuilder;
    }

    public async Task LaunchAttachSessionAsync(WorkspaceSnapshot snapshot, Action<CommandLogEntry>? log = null, CancellationToken cancellationToken = default)
    {
        var command = _attachCommandBuilder.Build(snapshot);
        var attachPrefix = GetAttachPrefix(snapshot);

        if (!File.Exists(snapshot.Paths.AttachWrapperScriptPath))
        {
            throw new InvalidOperationException($"The attach wrapper file is missing. Regenerate the workspace artifacts and try again.{Environment.NewLine}Expected file: {snapshot.Paths.AttachWrapperScriptPath}");
        }

        TryDeleteAttachDiagnosticsLog(snapshot.Paths.AttachDiagnosticsLogPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            UseShellExecute = true,
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = $"{attachPrefix} Selected workspace '{snapshot.Definition.Workspace.Name}'.",
            });
            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = $"{attachPrefix} Windows Terminal executable: {startInfo.FileName}",
            });
            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = $"{attachPrefix} Launching Windows Terminal command: {command.CommandText}",
            });

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };

            process.Start();
            if (process is null)
            {
                throw new InvalidOperationException("Windows Terminal did not return a process handle.");
            }

            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = $"{attachPrefix} Windows Terminal process id: {process.Id}",
            });

            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = $"{attachPrefix} Windows Terminal launch command accepted.",
            });

            await Task.Delay(1500, cancellationToken);
            await MirrorAttachDiagnosticsAsync(snapshot.Paths.AttachDiagnosticsLogPath, log, cancellationToken);

            var assessment = AssessLaunchOutcome(attachPrefix, command.CommandText, process.HasExited, process.HasExited ? process.ExitCode : 0);
            foreach (var message in assessment.Messages)
            {
                log?.Invoke(new CommandLogEntry
                {
                    Source = "app",
                    Message = message,
                });
            }

            if (assessment.ExitedEarly)
            {
                await MirrorAttachDiagnosticsAsync(snapshot.Paths.AttachDiagnosticsLogPath, log, cancellationToken);
                throw new InvalidOperationException("Windows Terminal exited before the attach session became interactive. See terminal output in the log panel for details.");
            }

            _ = process;
            return;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 2 || exception.NativeErrorCode == 3)
        {
            throw new InvalidOperationException("Windows Terminal is not available. Install it or enable its App Execution Alias.", exception);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Windows Terminal launch failed. See the log panel for the exact command and terminal output.", exception);
        }
    }

    private static IEnumerable<string> SplitLines(string content)
    {
        return content
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd());
    }

    internal static async Task MirrorAttachDiagnosticsAsync(string attachDiagnosticsLogPath, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        if (log is null || string.IsNullOrWhiteSpace(attachDiagnosticsLogPath))
        {
            return;
        }

        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(attachDiagnosticsLogPath))
            {
                foreach (var line in ReadAttachDiagnosticLines(attachDiagnosticsLogPath))
                {
                    log(new CommandLogEntry
                    {
                        Source = "attach",
                        Message = line,
                    });
                }

                return;
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    internal static IReadOnlyList<string> ReadAttachDiagnosticLines(string attachDiagnosticsLogPath)
    {
        if (!File.Exists(attachDiagnosticsLogPath))
        {
            return Array.Empty<string>();
        }

        return SplitLines(File.ReadAllText(attachDiagnosticsLogPath))
            .Where(line => line.Contains("[attach:", StringComparison.Ordinal)
                || line.Contains("[attach] Failed at line", StringComparison.Ordinal)
                || line.StartsWith("+ ", StringComparison.Ordinal)
                || line.StartsWith("++ ", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static void TryDeleteAttachDiagnosticsLog(string attachDiagnosticsLogPath)
    {
        try
        {
            if (File.Exists(attachDiagnosticsLogPath))
            {
                File.Delete(attachDiagnosticsLogPath);
            }
        }
        catch
        {
        }
    }

    private static string GetAttachPrefix(WorkspaceSnapshot snapshot)
        => $"[attach:{snapshot.Definition.Workspace.Name}]";

    internal static TerminalLaunchAssessment AssessLaunchOutcome(string attachPrefix, string commandText, bool hasExited, int exitCode)
    {
        if (hasExited)
        {
            return new TerminalLaunchAssessment(
                ExitedEarly: true,
                Messages:
                [
                    $"{attachPrefix} Windows Terminal exited before handoff completed with code {exitCode}.",
                    $"{attachPrefix} Windows Terminal command: {commandText}",
                ]);
        }

        return new TerminalLaunchAssessment(
            ExitedEarly: false,
            Messages:
            [
                $"{attachPrefix} Terminal window handoff completed.",
            ]);
    }

    internal sealed record TerminalLaunchAssessment(bool ExitedEarly, IReadOnlyList<string> Messages);
}
