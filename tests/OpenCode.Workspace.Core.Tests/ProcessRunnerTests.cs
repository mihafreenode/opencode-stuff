using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Core.Tests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_CapturesStdoutAndStderrLines()
    {
        var runner = new ProcessRunner();
        var streamedLines = new List<string>();
        var command = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        var arguments = OperatingSystem.IsWindows()
            ? new[] { "/c", "echo stdout-line & echo stderr-line 1>&2 & exit 3" }
            : new[] { "-c", "printf 'stdout-line\n'; printf 'stderr-line\n' >&2; exit 3" };

        var result = await runner.RunAsync(
            command,
            arguments,
            onOutput: (isError, line) => streamedLines.Add($"{(isError ? "err" : "out")}:{line}"));

        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.StandardOutputLines, line => line.Trim() == "stdout-line");
        Assert.Contains(result.StandardErrorLines, line => line.Trim() == "stderr-line");
        Assert.Contains("out:stdout-line", string.Join(Environment.NewLine, streamedLines.Select(line => line.Trim())));
        Assert.Contains("err:stderr-line", string.Join(Environment.NewLine, streamedLines.Select(line => line.Trim())));
        Assert.False(result.IsSuccess);
        Assert.NotEqual(TimeSpan.Zero, result.Duration);
    }
}
