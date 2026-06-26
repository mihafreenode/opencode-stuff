namespace OpenCode.Workspace.Platform;

public interface ICommandProbe
{
    Task<CommandProbeResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}

public sealed class CommandProbeResult
{
    public bool IsSuccess { get; init; }
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public string FailureMessage { get; init; } = string.Empty;
}
