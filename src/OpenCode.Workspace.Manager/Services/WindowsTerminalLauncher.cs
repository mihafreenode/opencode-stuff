using System.IO;
using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using System.Linq;
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
        var processStartSucceeded = false;
        var transcriptStarted = false;
        var launcherProcessId = 0;

        if (!File.Exists(snapshot.Paths.AttachWrapperScriptPath))
        {
            throw new InvalidOperationException($"The attach wrapper file is missing. Regenerate the workspace artifacts and try again.{Environment.NewLine}Expected file: {snapshot.Paths.AttachWrapperScriptPath}");
        }

        TryDeleteAttachDiagnosticsLog(snapshot.Paths.AttachDiagnosticsLogPath);

        var startInfo = CreateStartInfo(command);

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
                Message = $"{attachPrefix} Windows Terminal UseShellExecute: {startInfo.UseShellExecute}",
            });
            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = $"{attachPrefix} Windows Terminal uses ArgumentList: {startInfo.ArgumentList.Count > 0}",
            });
            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = $"{attachPrefix} Windows Terminal Arguments length: {startInfo.Arguments.Length}",
            });
            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = $"{attachPrefix} Windows Terminal Arguments value: {startInfo.Arguments}",
            });
            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = $"{attachPrefix} Windows Terminal working directory: {startInfo.WorkingDirectory}",
            });
            for (var index = 0; index < startInfo.ArgumentList.Count; index++)
            {
                var argument = startInfo.ArgumentList[index];
                log?.Invoke(new CommandLogEntry
                {
                    Source = "app",
                    Message = $"{attachPrefix} Windows Terminal ArgumentList[{index}]: {argument}",
                });
            }
            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = $"{attachPrefix} Launching Windows Terminal command: {command.CommandText}",
            });
            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = $"{attachPrefix} PowerShell fallback command: {command.FallbackCommandText}",
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

            processStartSucceeded = true;
            launcherProcessId = process.Id;

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
            var transcriptLines = await MirrorAttachDiagnosticsAsync(snapshot.Paths.AttachDiagnosticsLogPath, log, cancellationToken);
            transcriptStarted = transcriptLines.Count > 0;

            var assessment = AssessLaunchOutcome(attachPrefix, command.CommandText, command.FallbackCommandText, process.Id, process.HasExited, process.HasExited ? process.ExitCode : 0, transcriptLines);
            foreach (var message in assessment.Messages)
            {
                log?.Invoke(new CommandLogEntry
                {
                    Source = "app",
                    Message = message,
                });
            }

            if (assessment.Failed)
            {
                await MirrorAttachDiagnosticsAsync(snapshot.Paths.AttachDiagnosticsLogPath, log, cancellationToken);
                throw new InvalidOperationException("Attach transcript reported a terminal attach failure. See the log panel for details.");
            }

            _ = process;
            return;
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
                    log?.Invoke(new CommandLogEntry
                    {
                        Source = "app",
                        Message = message,
                    });
                }
                return;
            }

            throw new InvalidOperationException("Windows Terminal launch failed. See the log panel for the exact command and terminal output.", exception);
        }
    }

    private static IEnumerable<string> SplitLines(string content)
    {
        return content
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd());
    }

    internal static async Task<IReadOnlyList<string>> MirrorAttachDiagnosticsAsync(string attachDiagnosticsLogPath, Action<CommandLogEntry>? log, CancellationToken cancellationToken)
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
                    log(new CommandLogEntry
                    {
                        Source = "attach",
                        Message = line,
                    });
                }

                return lines;
            }

            await Task.Delay(250, cancellationToken);
        }

        return Array.Empty<string>();
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

    internal static ProcessStartInfo CreateStartInfo(WindowsTerminalCommand command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveWindowsTerminalExecutablePath(command.FileName),
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory,
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    internal static string ResolveWindowsTerminalExecutablePath(string fileName)
    {
        if (!string.Equals(fileName, "wt.exe", StringComparison.OrdinalIgnoreCase))
        {
            return fileName;
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.StartInfo.ArgumentList.Add(fileName);
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);

            var resolvedPath = output
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(path => path.EndsWith("wt.exe", StringComparison.OrdinalIgnoreCase));

            return string.IsNullOrWhiteSpace(resolvedPath) ? fileName : resolvedPath.Trim();
        }
        catch
        {
            return fileName;
        }
    }

    internal static TerminalLaunchAssessment AssessLaunchOutcome(string attachPrefix, string commandText, string fallbackCommandText, int processId, bool hasExited, int exitCode, IReadOnlyList<string> transcriptLines)
    {
        var transcriptStarted = transcriptLines.Count > 0;
        var transcriptReportedFailure = transcriptLines.Any(IsAttachFailureLine);

        if (transcriptReportedFailure)
        {
            return new TerminalLaunchAssessment(
                Failed: true,
                Messages:
                [
                    $"{attachPrefix} Attach transcript reported failure.",
                    $"{attachPrefix} Windows Terminal process id: {processId}",
                    $"{attachPrefix} Windows Terminal command: {commandText}",
                    $"{attachPrefix} PowerShell fallback command: {fallbackCommandText}",
                ]);
        }

        if (hasExited)
        {
            if (transcriptStarted)
            {
                return new TerminalLaunchAssessment(
                    Failed: false,
                    Messages:
                    [
                        $"{attachPrefix} Windows Terminal launcher process exited after handoff; attach transcript will be authoritative.",
                        $"{attachPrefix} Windows Terminal process id: {processId}",
                        $"{attachPrefix} Windows Terminal launcher exit code: {exitCode}",
                    ]);
            }

            return new TerminalLaunchAssessment(
                Failed: false,
                Messages:
                [
                    $"{attachPrefix} Windows Terminal launch accepted; attach transcript is authoritative.",
                    $"{attachPrefix} Windows Terminal process id: {processId}",
                    $"{attachPrefix} Windows Terminal launcher exit code: {exitCode}",
                    $"{attachPrefix} Windows Terminal command: {commandText}",
                    $"{attachPrefix} PowerShell fallback command: {fallbackCommandText}",
                ]);
        }

        return new TerminalLaunchAssessment(
            Failed: false,
            Messages:
            [
                $"{attachPrefix} Windows Terminal launch accepted; attach transcript is authoritative.",
                $"{attachPrefix} Windows Terminal process id: {processId}",
            ]);
    }

    private static bool IsAttachFailureLine(string line)
        => line.Contains("docker exec failed", StringComparison.Ordinal)
            || line.Contains("Failed at line", StringComparison.Ordinal)
            || line.Contains("Script not found", StringComparison.Ordinal)
            || line.Contains("Script is not marked executable", StringComparison.Ordinal)
            || line.Contains("does not exist", StringComparison.Ordinal)
            || line.Contains("Working directory missing", StringComparison.Ordinal)
            || line.Contains("Root cause:", StringComparison.Ordinal);

    internal static IReadOnlyList<string> CreatePostStartWarningMessages(string attachPrefix)
        =>
        [
            $"{attachPrefix} Windows Terminal launch accepted; attach transcript is authoritative.",
            $"{attachPrefix} Post-start launcher verification raised a warning; attach will not be marked failed unless the transcript reports failure.",
        ];

    private static void LogLaunchException(Action<CommandLogEntry>? log, string attachPrefix, string methodName, bool processStartSucceeded, bool transcriptStarted, int launcherProcessId, Exception exception)
    {
        if (log is null)
        {
            return;
        }

        log(new CommandLogEntry
        {
            Source = "app",
            Message = $"{attachPrefix} Launcher method: {methodName}",
        });
        log(new CommandLogEntry
        {
            Source = "app",
            Message = $"{attachPrefix} Launcher process start succeeded: {processStartSucceeded}",
        });
        log(new CommandLogEntry
        {
            Source = "app",
            Message = $"{attachPrefix} Launcher transcript started: {transcriptStarted}",
        });
        log(new CommandLogEntry
        {
            Source = "app",
            Message = $"{attachPrefix} Launcher process id at exception: {launcherProcessId}",
        });
        log(new CommandLogEntry
        {
            Source = "app",
            Message = $"{attachPrefix} Launcher exception: {exception.GetType().Name}: {exception.Message}",
        });
    }

    internal sealed record TerminalLaunchAssessment(bool Failed, IReadOnlyList<string> Messages);
}
