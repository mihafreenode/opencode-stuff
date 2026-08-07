using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenCode.Workspace.AppSupport;
namespace OpenCode.Workspace.Mcp;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Configuration.AddJsonFile("mcp.appsettings.json", optional: true, reloadOnChange: false);
        TryAddPackagedConfiguration(builder.Configuration, "mcp");
        builder.Logging.ClearProviders();
        // Stdio is reserved exclusively for MCP frames. The caller owns diagnostics capture.
        builder.Logging.SetMinimumLevel(LogLevel.None);
        builder.Services.AddOpenCodeWorkspaceLocalServices(builder.Configuration);
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<OpenCodeWorkspaceMcpTools>()
            .WithResources<OpenCodeWorkspaceMcpResources>();

        await builder.Build().RunAsync();
    }

    private static void TryAddPackagedConfiguration(ConfigurationManager configuration, string hostName)
    {
        try
        {
            var installationLayout = OpenCodeWorkspaceInstallationLayout.Resolve(AppContext.BaseDirectory);
            var configPath = installationLayout.GetConfigFilePath(hostName);
            if (File.Exists(configPath))
            {
                configuration.AddJsonFile(configPath, optional: true, reloadOnChange: false);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
