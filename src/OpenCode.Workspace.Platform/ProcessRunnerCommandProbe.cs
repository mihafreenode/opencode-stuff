using OpenCode.Workspace.Core.Runtime;

namespace OpenCode.Workspace.Platform;

public sealed class ProcessRunnerCommandProbe : ICommandProbe
{
    private readonly ProcessRunner _processRunner;

    public ProcessRunnerCommandProbe(ProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<CommandProbeResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _processRunner.RunAsync(fileName, [.. arguments], cancellationToken: cancellationToken);
            return new CommandProbeResult
            {
                IsSuccess = result.IsSuccess,
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput,
                StandardError = result.StandardError,
            };
        }
        catch (Exception exception)
        {
            return new CommandProbeResult
            {
                IsSuccess = false,
                ExitCode = -1,
                FailureMessage = exception.Message,
                StandardError = exception.Message,
            };
        }
    }
}
