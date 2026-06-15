using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Runtime;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        Action<bool, string>? onOutput = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null,
        Action<string>? onDiagnostic = null);
}
