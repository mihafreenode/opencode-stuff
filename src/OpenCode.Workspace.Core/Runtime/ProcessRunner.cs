using System.Diagnostics;
using System.Text;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Runtime;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        Action<bool, string>? onOutput = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null,
        Action<string>? onDiagnostic = null)
    {
        var argumentList = arguments.ToList();
        var standardOutputLines = new List<string>();
        var standardErrorLines = new List<string>();
        var standardOutputClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var standardErrorClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedAt = Stopwatch.StartNew();
        DataReceivedEventHandler? outputHandler = null;
        DataReceivedEventHandler? errorHandler = null;
        var effectiveTimeout = timeout;
        using var timeoutCts = effectiveTimeout is { } timeoutValue ? new CancellationTokenSource(timeoutValue) : null;
        using var linkedCancellationSource = timeoutCts is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var effectiveCancellationToken = linkedCancellationSource.Token;
        var commandText = BuildCommandText(fileName, argumentList);

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

        onDiagnostic?.Invoke($"[process] starting: {commandText}");

        outputHandler = (_, eventArgs) =>
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
        process.OutputDataReceived += outputHandler;

        errorHandler = (_, eventArgs) =>
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
        process.ErrorDataReceived += errorHandler;

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            onDiagnostic?.Invoke($"[process] started pid={process.Id}: {commandText}");

            using var cancellationRegistration = effectiveCancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        onDiagnostic?.Invoke($"[process] cancellation requested, killing tree pid={process.Id}: {commandText}");
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Best effort cancellation. The original exception path is more useful than a kill race.
                }
            });

            try
            {
                await process.WaitForExitAsync(effectiveCancellationToken);
            }
            catch (OperationCanceledException) when (timeoutCts is not null && timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                var timeoutSeconds = effectiveTimeout.GetValueOrDefault().TotalSeconds;
                onDiagnostic?.Invoke($"[process] timeout after {timeoutSeconds:F0}s: {commandText}");
                throw new TimeoutException($"Process timed out after {timeoutSeconds:F0} seconds: {commandText}");
            }

            await Task.WhenAll(standardOutputClosed.Task, standardErrorClosed.Task);
            startedAt.Stop();

            var outputLines = standardOutputLines.ToList();
            var errorLines = standardErrorLines.ToList();
            onDiagnostic?.Invoke($"[process] exited code={process.ExitCode}: {commandText}");

            return new ProcessResult
            {
                Command = commandText,
                ExitCode = process.ExitCode,
                StandardOutput = string.Join(Environment.NewLine, outputLines),
                StandardError = string.Join(Environment.NewLine, errorLines),
                StandardOutputLines = outputLines,
                StandardErrorLines = errorLines,
                Duration = startedAt.Elapsed,
            };
        }
        finally
        {
            startedAt.Stop();

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch
            {
            }

            try
            {
                process.CancelOutputRead();
            }
            catch
            {
            }

            try
            {
                process.CancelErrorRead();
            }
            catch
            {
            }

            standardOutputClosed.TrySetResult();
            standardErrorClosed.TrySetResult();

            try
            {
                await Task.WhenAll(standardOutputClosed.Task, standardErrorClosed.Task);
            }
            catch
            {
            }

            if (outputHandler is not null)
            {
                process.OutputDataReceived -= outputHandler;
            }

            if (errorHandler is not null)
            {
                process.ErrorDataReceived -= errorHandler;
            }

            onDiagnostic?.Invoke($"[process] cleanup complete: {commandText}");
        }
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
