using OpenCode.Workspace.Core.Runtime;
using System.Collections.Concurrent;
using Xunit;

namespace OpenCode.Workspace.Core.Tests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_CapturesStdoutAndStderrLines()
    {
        var runner = new ProcessRunner();
        var streamedLines = new ConcurrentQueue<string>();

        var result = await runner.RunAsync(
            TestCommand.FileName,
            TestCommand.StdoutAndStderrExitThree,
            onOutput: (isError, line) => streamedLines.Enqueue($"{(isError ? "err" : "out")}:{line}"));

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(["stdout-line"], result.StandardOutputLines.Select(line => line.Trim()).ToArray());
        Assert.Equal(["stderr-line"], result.StandardErrorLines.Select(line => line.Trim()).ToArray());
        Assert.Contains("out:stdout-line", streamedLines);
        Assert.Contains("err:stderr-line", streamedLines);
        Assert.False(result.IsSuccess);
        Assert.NotEqual(TimeSpan.Zero, result.Duration);
    }

    [Fact]
    public async Task RunAsync_CapturesStdoutOnly()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(TestCommand.FileName, TestCommand.StdoutOnly);

        Assert.Equal(["stdout-only"], result.StandardOutputLines.Select(line => line.Trim()).ToArray());
        Assert.Empty(result.StandardErrorLines);
    }

    [Fact]
    public async Task RunAsync_CapturesStderrOnly()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(TestCommand.FileName, TestCommand.StderrOnly);

        Assert.Empty(result.StandardOutputLines);
        Assert.Equal(["stderr-only"], result.StandardErrorLines.Select(line => line.Trim()).ToArray());
    }

    [Fact]
    public async Task RunAsync_CapturesSeveralLinesOnBothStreams()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(TestCommand.FileName, TestCommand.MultipleLines);

        Assert.Equal(["stdout-1", "stdout-2", "stdout-3"], result.StandardOutputLines.Select(line => line.Trim()).ToArray());
        Assert.Equal(["stderr-1", "stderr-2", "stderr-3"], result.StandardErrorLines.Select(line => line.Trim()).ToArray());
    }

    [Fact]
    public async Task RunAsync_ImmediateExitStillCapturesBothStreams()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(TestCommand.FileName, TestCommand.ImmediateExit);

        Assert.Equal(7, result.ExitCode);
        Assert.Equal(["stdout-immediate"], result.StandardOutputLines.Select(line => line.Trim()).ToArray());
        Assert.Equal(["stderr-immediate"], result.StandardErrorLines.Select(line => line.Trim()).ToArray());
    }

    [Fact]
    public async Task RunAsync_NonZeroExitStillCapturesBothStreams()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(TestCommand.FileName, TestCommand.StdoutAndStderrExitThree);

        Assert.Equal(3, result.ExitCode);
        Assert.NotEmpty(result.StandardOutputLines);
        Assert.NotEmpty(result.StandardErrorLines);
    }

    [Fact]
    public async Task RunAsync_CancellationDoesNotDeadlockStreamCompletion()
    {
        var runner = new ProcessRunner();
        using var cancellationSource = new CancellationTokenSource();

        var task = runner.RunAsync(TestCommand.FileName, TestCommand.LongRunning, cancellationToken: cancellationSource.Token);
        cancellationSource.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task RunAsync_EmptyStreamsRemainEmpty()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(TestCommand.FileName, TestCommand.NoOutput);

        Assert.Empty(result.StandardOutputLines);
        Assert.Empty(result.StandardErrorLines);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_LargeOutputIsFullyDrained()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(TestCommand.FileName, TestCommand.LargeOutput);

        Assert.Equal(250, result.StandardOutputLines.Count);
        Assert.Equal(250, result.StandardErrorLines.Count);
        Assert.Equal("stdout-000", result.StandardOutputLines[0].Trim());
        Assert.Equal("stderr-249", result.StandardErrorLines[^1].Trim());
    }

    [Fact]
    public async Task RunAsync_RepeatedRunsDoNotDropStderrLines()
    {
        var runner = new ProcessRunner();

        for (var index = 0; index < 10; index++)
        {
            var result = await runner.RunAsync(TestCommand.FileName, TestCommand.StdoutAndStderrExitThree);
            Assert.Contains(result.StandardOutputLines, line => line.Trim() == "stdout-line");
            Assert.Contains(result.StandardErrorLines, line => line.Trim() == "stderr-line");
        }
    }

    private static class TestCommand
    {
        public static string FileName => OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";

        public static string[] StdoutAndStderrExitThree => Create(
            windows: "echo stdout-line & echo stderr-line 1>&2 & exit 3",
            unix: "printf 'stdout-line\n'; printf 'stderr-line\n' >&2; exit 3");

        public static string[] StdoutOnly => Create(
            windows: "echo stdout-only & exit 0",
            unix: "printf 'stdout-only\n'; exit 0");

        public static string[] StderrOnly => Create(
            windows: "echo stderr-only 1>&2 & exit 0",
            unix: "printf 'stderr-only\n' >&2; exit 0");

        public static string[] MultipleLines => Create(
            windows: "echo stdout-1 & echo stderr-1 1>&2 & echo stdout-2 & echo stderr-2 1>&2 & echo stdout-3 & echo stderr-3 1>&2 & exit 0",
            unix: "printf 'stdout-1\nstdout-2\nstdout-3\n'; printf 'stderr-1\nstderr-2\nstderr-3\n' >&2; exit 0");

        public static string[] ImmediateExit => Create(
            windows: "echo stdout-immediate & echo stderr-immediate 1>&2 & exit 7",
            unix: "printf 'stdout-immediate\n'; printf 'stderr-immediate\n' >&2; exit 7");

        public static string[] LongRunning => Create(
            windows: "echo stdout-before-wait & powershell -NoProfile -Command Start-Sleep -Seconds 30",
            unix: "printf 'stdout-before-wait\n'; sleep 30");

        public static string[] NoOutput => Create(
            windows: "exit 0",
            unix: "exit 0");

        public static string[] LargeOutput => Create(
            windows: string.Join(" & ", Enumerable.Range(0, 250).Select(index => $"echo stdout-{index:000}").Concat(Enumerable.Range(0, 250).Select(index => $"echo stderr-{index:000} 1^>^&2")).Concat(["exit 0"])),
            unix: "for i in $(seq 0 249); do printf 'stdout-%03d\\n' \"$i\"; done; for i in $(seq 0 249); do printf 'stderr-%03d\\n' \"$i\" >&2; done; exit 0");

        private static string[] Create(string windows, string unix)
            => OperatingSystem.IsWindows()
                ? (["/c", windows])
                : (["-c", unix]);
    }
}
