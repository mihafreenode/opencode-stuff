namespace OpenCode.Workspace.Platform;

public interface IHostCapabilities
{
    PlatformKind Platform { get; }

    Task<HostCapabilityReport> DetectAsync(CancellationToken cancellationToken = default);
}
