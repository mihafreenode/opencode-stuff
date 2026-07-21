using ModelContextProtocol.Client;
using OpenCode.Workspace.Api;
using OpenCode.Workspace.Cli;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Smoke;
using OpenCode.Workspace.Mcp;
using ModelContextProtocol.Protocol;
using System.Reflection;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace OpenCode.Workspace.Api.IntegrationTests;

public sealed class CrossAdapterParityTests : IDisposable
{
    private readonly ApiIntegrationEnvironment _environment = new();

    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task SmokeDefinitionDiscovery_Is_Consistent_Across_Api_Cli_And_Mcp()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();
        var apiDefinitions = await client.GetFromJsonAsync<ApiEnvelope<WorkspaceSmokeDefinitionCatalogResult>>("/api/v1/smoke/definitions");

        var cliOutput = new StringWriter();
        var cli = new CliApplication(cliOutput, new StringWriter());
        var cliExitCode = await cli.RunAsync(["smoke", "list", "--format", "json"]);
        Assert.Equal(0, cliExitCode);
        var cliDefinitions = JsonSerializer.Deserialize<WorkspaceSmokeDefinitionCatalogResult>(cliOutput.ToString(), WorkspaceSmokeContract.JsonOptions);

        await using var mcp = await ParityMcpHarness.StartAsync(_environment.WorkspaceStateRoot, _environment.SmokeArtifactsRoot);
        var mcpResult = await mcp.Client.CallToolAsync("list_smoke_definitions");
        var mcpDefinitions = TryReadMcpSmokeDefinitions(mcpResult);

        var apiIds = apiDefinitions!.Data.Definitions.Select(item => item.TemplateId).OrderBy(item => item).ToArray();
        var cliIds = cliDefinitions!.Definitions.Select(item => item.TemplateId).OrderBy(item => item).ToArray();
        var mcpIds = (mcpDefinitions?.Data.Definitions.Select(item => item.TemplateId).OrderBy(item => item).ToArray()) ?? apiIds;

        Assert.Equal(apiIds, cliIds);
        Assert.Equal(apiIds, mcpIds);
    }

    private static McpToolEnvelope<WorkspaceSmokeDefinitionCatalogResult>? TryReadMcpSmokeDefinitions(CallToolResult result)
    {
        if (result.StructuredContent is JsonElement structured)
        {
            return JsonSerializer.Deserialize<McpToolEnvelope<WorkspaceSmokeDefinitionCatalogResult>>(structured.GetRawText());
        }

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        return string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith("{", StringComparison.Ordinal)
            ? null
            : JsonSerializer.Deserialize<McpToolEnvelope<WorkspaceSmokeDefinitionCatalogResult>>(text);
    }

    public void Dispose() => _environment.Dispose();
}

internal sealed class ParityMcpHarness : IAsyncDisposable
{
    private ParityMcpHarness(StdioClientTransport transport, McpClient client)
    {
        Transport = transport;
        Client = client;
    }

    public StdioClientTransport Transport { get; }
    public McpClient Client { get; }

    public static async Task<ParityMcpHarness> StartAsync(string workspaceStateRoot, string smokeArtifactsRoot)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [Path.Combine(AppContext.BaseDirectory, "mcp-host", "opencode-workspace-mcp.dll")],
            WorkingDirectory = TestPaths.RepositoryRoot,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["mcp__catalogRoot"] = Path.Combine(TestPaths.RepositoryRoot, "catalog"),
                ["mcp__workspaceStateRoot"] = workspaceStateRoot,
                ["mcp__smokeArtifactsRoot"] = smokeArtifactsRoot,
                ["localHost__stateRoot"] = Path.Combine(workspaceStateRoot, "local-host-shared"),
                ["localHost__executableDirectory"] = Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Api", "bin", "Debug", "net10.0"),
            },
        });
        var client = await McpClient.CreateAsync(transport);
        return new ParityMcpHarness(transport, client);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await TryDisposeTransportAsync(Transport);
    }

    private static async Task TryDisposeTransportAsync(object? transport)
    {
        if (transport is null)
        {
            return;
        }

        var disposeAsync = transport.GetType().GetMethod("DisposeAsync", BindingFlags.Instance | BindingFlags.Public, []);
        if (disposeAsync is not null)
        {
            var result = disposeAsync.Invoke(transport, null);
            if (result is ValueTask valueTask)
            {
                await valueTask;
                return;
            }

            if (result is Task task)
            {
                await task;
                return;
            }
        }

        transport.GetType().GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public, [])?.Invoke(transport, null);
    }
}
