using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Runtime;

public interface IPlatformDetector
{
    Task<HostPlatformInfo> DetectAsync(CancellationToken cancellationToken = default);
}
