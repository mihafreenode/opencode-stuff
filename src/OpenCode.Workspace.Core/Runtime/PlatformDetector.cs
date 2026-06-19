using System.Runtime.InteropServices;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Core.Runtime;

public sealed class PlatformDetector : IPlatformDetector
{
    private readonly IProcessRunner _processRunner;

    public PlatformDetector(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<HostPlatformInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        var operatingSystem = DetectOperatingSystem();
        var architecture = DetectArchitecture();
        var nativeContainerPlatform = MapNativeContainerPlatform(architecture);
        var dockerAvailability = await DetectDockerAvailabilityAsync(cancellationToken);

        return new HostPlatformInfo
        {
            OperatingSystem = operatingSystem,
            Architecture = architecture,
            HostDescription = $"{operatingSystem} {architecture}",
            NativeContainerPlatform = nativeContainerPlatform,
            Docker = dockerAvailability,
        };
    }

    private async Task<ContainerRuntimeAvailability> DetectDockerAvailabilityAsync(CancellationToken cancellationToken)
    {
        ProcessResult? cliResult = null;
        ProcessResult? engineResult = null;
        ProcessResult? buildxResult = null;
        var cliAvailable = false;
        var engineReachable = false;
        var buildxAvailable = false;
        var supportedPlatforms = Array.Empty<string>();
        var diagnostics = new List<string>();

        try
        {
            cliResult = await _processRunner.RunAsync("docker", ["--version"], cancellationToken: cancellationToken);
            cliAvailable = cliResult.IsSuccess;
            diagnostics.Add(cliAvailable
                ? $"Docker CLI available: {cliResult.StandardOutput.Trim()}"
                : $"Docker CLI returned exit code {cliResult.ExitCode}.");
        }
        catch (Exception exception)
        {
            diagnostics.Add($"Docker CLI unavailable: {exception.Message}");
        }

        if (cliAvailable)
        {
            try
            {
                engineResult = await _processRunner.RunAsync("docker", ["info"], cancellationToken: cancellationToken);
                engineReachable = engineResult.IsSuccess;
                diagnostics.Add(engineReachable ? "Docker engine reachable." : $"Docker engine not reachable. Exit code {engineResult.ExitCode}.");
            }
            catch (Exception exception)
            {
                diagnostics.Add($"Docker engine check failed: {exception.Message}");
            }

            try
            {
                buildxResult = await _processRunner.RunAsync("docker", ["buildx", "ls"], cancellationToken: cancellationToken);
                buildxAvailable = buildxResult.IsSuccess;
                supportedPlatforms = buildxAvailable
                    ? ParseSupportedPlatforms(buildxResult.StandardOutputLines)
                    : Array.Empty<string>();
                diagnostics.Add(buildxAvailable
                    ? supportedPlatforms.Length > 0
                        ? $"Docker Buildx available for {string.Join(", ", supportedPlatforms)}."
                        : "Docker Buildx available."
                    : $"Docker Buildx not available. Exit code {buildxResult.ExitCode}.");
            }
            catch (Exception exception)
            {
                diagnostics.Add($"Docker Buildx check failed: {exception.Message}");
            }
        }

        return new ContainerRuntimeAvailability
        {
            EngineId = "docker",
            CliAvailable = cliAvailable,
            EngineReachable = engineReachable,
            BuildxAvailable = buildxAvailable,
            SupportedPlatforms = supportedPlatforms,
            DiagnosticSummary = string.Join(" ", diagnostics.Where(item => !string.IsNullOrWhiteSpace(item))),
        };
    }

    public static string[] ParseSupportedPlatforms(IEnumerable<string> lines)
    {
        var platforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var markerIndex = line.IndexOf("linux/", StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                continue;
            }

            var slice = line[markerIndex..];
            foreach (var candidate in slice.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!candidate.StartsWith("linux/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var normalized = candidate.Trim();
                if (normalized.EndsWith("*", StringComparison.Ordinal))
                {
                    normalized = normalized[..^1];
                }

                platforms.Add(normalized);
            }
        }

        return platforms.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static HostOperatingSystem DetectOperatingSystem()
    {
        if (OperatingSystem.IsWindows())
        {
            return HostOperatingSystem.Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return HostOperatingSystem.Linux;
        }

        if (OperatingSystem.IsMacOS())
        {
            return HostOperatingSystem.MacOS;
        }

        return HostOperatingSystem.Unknown;
    }

    private static HostArchitecture DetectArchitecture()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => HostArchitecture.X64,
            Architecture.Arm64 => HostArchitecture.Arm64,
            _ => HostArchitecture.Unknown,
        };
    }

    private static string MapNativeContainerPlatform(HostArchitecture architecture)
        => architecture switch
        {
            HostArchitecture.X64 => "linux/amd64",
            HostArchitecture.Arm64 => "linux/arm64",
            _ => string.Empty,
        };
}
