using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace OpenCode.Workspace.Mcp;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddOpenCodeWorkspaceLocalServices(builder.Configuration);
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<OpenCodeWorkspaceMcpTools>()
            .WithResources<OpenCodeWorkspaceMcpResources>();

        await builder.Build().RunAsync();
    }
}
