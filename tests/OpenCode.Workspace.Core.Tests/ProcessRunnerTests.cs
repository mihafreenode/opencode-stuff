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
        Assert.Contains("out:stdout-line", streamedLines.Select(line => line.Trim()).ToArray());
        Assert.Contains("err:stderr-line", streamedLines.Select(line => line.Trim()).ToArray());
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
    public async Task RunAsync_CancelledChildExitNeverBecomesProcessFailure()
    {
        var runner = new ProcessRunner();
        for (var iteration = 0; iteration < 10; iteration++)
        {
            using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(TestCommand.FileName, TestCommand.LongRunning, cancellationToken: cancellationSource.Token));
        }
    }

    [SkippableFact]
    public async Task RunAsync_CancellationTerminatesDescendantProcessTree()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Process-tree cancellation requires a Windows host.");
        var runner = new ProcessRunner();
        using var cancellationSource = new CancellationTokenSource();
        var parentIdSource = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var childIdSource = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var treeReadySource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var streamedLines = new ConcurrentQueue<string>();
        OwnedProcessIdentity? parentIdentity = null;
        OwnedProcessIdentity? childIdentity = null;

        var task = runner.RunAsync(
            TestCommand.FileName,
            TestCommand.DescendantProcessTree,
            onOutput: (isError, line) =>
            {
                streamedLines.Enqueue($"{(isError ? "err" : "out")}:{line}");
                TrySetProcessId(line, "PARENT:", parentIdSource);
                TrySetProcessId(line, "CHILD:", childIdSource);
                if (isError && string.Equals(line, "TREE-READY", StringComparison.Ordinal)) treeReadySource.TrySetResult();
            },
            cancellationToken: cancellationSource.Token);

        try
        {
            var parentId = await parentIdSource.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var childId = await childIdSource.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await treeReadySource.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotEqual(parentId, childId);
            parentIdentity = OwnedProcessIdentity.Capture(parentId);
            childIdentity = OwnedProcessIdentity.Capture(childId);
            Assert.True(parentIdentity.IsRunning());
            Assert.True(childIdentity.IsRunning());
            Assert.Contains("err:TREE-READY", streamedLines);

            cancellationSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(15)));
            await WaitForExitAsync(parentIdentity, TimeSpan.FromSeconds(10));
            await WaitForExitAsync(childIdentity, TimeSpan.FromSeconds(10));
            Assert.False(parentIdentity.IsRunning());
            Assert.False(childIdentity.IsRunning());
        }
        finally
        {
            cancellationSource.Cancel();
            if (childIdentity is not null) childIdentity.TerminateIfRunning();
            if (parentIdentity is not null) parentIdentity.TerminateIfRunning();
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
            }
        }
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

    [Fact]
    public async Task RunAsync_GitProbeOutsideRepositoryExitsPromptly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"process-runner-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var result = await new ProcessRunner().RunAsync("git", ["rev-parse", "--is-inside-work-tree"], root, timeout: TimeSpan.FromSeconds(10));
            Assert.NotEqual(0, result.ExitCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TrySetProcessId(string line, string prefix, TaskCompletionSource<int> source)
    {
        if (line.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(line[prefix.Length..], out var processId))
        {
            source.TrySetResult(processId);
        }
    }

    private static async Task WaitForExitAsync(OwnedProcessIdentity process, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline && process.IsRunning())
        {
            await Task.Delay(50);
        }
    }

    private sealed record OwnedProcessIdentity(int ProcessId, DateTime StartTimeUtc)
    {
        public static OwnedProcessIdentity Capture(int processId)
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return new OwnedProcessIdentity(processId, process.StartTime.ToUniversalTime());
        }

        public bool IsRunning()
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(ProcessId);
                return !process.HasExited && process.StartTime.ToUniversalTime() == StartTimeUtc;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public void TerminateIfRunning()
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(ProcessId);
                if (!process.HasExited && process.StartTime.ToUniversalTime() == StartTimeUtc)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private static class TestCommand
    {
        public static string FileName => OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh";

        public static string[] StdoutAndStderrExitThree => Create(
            windows: "[Console]::Out.WriteLine('stdout-line'); [Console]::Error.WriteLine('stderr-line'); exit 3",
            unix: "printf 'stdout-line\n'; printf 'stderr-line\n' >&2; exit 3");

        public static string[] StdoutOnly => Create(
            windows: "[Console]::Out.WriteLine('stdout-only'); exit 0",
            unix: "printf 'stdout-only\n'; exit 0");

        public static string[] StderrOnly => Create(
            windows: "[Console]::Error.WriteLine('stderr-only'); exit 0",
            unix: "printf 'stderr-only\n' >&2; exit 0");

        public static string[] MultipleLines => Create(
            windows: "[Console]::Out.WriteLine('stdout-1'); [Console]::Error.WriteLine('stderr-1'); [Console]::Out.WriteLine('stdout-2'); [Console]::Error.WriteLine('stderr-2'); [Console]::Out.WriteLine('stdout-3'); [Console]::Error.WriteLine('stderr-3'); exit 0",
            unix: "printf 'stdout-1\nstdout-2\nstdout-3\n'; printf 'stderr-1\nstderr-2\nstderr-3\n' >&2; exit 0");

        public static string[] ImmediateExit => Create(
            windows: "[Console]::Out.WriteLine('stdout-immediate'); [Console]::Error.WriteLine('stderr-immediate'); exit 7",
            unix: "printf 'stdout-immediate\n'; printf 'stderr-immediate\n' >&2; exit 7");

        public static string[] LongRunning => Create(
            windows: "[Console]::Out.WriteLine('stdout-before-wait'); Start-Sleep -Seconds 30",
            unix: "printf 'stdout-before-wait\n'; sleep 30");

        public static string[] DescendantProcessTree => Create(
            windows: "$child = Start-Process -FilePath 'powershell.exe' -ArgumentList '-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 30\"' -PassThru; [Console]::Out.WriteLine(\"PARENT:$PID\"); [Console]::Out.WriteLine(\"CHILD:$($child.Id)\"); [Console]::Error.WriteLine('TREE-READY'); Start-Sleep -Seconds 30",
            unix: "sleep 30");

        public static string[] NoOutput => Create(
            windows: "exit 0",
            unix: "exit 0");

        public static string[] LargeOutput => Create(
            windows: string.Join("; ", Enumerable.Range(0, 250).Select(index => $"[Console]::Out.WriteLine('stdout-{index:000}')").Concat(Enumerable.Range(0, 250).Select(index => $"[Console]::Error.WriteLine('stderr-{index:000}')")).Concat(["exit 0"])),
            unix: "for i in $(seq 0 249); do printf 'stdout-%03d\\n' \"$i\"; done; for i in $(seq 0 249); do printf 'stderr-%03d\\n' \"$i\" >&2; done; exit 0");

        private static string[] Create(string windows, string unix)
            => OperatingSystem.IsWindows()
                ? (["-NoProfile", "-NonInteractive", "-Command", windows])
                : (["-c", unix]);
    }
}
