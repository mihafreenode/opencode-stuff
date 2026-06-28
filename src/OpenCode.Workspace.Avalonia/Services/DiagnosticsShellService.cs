using OpenCode.Workspace.Core.Diagnostics;
using OpenCode.Workspace.Core.Catalog;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Platform;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class DiagnosticsShellService : IDiagnosticsShellService
{
    private readonly WorkspaceDoctorService _doctorService;
    private readonly PlatformValidationService _validationService;
    private readonly IHostCapabilities _hostCapabilities;
    private readonly BuiltInCatalogProvider _catalogProvider;

    public DiagnosticsShellService(WorkspaceDoctorService doctorService, PlatformValidationService validationService, IHostCapabilities hostCapabilities, BuiltInCatalogProvider catalogProvider)
    {
        _doctorService = doctorService;
        _validationService = validationService;
        _hostCapabilities = hostCapabilities;
        _catalogProvider = catalogProvider;
    }

    public Task<HostCapabilityReport> DetectHostCapabilitiesAsync(CancellationToken cancellationToken = default)
        => _hostCapabilities.DetectAsync(cancellationToken);

    public Task<WorkspaceDoctorResult> RunDoctorAsync(string workspacePath, CancellationToken cancellationToken = default)
        => _doctorService.DiagnoseAsync(workspacePath, cancellationToken);

    public Task<PlatformValidationReport> ValidateAsync(string workspacePath, string targetPlatform, CancellationToken cancellationToken = default)
        => _validationService.ValidateAsync(new PlatformValidationRequest { WorkspacePath = workspacePath, TargetPlatform = targetPlatform }, cancellationToken);

    public TemplateCatalogDiagnosticResult GetTemplateCatalogStatus()
    {
        var templates = _catalogProvider.LoadTemplates();
        return new TemplateCatalogDiagnosticResult
        {
            CatalogRootPath = _catalogProvider.CatalogRootPath,
            TemplateCount = templates.Count,
            Detail = templates.Count == 0
                ? "No template manifests were loaded from the packaged catalog."
                : $"Loaded {templates.Count} template manifest(s) from the packaged catalog.",
        };
    }
}
