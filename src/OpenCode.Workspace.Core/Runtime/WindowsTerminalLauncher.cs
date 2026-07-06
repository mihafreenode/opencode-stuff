using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Runtime;

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
        var processStartSucceeded = false;
        var transcriptStarted = false;
        var launcherProcessId = 0;

        if (!File.Exists(snapshot.Paths.AttachWrapperScriptPath))
        {
            throw new InvalidOperationException($"The attach wrapper file is missing. Regenerate the workspace artifacts and try again.{Environment.NewLine}Expected file: {snapshot.Paths.AttachWrapperScriptPath}");
        }

        TryDeleteAttachDiagnosticsLog(snapshot.Paths.AttachDiagnosticsLogPath, log, attachPrefix);

        var startInfo = CreateStartInfo(command);

        try
        {
            log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{attachPrefix} Selected workspace '{snapshot.Definition.Workspace.Name}'." });
            log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{attachPrefix} Windows Terminal executable: {startInfo.FileName}" });
            log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{attachPrefix} Windows Terminal UseShellExecute: {startInfo.UseShellExecute}" });
            log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{attachPrefix} Windows Terminal uses ArgumentList: {startInfo.ArgumentList.Count > 0}" });
            log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{attachPrefix} Windows Terminal Arguments length: {startInfo.Arguments.Length}" });
            log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{attachPrefix} Windows Terminal Arguments value: {startInfo.Arguments}" });
            log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{attachPrefix} Windows Terminal working directory: {startInfo.WorkingDirectory}" });
            for (var index = 0; index < startInfo.ArgumentList.Count; index++)
            {
                var argument = startInfo.ArgumentList[index];
                log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{attachPrefix} Windows Terminal ArgumentList[{index}]: {argument}" });
            }
            log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{attachPrefix} Launching Windows Terminal command: {command.CommandText}" });
            log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{attachPrefix} PowerShell fallback command: {command.FallbackCommandText}" });

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.Start();
            processStartSucceeded = true;
            launcherProcessId = process.Id;

            log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{attachPrefix} Windows Terminal process id: {process.Id}" });
            log?.Invoke(new CommandLogEntry { Source = "app", Message = $"{attachPrefix} Windows Terminal launch command accepted." });

            await Task.Delay(1500, cancellationToken);
            var transcriptLines = await MirrorAttachDiagnosticsAsync(snapshot.Paths.AttachDiagnosticsLogPath, log, cancellationToken);
            transcriptStarted = transcriptLines.Count > 0;

            var assessment = AssessLaunchOutcome(attachPrefix, command.CommandText, command.FallbackCommandText, process.Id, process.HasExited, process.HasExited ? process.ExitCode : 0, transcriptLines);
            foreach (var message in assessment.Messages)
            {
                log?.Invoke(new CommandLogEntry { Source = "app", Message = message });
            }

            if (assessment.Failed)
            {
                await MirrorAttachDiagnosticsAsync(snapshot.Paths.AttachDiagnosticsLogPath, log, cancellationToken);
                throw new InvalidOperationException("Attach transcript reported a terminal attach failure. See the log panel for details.");
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 2 || exception.NativeErrorCode == 3)
        {
            throw new InvalidOperationException("Windows Terminal is not available. Install it or enable its App Execution Alias.", exception);
        }
        catch (InvalidOperationException exception)
        {
            LogLaunchException(log, attachPrefix, "LaunchAttachSessionAsync", processStartSucceeded, transcriptStarted, launcherProcessId, exception);
            throw;
        }
        catch (Exception exception)
        {
            LogLaunchException(log, attachPrefix, "LaunchAttachSessionAsync", processStartSucceeded, transcriptStarted, launcherProcessId, exception);

            if (processStartSucceeded)
            {
                foreach (var message in CreatePostStartWarningMessages(attachPrefix))
                {
                    log?.Invoke(new CommandLogEntry { Source = "app", Message = message });
                }

                return;
            }

            throw new InvalidOperationException("Windows Terminal launch failed. See the log panel for the exact command and terminal output.", exception);
        }
    }

    public static ProcessStartInfo CreateStartInfo(WindowsTerminalCommand command)
    {
        var startInfo = new ProcessStartInfo(command.FileName)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    public static async Task<IReadOnlyList<string>> MirrorAttachDiagnosticsAsync(string attachDiagnosticsLogPath, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
    {
        if (log is null || string.IsNullOrWhiteSpace(attachDiagnosticsLogPath))
        {
            return Array.Empty<string>();
        }

        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(attachDiagnosticsLogPath))
            {
                var lines = ReadAttachDiagnosticLines(attachDiagnosticsLogPath);
                foreach (var line in lines)
                {
                    log(new CommandLogEntry { Source = "attach", Message = line });
                }

                return lines;
            }

            await Task.Delay(250, cancellationToken);
        }

        return Array.Empty<string>();
    }

    public static IReadOnlyList<string> ReadAttachDiagnosticLines(string attachDiagnosticsLogPath)
    {
        var content = File.ReadAllText(attachDiagnosticsLogPath, Encoding.UTF8);
        return content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd()).ToList();
    }

    public static (bool Failed, IReadOnlyList<string> Messages) AssessLaunchOutcome(string attachPrefix, string commandText, string fallbackCommandText, int processId, bool hasExited, int exitCode, IReadOnlyList<string> transcriptLines)
    {
        var messages = new List<string>();
        if (!hasExited)
        {
            messages.Add($"{attachPrefix} Windows Terminal handoff is running.");
            return (false, messages);
        }

        messages.Add($"{attachPrefix} Windows Terminal exited early with code {exitCode}.");
        messages.Add($"{attachPrefix} Windows Terminal process id: {processId}");
        messages.Add($"{attachPrefix} Launch command: {commandText}");
        messages.Add($"{attachPrefix} PowerShell fallback command: {fallbackCommandText}");

        var transcriptReportedFailure = transcriptLines.Any(IsAttachFailureLine);
        if (transcriptLines.Count > 0 && !transcriptReportedFailure)
        {
            messages.Add($"{attachPrefix} Windows Terminal launcher process exited after handoff; attach transcript will be authoritative.");
            messages.Add($"{attachPrefix} Attach transcript started successfully before the launcher exited.");
            return (false, messages);
        }

        messages.Add($"{attachPrefix} Attach transcript reported failure.");
        return (true, messages);
    }

    public static IEnumerable<string> CreatePostStartWarningMessages(string attachPrefix)
    {
        yield return $"{attachPrefix} Windows Terminal launch accepted; attach transcript is authoritative.";
        yield return $"{attachPrefix} Post-start launcher verification raised a warning.";
    }

    private static void TryDeleteAttachDiagnosticsLog(string attachDiagnosticsLogPath, Action<CommandLogEntry>? log = null, string attachPrefix = "[attach]")
    {
        if (string.IsNullOrWhiteSpace(attachDiagnosticsLogPath) || !File.Exists(attachDiagnosticsLogPath))
        {
            return;
        }

        try
        {
            File.Delete(attachDiagnosticsLogPath);
        }
        catch (IOException exception)
        {
            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = $"{attachPrefix} Attach diagnostics log is locked and will be preserved: {exception.Message}",
            });
        }
        catch (UnauthorizedAccessException exception)
        {
            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = $"{attachPrefix} Attach diagnostics log could not be replaced and will be preserved: {exception.Message}",
            });
        }
    }

    private static string GetAttachPrefix(WorkspaceSnapshot snapshot)
        => $"[attach:{snapshot.Definition.Workspace.Name}]";

    private static bool IsAttachFailureLine(string line)
        => line.Contains("launch failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("attach failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("terminal attach failure", StringComparison.OrdinalIgnoreCase);

    private static void LogLaunchException(Action<CommandLogEntry>? log, string attachPrefix, string operationName, bool processStartSucceeded, bool transcriptStarted, int launcherProcessId, Exception exception)
    {
        log?.Invoke(new CommandLogEntry
        {
            Source = "app",
            Message = $"{attachPrefix} {operationName} exception. processStartSucceeded={processStartSucceeded}; transcriptStarted={transcriptStarted}; launcherProcessId={launcherProcessId}; {exception.GetType().Name}: {exception.Message}",
        });
    }
}
