using ModelContextProtocol.Client;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace OpenCode.Workspace.Mcp.Tests;

internal static class McpHostLaunch
{
    public static McpHostLaunchInfo Resolve()
    {
        var hostDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "mcp-host"));
        var hostDllPath = Path.Combine(hostDirectory, "opencode-workspace-mcp.dll");
        var runtimeConfigPath = Path.Combine(hostDirectory, "opencode-workspace-mcp.runtimeconfig.json");
        var depsPath = Path.Combine(hostDirectory, "opencode-workspace-mcp.deps.json");
        return new McpHostLaunchInfo(
            Command: "dotnet",
            Arguments: [hostDllPath],
            WorkingDirectory: TestPaths.RepositoryRoot,
            HostDirectory: hostDirectory,
            HostDllPath: hostDllPath,
            RuntimeConfigPath: runtimeConfigPath,
            DepsPath: depsPath,
            AppBaseDirectory: AppContext.BaseDirectory,
            CurrentWorkingDirectory: Environment.CurrentDirectory,
            OperatingSystem: RuntimeInformation.OSDescription,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString());
    }

    public static void AssertHostFilesExist(McpHostLaunchInfo launch)
    {
        Assert.True(Path.IsPathRooted(launch.HostDllPath), BuildMissingFileMessage(launch, "MCP host DLL path is not absolute."));
        Assert.True(File.Exists(launch.HostDllPath), BuildMissingFileMessage(launch, "MCP host DLL was not found."));
        Assert.True(File.Exists(launch.RuntimeConfigPath), BuildMissingFileMessage(launch, "MCP host runtimeconfig.json was not found."));
        Assert.True(File.Exists(launch.DepsPath), BuildMissingFileMessage(launch, "MCP host deps.json was not found."));
    }

    public static StdioClientTransport CreateTransport(Action<string>? stderrLine = null, IDictionary<string, string?>? environmentVariables = null)
    {
        var launch = Resolve();
        AssertHostFilesExist(launch);
        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "OpenCode Workspace MCP",
            Command = launch.Command,
            Arguments = launch.Arguments.ToList(),
            WorkingDirectory = launch.WorkingDirectory,
            EnvironmentVariables = environmentVariables,
            StandardErrorLines = stderrLine,
        });
    }

    public static string BuildStartupFailureMessage(McpHostLaunchInfo launch, IReadOnlyList<string> stderrLines, Exception exception)
    {
        var stderrTail = stderrLines.Count == 0
            ? "<empty>"
            : string.Join(Environment.NewLine, stderrLines.TakeLast(20));
        var builder = new StringBuilder();
        builder.AppendLine("MCP stdio server failed to start.");
        builder.AppendLine($"MCP server command: {launch.Command}");
        builder.AppendLine($"MCP server arguments: {string.Join(' ', launch.Arguments)}");
        builder.AppendLine($"MCP server path exists: {File.Exists(launch.HostDllPath)}");
        builder.AppendLine($"MCP runtimeconfig exists: {File.Exists(launch.RuntimeConfigPath)}");
        builder.AppendLine($"MCP deps exists: {File.Exists(launch.DepsPath)}");
        builder.AppendLine($"MCP host directory: {launch.HostDirectory}");
        builder.AppendLine($"Working directory: {launch.WorkingDirectory}");
        builder.AppendLine($"Current working directory: {launch.CurrentWorkingDirectory}");
        builder.AppendLine($"AppContext.BaseDirectory: {launch.AppBaseDirectory}");
        builder.AppendLine($"OS: {launch.OperatingSystem}");
        builder.AppendLine($"Process architecture: {launch.ProcessArchitecture}");
        builder.AppendLine("Exit code: unavailable");
        builder.AppendLine("stderr tail:");
        builder.AppendLine(stderrTail);
        builder.AppendLine($"Exception: {exception.GetType().FullName}: {exception.Message}");
        return builder.ToString();
    }

    private static string BuildMissingFileMessage(McpHostLaunchInfo launch, string message)
        => $"{message}{Environment.NewLine}{BuildStartupFailureMessage(launch, [], new InvalidOperationException(message))}";
}

internal sealed record McpHostLaunchInfo(
    string Command,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string HostDirectory,
    string HostDllPath,
    string RuntimeConfigPath,
    string DepsPath,
    string AppBaseDirectory,
    string CurrentWorkingDirectory,
    string OperatingSystem,
    string ProcessArchitecture);
