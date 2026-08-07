using OpenCode.Workspace.LocalClient;
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace OpenCode.Workspace.Mcp.Tests;

public sealed class McpStartupCoordinationIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    [Trait("Category", "StartupCoordination")]
    public async Task SimultaneousStartup_TwoClientsShareOneCanonicalLocalHost()
    {
        await using var scope = new LocalHostTeardownScope();
        TeardownAssert.AssertNoLiveState(scope.Identity);
        var starts = await Task.WhenAll(scope.StartMcpAsync("client-a"), scope.StartMcpAsync("client-b"));
        await using var a = starts[0];
        await using var b = starts[1];
        var host = await scope.WaitForIdentityAsync(a.StandardErrorLines.Concat(b.StandardErrorLines).ToArray());
        await TeardownAssert.AssertDescriptorHealthyAsync(scope.Identity, host, a.StandardErrorLines, b.StandardErrorLines, Timeout, CancellationToken.None);
        var controllerA = await scope.WaitForControllerAsync(host, a.Report.ProcessId, a.StandardErrorLines);
        var controllerB = await scope.WaitForControllerAsync(host, b.Report.ProcessId, b.StandardErrorLines);

        Assert.NotEqual(controllerA.ControllerSessionId, controllerB.ControllerSessionId);
        Assert.NotEqual(controllerA.ClientInstanceId, controllerB.ClientInstanceId);
        Assert.Equal(host.InstanceId, JsonSerializer.Deserialize<LocalHostDescriptor>(File.ReadAllText(scope.Identity.DescriptorPath), LocalHostContract.JsonOptions)!.InstanceId);
        TeardownAssert.AssertHostLockHeld(scope.Identity);
        AssertProtocolOnly("A", a.StandardOutputLines);
        AssertProtocolOnly("B", b.StandardOutputLines);

        await b.RequestGracefulShutdownByClosingStandardInputAsync(Timeout);
        await TeardownAssert.AssertControllerDisconnectedAsync(scope.Client(host), controllerB.ControllerSessionId, scope.Identity, b.StandardErrorLines, [], Timeout, CancellationToken.None);
        await TeardownAssert.AssertProcessStillRunningAsync(host, a.StandardErrorLines, b.StandardErrorLines, Timeout, CancellationToken.None);
        await a.RequestGracefulShutdownByClosingStandardInputAsync(Timeout);
        await TeardownAssert.AssertControllerDisconnectedAsync(scope.Client(host), controllerA.ControllerSessionId, scope.Identity, a.StandardErrorLines, [], Timeout, CancellationToken.None);
        await TeardownAssert.AssertProcessExitedAsync(host, a.StandardErrorLines, b.StandardErrorLines, Timeout, CancellationToken.None);
        await TeardownAssert.AssertHostLockReleasedAsync(scope.Identity, a.StandardErrorLines, b.StandardErrorLines, Timeout, CancellationToken.None);
    }

    [Fact]
    [Trait("Category", "LocalHostIntegration")]
    public async Task TwoClient_SharedOperation_IsVisibleAndCancellableAcrossControllers()
    {
        await using var scope = new LocalHostTeardownScope();
        await using var a = await scope.StartMcpAsync("client-a", useTestOperation: true);
        var host = await scope.WaitForIdentityAsync(a.StandardErrorLines);
        var controllerA = await scope.WaitForControllerAsync(host, a.Report.ProcessId, a.StandardErrorLines);
        await using var b = await scope.StartMcpAsync("client-b", useTestOperation: true);
        var controllerB = await scope.WaitForControllerAsync(host, b.Report.ProcessId, b.StandardErrorLines);
        await a.WriteStandardInputAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"run_smoke\",\"arguments\":{\"templateId\":\"empty-workspace\"}}}");
        var operation = await WaitForOperationAsync(scope.Client(host), item => item.InitiatedBy.ControllerSessionId == controllerA.ControllerSessionId && item.OperationKind == "run_smoke");
        await b.WriteStandardInputAsync($"{{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{{\"name\":\"cancel_operation\",\"arguments\":{{\"operationId\":\"{operation.OperationId}\"}}}}}}");
        var terminal = await WaitForOperationAsync(scope.Client(host), item => item.OperationId == operation.OperationId && item.Status is WorkspaceOperationStatus.Cancelled or WorkspaceOperationStatus.Succeeded);
        Assert.Equal(controllerA.ControllerSessionId, terminal.InitiatedBy.ControllerSessionId);
        Assert.True(terminal.LastEventSequence > 0);
        Assert.NotEqual(controllerA.ControllerSessionId, controllerB.ControllerSessionId);
        AssertProtocolOnly("A", a.StandardOutputLines);
        AssertProtocolOnly("B", b.StandardOutputLines);
    }

    [Fact]
    [Trait("Category", "LocalHostIntegration")]
    public async Task McpRestart_ControllerDisconnect_SeesOperationStartedByDisconnectedController()
    {
        await using var scope = new LocalHostTeardownScope();
        await using var a = await scope.StartMcpAsync("client-a", useTestOperation: true);
        var host = await scope.WaitForIdentityAsync(a.StandardErrorLines);
        var controllerA = await scope.WaitForControllerAsync(host, a.Report.ProcessId, a.StandardErrorLines);
        await a.WriteStandardInputAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"run_smoke\",\"arguments\":{\"templateId\":\"empty-workspace\"}}}");
        var operation = await WaitForOperationAsync(scope.Client(host), item => item.InitiatedBy.ControllerSessionId == controllerA.ControllerSessionId);
        await a.RequestGracefulShutdownByClosingStandardInputAsync(Timeout);
        await TeardownAssert.AssertControllerDisconnectedAsync(scope.Client(host), controllerA.ControllerSessionId, scope.Identity, a.StandardErrorLines, [], Timeout, CancellationToken.None);
        await using var c = await scope.StartMcpAsync("client-c", useTestOperation: true);
        var controllerC = await scope.WaitForControllerAsync(host, c.Report.ProcessId, c.StandardErrorLines);
        var visible = await WaitForOperationAsync(scope.Client(host), item => item.OperationId == operation.OperationId);
        Assert.Equal(controllerA.ControllerSessionId, visible.InitiatedBy.ControllerSessionId);
        Assert.NotEqual(controllerA.ControllerSessionId, controllerC.ControllerSessionId);
        await c.WriteStandardInputAsync($"{{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{{\"name\":\"cancel_operation\",\"arguments\":{{\"operationId\":\"{operation.OperationId}\"}}}}}}");
        await WaitForOperationAsync(scope.Client(host), item => item.OperationId == operation.OperationId && item.Status == WorkspaceOperationStatus.Cancelled);
    }

    private static async Task<WorkspaceOperationRecord> WaitForOperationAsync(LocalHostClient client, Func<WorkspaceOperationRecord, bool> predicate)
    {
        await using var disposable = client;
        var started = Stopwatch.StartNew();
        while (started.Elapsed < Timeout)
        {
            var operation = (await client.ListOperationsAsync()).FirstOrDefault(predicate);
            if (operation is not null) return operation;
            await Task.Delay(100);
        }
        throw new TimeoutException("Canonical operation was not observed within the bounded timeout.");
    }

    private static void AssertProtocolOnly(string client, IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(line));
            try
            {
                Assert.True(JsonDocument.TryParseValue(ref reader, out _), $"{client} stdout contained a non-protocol frame: {System.Text.Json.JsonSerializer.Serialize(line)}");
            }
            catch (JsonException)
            {
                Assert.Fail($"{client} stdout contained a non-protocol frame: {System.Text.Json.JsonSerializer.Serialize(line)}");
            }
        }
    }
}
