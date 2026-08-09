using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.LocalClient;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Xunit;

namespace OpenCode.Workspace.Mcp.Tests;

[Trait("Category", "LocalHostIntegration")]
public sealed class LocalHostTeardownIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    [Trait("Category", "McpTeardown")]
    public async Task OwnedLocalHost_Teardown_DisconnectsControllerAndReleasesOwnedState()
    {
        await using var scope = new LocalHostTeardownScope();
        PackagedProcessHarness? mcp = null;
        LocalHostProcessIdentity? host = null;
        try
        {
            TeardownAssert.AssertNoLiveState(scope.Identity);
            mcp = await scope.StartMcpAsync();
            host = await scope.WaitForIdentityAsync(mcp.StandardErrorLines);
            var controller = await scope.WaitForControllerAsync(host, mcp.StandardErrorLines);

            Assert.Equal(ControllerSessionStatus.Connected, controller.Status);
            await mcp.RequestGracefulShutdownByClosingStandardInputAsync(Timeout);
            mcp = null;

            await TeardownAssert.AssertControllerDisconnectedAsync(scope.Client(host), controller.ControllerSessionId, scope.Identity, [], [], Timeout, CancellationToken.None);
            await TeardownAssert.AssertProcessExitedAsync(host, [], [], Timeout, CancellationToken.None);
            await TeardownAssert.AssertDescriptorNotLiveAsync(scope.Identity, [], [], Timeout, CancellationToken.None);
            await TeardownAssert.AssertHostLockReleasedAsync(scope.Identity, [], [], Timeout, CancellationToken.None);
        }
        finally
        {
            if (mcp is not null)
            {
                await mcp.DisposeAsync();
            }

            if (host is not null)
            {
                await scope.StopIfOwnedAsync(host);
            }
        }
    }

    [Fact]
    [Trait("Category", "McpTeardown")]
    public async Task ExternalLocalHost_Teardown_PreservesSharedHostAndDisconnectsController()
    {
        await using var scope = new LocalHostTeardownScope();
        await using var externalHost = await scope.StartExternalHostAsync();
        PackagedProcessHarness? mcp = null;
        try
        {
            var host = await scope.WaitForIdentityAsync(externalHost.StandardErrorLines);
            await TeardownAssert.AssertDescriptorHealthyAsync(scope.Identity, host, [], externalHost.StandardErrorLines, Timeout, CancellationToken.None);
            mcp = await scope.StartMcpAsync();
            var controller = await scope.WaitForControllerAsync(host, mcp.StandardErrorLines);

            await mcp.RequestGracefulShutdownByClosingStandardInputAsync(Timeout);
            mcp = null;

            await TeardownAssert.AssertControllerDisconnectedAsync(scope.Client(host), controller.ControllerSessionId, scope.Identity, [], externalHost.StandardErrorLines, Timeout, CancellationToken.None);
            await TeardownAssert.AssertProcessStillRunningAsync(host, [], externalHost.StandardErrorLines, Timeout, CancellationToken.None);
            await TeardownAssert.AssertDescriptorHealthyAsync(scope.Identity, host, [], externalHost.StandardErrorLines, Timeout, CancellationToken.None);
            TeardownAssert.AssertHostLockHeld(scope.Identity);

            await externalHost.ForceKillAsync(Timeout);
            await TeardownAssert.AssertProcessExitedAsync(host, [], externalHost.StandardErrorLines, Timeout, CancellationToken.None);
            await TeardownAssert.AssertHostLockReleasedAsync(scope.Identity, [], externalHost.StandardErrorLines, Timeout, CancellationToken.None);
        }
        finally
        {
            if (mcp is not null)
            {
                await mcp.DisposeAsync();
            }
        }
    }

    [Fact]
    [Trait("Category", "McpTeardown")]
    public void TeardownArchitecture_DisconnectsBeforeOwnedClientDisposal_AndUsesNoGlobalKill()
    {
        var source = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Mcp", "LocalHostMcpProxyServices.cs"));
        var stop = source.IndexOf("public async Task StopAsync", StringComparison.Ordinal);
        var disconnect = source.IndexOf("DisconnectControllerSessionAsync", stop, StringComparison.Ordinal);
        var dispose = source.IndexOf("clients.DisposeAsync", stop, StringComparison.Ordinal);
        Assert.True(disconnect > stop && dispose > disconnect, "MCP shutdown must disconnect its controller before disposing its LocalHost client.");
        Assert.DoesNotContain(".Kill(", source, StringComparison.Ordinal);
    }
}

