using OpenCode.Workspace.Core.Diagnostics;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class DiagnosticsShellService : IDiagnosticsShellService
{
    private readonly WorkspaceDoctorService _doctorService;
    private readonly PlatformValidationService _validationService;

    public DiagnosticsShellService(WorkspaceDoctorService doctorService, PlatformValidationService validationService)
    {
        _doctorService = doctorService;
        _validationService = validationService;
    }

    public Task<WorkspaceDoctorResult> RunDoctorAsync(string workspacePath, CancellationToken cancellationToken = default)
        => _doctorService.DiagnoseAsync(workspacePath, cancellationToken);

    public Task<PlatformValidationReport> ValidateAsync(string workspacePath, string targetPlatform, CancellationToken cancellationToken = default)
        => _validationService.ValidateAsync(new PlatformValidationRequest { WorkspacePath = workspacePath, TargetPlatform = targetPlatform }, cancellationToken);
}
