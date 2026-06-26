using OpenCode.Workspace.Core.Diagnostics;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Platform;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class DiagnosticsShellService : IDiagnosticsShellService
{
    private readonly WorkspaceDoctorService _doctorService;
    private readonly PlatformValidationService _validationService;
    private readonly IHostCapabilities _hostCapabilities;

    public DiagnosticsShellService(WorkspaceDoctorService doctorService, PlatformValidationService validationService, IHostCapabilities hostCapabilities)
    {
        _doctorService = doctorService;
        _validationService = validationService;
        _hostCapabilities = hostCapabilities;
    }

    public Task<HostCapabilityReport> DetectHostCapabilitiesAsync(CancellationToken cancellationToken = default)
        => _hostCapabilities.DetectAsync(cancellationToken);

    public Task<WorkspaceDoctorResult> RunDoctorAsync(string workspacePath, CancellationToken cancellationToken = default)
        => _doctorService.DiagnoseAsync(workspacePath, cancellationToken);

    public Task<PlatformValidationReport> ValidateAsync(string workspacePath, string targetPlatform, CancellationToken cancellationToken = default)
        => _validationService.ValidateAsync(new PlatformValidationRequest { WorkspacePath = workspacePath, TargetPlatform = targetPlatform }, cancellationToken);
}
