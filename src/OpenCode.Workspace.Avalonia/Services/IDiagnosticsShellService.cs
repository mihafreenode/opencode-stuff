using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Platform;

namespace OpenCode.Workspace.Avalonia.Services;

public interface IDiagnosticsShellService
{
    Task<HostCapabilityReport> DetectHostCapabilitiesAsync(CancellationToken cancellationToken = default);
    Task<WorkspaceDoctorResult> RunDoctorAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task<PlatformValidationReport> ValidateAsync(string workspacePath, string targetPlatform, CancellationToken cancellationToken = default);
    TemplateCatalogDiagnosticResult GetTemplateCatalogStatus();
}

public sealed class TemplateCatalogDiagnosticResult
{
    public required string CatalogRootPath { get; init; }
    public required int TemplateCount { get; init; }
    public string Detail { get; init; } = string.Empty;
    public bool IsAvailable => TemplateCount > 0;
}