internal sealed class LocalHostTeardownScope : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "opencode-mcp-teardown", Guid.NewGuid().ToString("n"));
    private readonly List<LocalHostProcessIdentity> _ownedProcesses = [];
    private bool _disposed;

    public LocalHostTeardownScope()
    {
        WorkspaceStateRoot = Path.Combine(_root, "workspace-state");
        ArtifactsRoot = Path.Combine(_root, "artifacts");
        Identity = LocalHostProcessIdentity.ForStateRoot(Path.Combine(_root, "local-host-state"));
        LocalHostExecutableDirectory = Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Api", "bin", new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net10.0");
        Directory.CreateDirectory(WorkspaceStateRoot);
        Directory.CreateDirectory(ArtifactsRoot);
    }

    public string WorkspaceStateRoot { get; }
    public string ArtifactsRoot { get; }
    public string LocalHostExecutableDirectory { get; }
    public LocalHostProcessIdentity Identity { get; }

    public async Task<PackagedProcessHarness> StartExternalHostAsync(bool useTestOperation = false)
    {
        var dll = Path.Combine(LocalHostExecutableDirectory, "OpenCode.Workspace.LocalHost.dll");
        var host = await PackagedProcessHarness.StartAsync("external-local-host", "dotnet", [dll], _root, new Dictionary<string, string?>
        {
            ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{AllocateLoopbackPort()}",
            ["localHost__stateRoot"] = Identity.StateRoot,
            ["localHost__useTestOperation"] = useTestOperation ? "true" : "false",
        });
        return host;
    }

    public async Task<PackagedProcessHarness> StartMcpAsync(string clientName = "teardown-test", bool useTestOperation = false)
    {
        var launch = McpHostLaunch.Resolve();
        var mcp = await PackagedProcessHarness.StartAsync("mcp", launch.Command, launch.Arguments, _root, new Dictionary<string, string?>
        {
            ["mcp__catalogRoot"] = Path.Combine(TestPaths.RepositoryRoot, "catalog"),
            ["mcp__workspaceStateRoot"] = WorkspaceStateRoot,
            ["mcp__smokeArtifactsRoot"] = ArtifactsRoot,
            ["localHost__stateRoot"] = Identity.StateRoot,
            ["localHost__executableDirectory"] = LocalHostExecutableDirectory,
            ["localHost__useTestOperation"] = useTestOperation ? "true" : "false",
            ["MCP_CLIENT_NAME"] = clientName,
            ["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "None",
            ["Logging__LogLevel__Default"] = "None",
        });
        await mcp.WriteStandardInputAsync($"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{{}},\"clientInfo\":{{\"name\":\"{clientName}\",\"version\":\"1\"}}}}}}");
        return mcp;
    }

    public async Task<LocalHostProcessIdentity> WaitForIdentityAsync(IReadOnlyList<string> localHostStderr)
    {
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < Timeout)
        {
            if (File.Exists(Identity.DescriptorPath))
            {
                var descriptor = JsonSerializer.Deserialize<LocalHostDescriptor>(await File.ReadAllTextAsync(Identity.DescriptorPath), LocalHostContract.JsonOptions);
                if (descriptor is not null && descriptor.ProcessId > 0 && !string.IsNullOrWhiteSpace(descriptor.InstanceId))
                {
                    var process = Process.GetProcessById(descriptor.ProcessId);
                    var result = Identity with
                    {
                        ProcessId = descriptor.ProcessId,
                        ProcessStartedUtc = process.StartTime.ToUniversalTime(),
                        ExecutablePath = descriptor.ExecutablePath,
                        InstanceId = descriptor.InstanceId,
                        BaseUrl = descriptor.BaseUrl,
                    };
                    _ownedProcesses.Add(result);
                    return result;
                }
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(TeardownAssert.Diagnostics(Identity, null, [], localHostStderr, "LocalHost descriptor did not become available."));
    }

    public async Task<ControllerSessionRecord> WaitForControllerAsync(LocalHostProcessIdentity host, IReadOnlyList<string> mcpStderr)
        => await WaitForControllerAsync(host, null, mcpStderr);

    public async Task<ControllerSessionRecord> WaitForControllerAsync(LocalHostProcessIdentity host, int? processId, IReadOnlyList<string> mcpStderr)
    {
        var client = Client(host);
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < Timeout)
        {
            try
            {
                var controller = (await client.ListControllerSessionsAsync()).SingleOrDefault(item => item.ClientKind == "mcp" && item.Status == ControllerSessionStatus.Connected && (processId is null || item.Metadata.TryGetValue("processId", out var id) && id == processId.Value.ToString()));
                if (controller is not null)
                {
                    return controller;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or LocalHostClientException)
            {
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(TeardownAssert.Diagnostics(host, null, mcpStderr, [], "MCP controller registration did not appear."));
    }

    public LocalHostClient Client(LocalHostProcessIdentity host)
        => new(new HttpClient { BaseAddress = new Uri(host.BaseUrl) }, host.BaseUrl);

    public OpenCode.Workspace.Avalonia.Services.WorkspaceLocalHostApplicationService CreateAvaloniaLocalHostService()
        => new(new LocalHostClientOptions
        {
            StateRoot = Identity.StateRoot,
            LocalHostExecutableDirectory = LocalHostExecutableDirectory,
        });

    public async Task StopIfOwnedAsync(LocalHostProcessIdentity identity)
    {
        if (!identity.IsSameLiveProcess())
        {
            return;
        }

        using var process = Process.GetProcessById(identity.ProcessId);
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var process in _ownedProcesses.DistinctBy(item => item.ProcessId))
        {
            await StopIfOwnedAsync(process);
        }

        if (Directory.Exists(_root))
        {
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
            while (true)
            {
                try
                {
                    Directory.Delete(_root, recursive: true);
                    break;
                }
                catch (IOException) when (DateTimeOffset.UtcNow < deadline)
                {
                    await Task.Delay(100);
                }
            }
        }
    }

    private static int AllocateLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
}

internal sealed record LocalHostProcessIdentity(string StateRoot, string DescriptorPath, string LockPath)
{
    public int ProcessId { get; init; }
    public DateTime ProcessStartedUtc { get; init; }
    public string ExecutablePath { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;

    public static LocalHostProcessIdentity ForStateRoot(string stateRoot)
    {
        var paths = WorkspaceAppDataPaths.CreateLocalHostStatePathProvider(stateRoot);
        return new LocalHostProcessIdentity(paths.StateRoot, paths.DescriptorPath, paths.LockPath);
    }

    public bool IsSameLiveProcess()
    {
        try
        {
            using var process = Process.GetProcessById(ProcessId);
            return !process.HasExited && process.StartTime.ToUniversalTime() == ProcessStartedUtc;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

internal static class TeardownAssert
{
    public static void AssertNoLiveState(LocalHostProcessIdentity identity)
    {
        Assert.False(File.Exists(identity.DescriptorPath), Diagnostics(identity, null, [], [], "Descriptor already exists."));
        Assert.False(File.Exists(identity.LockPath), Diagnostics(identity, null, [], [], "Host lock already exists."));
    }

    public static async Task AssertProcessExitedAsync(LocalHostProcessIdentity identity, IReadOnlyList<string> mcpStderr, IReadOnlyList<string> hostStderr, TimeSpan timeout, CancellationToken cancellationToken)
        => await WaitAsync(() => !identity.IsSameLiveProcess(), identity, null, mcpStderr, hostStderr, timeout, cancellationToken, "Process did not exit.");

    public static async Task AssertProcessStillRunningAsync(LocalHostProcessIdentity identity, IReadOnlyList<string> mcpStderr, IReadOnlyList<string> hostStderr, TimeSpan timeout, CancellationToken cancellationToken)
        => await WaitAsync(identity.IsSameLiveProcess, identity, null, mcpStderr, hostStderr, timeout, cancellationToken, "Process did not remain running.");

    public static async Task AssertDescriptorNotLiveAsync(LocalHostProcessIdentity identity, IReadOnlyList<string> mcpStderr, IReadOnlyList<string> hostStderr, TimeSpan timeout, CancellationToken cancellationToken)
        => await WaitAsync(() => !File.Exists(identity.DescriptorPath) || !DescriptorIsHealthy(identity), identity, null, mcpStderr, hostStderr, timeout, cancellationToken, "Descriptor still resolves to a healthy LocalHost.");

    public static async Task AssertDescriptorHealthyAsync(LocalHostProcessIdentity identity, LocalHostProcessIdentity expected, IReadOnlyList<string> mcpStderr, IReadOnlyList<string> hostStderr, TimeSpan timeout, CancellationToken cancellationToken)
        => await WaitAsync(() => DescriptorIsHealthy(identity) && ReadDescriptor(identity)?.InstanceId == expected.InstanceId, identity, null, mcpStderr, hostStderr, timeout, cancellationToken, "Descriptor is not healthy for the expected LocalHost instance.");

    public static async Task AssertHostLockReleasedAsync(LocalHostProcessIdentity identity, IReadOnlyList<string> mcpStderr, IReadOnlyList<string> hostStderr, TimeSpan timeout, CancellationToken cancellationToken)
        => await WaitAsync(() => !File.Exists(identity.LockPath) || CanOpenExclusive(identity.LockPath), identity, null, mcpStderr, hostStderr, timeout, cancellationToken, "Host lock was not released.");

    public static void AssertHostLockHeld(LocalHostProcessIdentity identity)
        => Assert.True(File.Exists(identity.LockPath) && !CanOpenExclusive(identity.LockPath), Diagnostics(identity, null, [], [], "Host lock is not held."));

    public static async Task AssertControllerDisconnectedAsync(LocalHostClient client, string controllerSessionId, LocalHostProcessIdentity identity, IReadOnlyList<string> mcpStderr, IReadOnlyList<string> hostStderr, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await using var disposable = client;
        await WaitAsync(async () =>
        {
            ControllerSessionRecord? record;
            try
            {
                record = (await client.ListControllerSessionsAsync(cancellationToken)).SingleOrDefault(item => item.ControllerSessionId == controllerSessionId);
            }
            catch (HttpRequestException)
            {
                var path = Path.Combine(identity.StateRoot, "controller-sessions", $"{controllerSessionId}.json");
                record = File.Exists(path)
                    ? JsonSerializer.Deserialize<ControllerSessionRecord>(await File.ReadAllTextAsync(path, cancellationToken), LocalHostContract.JsonOptions)
                    : null;
            }
            return record is { Status: ControllerSessionStatus.Disconnected, DisconnectedUtc: not null } && record.ConnectedUtc != default;
        }, identity, controllerSessionId, mcpStderr, hostStderr, timeout, cancellationToken, "Controller session did not become canonically disconnected.");
    }

    public static string Diagnostics(LocalHostProcessIdentity identity, string? controllerSessionId, IReadOnlyList<string> mcpStderr, IReadOnlyList<string> hostStderr, string reason)
    {
        var descriptor = File.Exists(identity.DescriptorPath) ? File.ReadAllText(identity.DescriptorPath) : "<absent>";
        var processState = identity.ProcessId == 0 ? "<unknown>" : identity.IsSameLiveProcess() ? "running" : "exited-or-reused";
        return $"{reason} pid={identity.ProcessId} processStart={identity.ProcessStartedUtc:O} processState={processState} instanceId={identity.InstanceId} stateRoot={identity.StateRoot} descriptorPath={identity.DescriptorPath} descriptor={descriptor} lockPath={identity.LockPath} lockExists={File.Exists(identity.LockPath)} controllerSessionId={controllerSessionId ?? "<none>"} mcpStderr={string.Join(" | ", mcpStderr.TakeLast(20))} localHostStderr={string.Join(" | ", hostStderr.TakeLast(20))}";
    }

    private static async Task WaitAsync(Func<bool> condition, LocalHostProcessIdentity identity, string? controller, IReadOnlyList<string> mcpStderr, IReadOnlyList<string> hostStderr, TimeSpan timeout, CancellationToken cancellationToken, string failure)
        => await WaitAsync(() => Task.FromResult(condition()), identity, controller, mcpStderr, hostStderr, timeout, cancellationToken, failure);

    private static async Task WaitAsync(Func<Task<bool>> condition, LocalHostProcessIdentity identity, string? controller, IReadOnlyList<string> mcpStderr, IReadOnlyList<string> hostStderr, TimeSpan timeout, CancellationToken cancellationToken, string failure)
    {
        var started = Stopwatch.StartNew();
        while (started.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await condition()) return;
            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException(Diagnostics(identity, controller, mcpStderr, hostStderr, $"{failure} elapsed={started.Elapsed}"));
    }

    private static LocalHostDescriptor? ReadDescriptor(LocalHostProcessIdentity identity)
    {
        try { return File.Exists(identity.DescriptorPath) ? JsonSerializer.Deserialize<LocalHostDescriptor>(File.ReadAllText(identity.DescriptorPath), LocalHostContract.JsonOptions) : null; }
        catch { return null; }
    }

    private static bool DescriptorIsHealthy(LocalHostProcessIdentity identity)
    {
        var descriptor = ReadDescriptor(identity);
        if (descriptor is null) return false;
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(descriptor.BaseUrl), Timeout = TimeSpan.FromSeconds(2) };
            return client.GetAsync("/api/v1/local-host/health").GetAwaiter().GetResult().IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static bool CanOpenExclusive(string path)
    {
        try { using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; }
        catch (IOException) { return false; }
    }
}
