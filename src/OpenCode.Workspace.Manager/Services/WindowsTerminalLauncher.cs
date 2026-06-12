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

        if (!File.Exists(snapshot.Paths.AttachWrapperScriptPath))
        {
            throw new InvalidOperationException($"The attach wrapper file is missing. Regenerate the workspace artifacts and try again.{Environment.NewLine}Expected file: {snapshot.Paths.AttachWrapperScriptPath}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
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
                Message = $"Windows Terminal executable: {startInfo.FileName}",
            });
            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = $"Launching Windows Terminal command: {command.CommandText}",
            });

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };

            var standardOutput = new StringBuilder();
            var standardError = new StringBuilder();
            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                {
                    standardOutput.AppendLine(eventArgs.Data);
                }
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                {
                    standardError.AppendLine(eventArgs.Data);
                }
            };

            process.Start();
            if (process is null)
            {
                throw new InvalidOperationException("Windows Terminal did not return a process handle.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = $"Windows Terminal process id: {process.Id}",
            });

            await Task.Delay(1500, cancellationToken);

            if (process.HasExited)
            {
                log?.Invoke(new CommandLogEntry
                {
                    Source = "app",
                    Message = $"Windows Terminal exited early with code {process.ExitCode}.",
                });

                LogProcessOutput(log, standardOutput.ToString(), standardError.ToString());

                if (process.ExitCode == 0)
                {
                    log?.Invoke(new CommandLogEntry
                    {
                        Source = "app",
                        Message = "Windows Terminal launch command accepted.",
                    });
                    log?.Invoke(new CommandLogEntry
                    {
                        Source = "app",
                        Message = "Terminal window handoff completed.",
                    });
                    return;
                }

                throw new InvalidOperationException("Windows Terminal exited before the attach session became interactive. See terminal output in the log panel for details.");
            }

            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = "Windows Terminal launch command accepted.",
            });
            log?.Invoke(new CommandLogEntry
            {
                Source = "app",
                Message = "Terminal window handoff completed.",
            });

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

    private static void LogProcessOutput(Action<CommandLogEntry>? log, string standardOutput, string standardError)
    {
        if (standardOutput.Length > 0)
        {
            foreach (var line in SplitLines(standardOutput))
            {
                log?.Invoke(new CommandLogEntry
                {
                    Source = "terminal",
                    Message = line,
                });
            }
        }

        if (standardError.Length > 0)
        {
            foreach (var line in SplitLines(standardError))
            {
                log?.Invoke(new CommandLogEntry
                {
                    Source = "terminal:err",
                    Message = line,
                });
            }
        }
    }

    private static IEnumerable<string> SplitLines(string content)
    {
        return content
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd());
    }
}
