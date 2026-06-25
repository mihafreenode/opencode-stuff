using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Platform.Windows;

public sealed class WindowsTerminalProfileSetupService
{
    private readonly IWindowsTerminalProfileManager _profileManager;
    private readonly IWindowsHostCapabilities _hostCapabilities;

    public WindowsTerminalProfileSetupService(IWindowsTerminalProfileManager profileManager, IWindowsHostCapabilities hostCapabilities)
    {
        _profileManager = profileManager;
        _hostCapabilities = hostCapabilities;
    }

    public async Task<WindowsTerminalProfileSetupResult> EnsureAsync(WorkspaceDefinition definition, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WindowsTerminalProfileSetupResult
            {
                Status = WindowsTerminalProfileSetupStatus.Unavailable,
                Summary = "Windows Terminal profile setup is only available on Windows.",
                ProfileName = string.Empty,
                FragmentPath = string.Empty,
                ResolvedFontFace = string.Empty,
                FailureReason = string.Empty,
            };
        }

        var terminalCheck = await _hostCapabilities.CheckWindowsTerminalAsync(cancellationToken);
        if (!terminalCheck.IsAvailable)
        {
            return new WindowsTerminalProfileSetupResult
            {
                Status = WindowsTerminalProfileSetupStatus.Unavailable,
                Summary = terminalCheck.Reason,
                ProfileName = string.Empty,
                FragmentPath = _profileManager.GetFragmentFilePath(),
                ResolvedFontFace = string.Empty,
                FailureReason = string.Empty,
            };
        }

        try
        {
            var resolvedFace = _hostCapabilities.ResolvePreferredTerminalFace(definition.Terminal.Font.Family);
            var profileName = _profileManager.GetProfileName(definition);
            var fragmentPath = _profileManager.GetFragmentFilePath();
            var previousFace = _profileManager.GetConfiguredFontFace(definition);
            if (!string.IsNullOrWhiteSpace(previousFace) && string.Equals(previousFace, resolvedFace, StringComparison.OrdinalIgnoreCase))
            {
                return new WindowsTerminalProfileSetupResult
                {
                    Status = WindowsTerminalProfileSetupStatus.AlreadyConfigured,
                    Summary = $"Windows Terminal profile '{profileName}' is already configured.",
                    ProfileName = profileName,
                    FragmentPath = fragmentPath,
                    ResolvedFontFace = resolvedFace,
                    FailureReason = string.Empty,
                };
            }

            _profileManager.EnsureManagedProfile(definition, definition.Terminal.Font, resolvedFace);
            var status = string.IsNullOrWhiteSpace(previousFace)
                ? WindowsTerminalProfileSetupStatus.Created
                : WindowsTerminalProfileSetupStatus.Updated;
            return new WindowsTerminalProfileSetupResult
            {
                Status = status,
                Summary = status == WindowsTerminalProfileSetupStatus.Created
                    ? $"Created Windows Terminal profile '{profileName}'."
                    : $"Updated Windows Terminal profile '{profileName}'.",
                ProfileName = profileName,
                FragmentPath = fragmentPath,
                ResolvedFontFace = resolvedFace,
                FailureReason = string.Empty,
            };
        }
        catch (Exception exception)
        {
            return new WindowsTerminalProfileSetupResult
            {
                Status = WindowsTerminalProfileSetupStatus.Failed,
                Summary = "Windows Terminal profile setup failed.",
                ProfileName = string.Empty,
                FragmentPath = _profileManager.GetFragmentFilePath(),
                ResolvedFontFace = string.Empty,
                FailureReason = exception.Message,
            };
        }
    }
}

public enum WindowsTerminalProfileSetupStatus
{
    AlreadyConfigured,
    Created,
    Updated,
    Unavailable,
    Failed,
}

public sealed class WindowsTerminalProfileSetupResult
{
    public required WindowsTerminalProfileSetupStatus Status { get; init; }
    public required string Summary { get; init; }
    public required string ProfileName { get; init; }
    public required string FragmentPath { get; init; }
    public required string ResolvedFontFace { get; init; }
    public required string FailureReason { get; init; }
}
