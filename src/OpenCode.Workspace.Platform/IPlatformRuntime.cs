using System.Runtime.InteropServices;

namespace OpenCode.Workspace.Platform;

public interface IPlatformRuntime
{
    bool IsWindows { get; }
    bool IsLinux { get; }
    bool IsMacOS { get; }
    string Architecture { get; }
}

public sealed class RuntimeInformationPlatformRuntime : IPlatformRuntime
{
    public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    public string Architecture => RuntimeInformation.OSArchitecture.ToString();
}
