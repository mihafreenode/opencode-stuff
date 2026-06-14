using System.Diagnostics;
using System.Text;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Runtime;

public sealed class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        Action<bool, string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var argumentList = arguments.ToList();
        var standardOutputLines = new List<string>();
        var standardErrorLines = new List<string>();
        var standardOutputClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var standardErrorClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedAt = Stopwatch.StartNew();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in argumentList)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (string.Equals(fileName, "git", StringComparison.OrdinalIgnoreCase))
        {
            process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            process.StartInfo.Environment["GCM_INTERACTIVE"] = "Never";
        }

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                standardOutputClosed.TrySetResult();
                return;
            }

            lock (standardOutputLines)
            {
                standardOutputLines.Add(eventArgs.Data);
            }

            onOutput?.Invoke(false, eventArgs.Data);
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                standardErrorClosed.TrySetResult();
                return;
            }

            lock (standardErrorLines)
            {
                standardErrorLines.Add(eventArgs.Data);
            }

            onOutput?.Invoke(true, eventArgs.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort cancellation. The original exception path is more useful than a kill race.
            }
        });

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(standardOutputClosed.Task, standardErrorClosed.Task);
        startedAt.Stop();

        var outputLines = standardOutputLines.ToList();
        var errorLines = standardErrorLines.ToList();

        return new ProcessResult
        {
            Command = BuildCommandText(fileName, argumentList),
            ExitCode = process.ExitCode,
            StandardOutput = string.Join(Environment.NewLine, outputLines),
            StandardError = string.Join(Environment.NewLine, errorLines),
            StandardOutputLines = outputLines,
            StandardErrorLines = errorLines,
            Duration = startedAt.Elapsed,
        };
    }

    private static string BuildCommandText(string fileName, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder(fileName);
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            builder.Append(argument.Contains(' ') ? $"\"{argument}\"" : argument);
        }

        return builder.ToString();
    }
}
