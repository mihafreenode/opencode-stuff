using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.Services;

public interface IDiagnosticsShellService
{
    Task<WorkspaceDoctorResult> RunDoctorAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task<PlatformValidationReport> ValidateAsync(string workspacePath, string targetPlatform, CancellationToken cancellationToken = default);
}
