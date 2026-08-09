using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenCode.Workspace.Api;
using OpenCode.Workspace.LocalClient;
using OpenCode.Workspace.Mcp;
using OpenCode.Workspace.RemoteBridge;
using Xunit;

namespace OpenCode.Workspace.Api.IntegrationTests;

public sealed class RemoteBridgeEndToEndTests : IDisposable
{
    private readonly ApiIntegrationEnvironment _environment = new();

    [Fact]
    [Trait("Category", "FastIntegration")]
    [Trait("Category", "RemoteBridgeIntegration")]
    public async Task RealApplications_BridgeIoReconnectAndStop_PreserveCanonicalRuntime()
    {
        await WithTestRuntimeAsync(async (native, localFactory, localClient) =>
        {
            var (session, runtime) = await CreateRunningSessionAsync(localClient);
            await using var bridge = await RealBridgeHost.StartAsync(localFactory);

            var firstGrant = await bridge.CreateGrantAsync(session.InteractiveAgentSessionId, false);
            using (var socket = await bridge.ConnectAsync(firstGrant, afterSequence: 0))
            {
                var attached = await ReceiveControlAsync(socket);
                Assert.Equal("attached", attached.Type);
                Assert.Equal(runtime.RuntimeId, attached.TerminalRuntimeId);
                Assert.Equal(InteractiveTerminalRuntimeStatus.Running, attached.RuntimeStatus);

                var input = new byte[] { 0, 0xfe, 0x0d };
                await socket.SendAsync(input, WebSocketMessageType.Binary, true, default);
                Assert.Equal("ack", (await ReceiveControlAsync(socket)).Type);
                Assert.Equal(input, Assert.Single(native.ReceivedInput));

                native.EmitOutput([0x4f, 0x4e, 0x45]);
                var output = await ReceiveControlAsync(socket);
                Assert.Equal("output", output.Type);
                Assert.Equal("ONE", System.Text.Encoding.ASCII.GetString(await ReceiveBinaryAsync(socket)));
                await SendControlAsync(socket, new InteractiveTerminalWebSocketControl { Type = "ack", Sequence = output.Sequence });
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "presentation closed", default);
            }

            await WaitForDetachedAsync(localClient, session.InteractiveAgentSessionId);
            var disconnected = await localClient.GetInteractiveTerminalAsync(session.InteractiveAgentSessionId);
            AssertCanonicalIdentity(runtime, disconnected);
            Assert.Equal(InteractiveTerminalRuntimeStatus.Running, disconnected.Status);
            Assert.Equal(0, native.StopCount);

            native.EmitOutput([0x54, 0x57, 0x4f]);
            var secondGrant = await bridge.CreateGrantAsync(session.InteractiveAgentSessionId, false);
            using var reconnected = await bridge.ConnectAsync(secondGrant, afterSequence: 1);
            Assert.Equal("attached", (await ReceiveControlAsync(reconnected)).Type);
            var replayed = await ReceiveControlAsync(reconnected);
            Assert.Equal("output", replayed.Type);
            Assert.Equal(2, replayed.Sequence);
            Assert.Equal("TWO", System.Text.Encoding.ASCII.GetString(await ReceiveBinaryAsync(reconnected)));
            await SendControlAsync(reconnected, new InteractiveTerminalWebSocketControl { Type = "ack", Sequence = replayed.Sequence });

            AssertCanonicalIdentity(runtime, await localClient.GetInteractiveTerminalAsync(session.InteractiveAgentSessionId));
            await SendControlAsync(reconnected, new InteractiveTerminalWebSocketControl { Type = "stop" });
            var stoppedControl = await ReceiveControlAsync(reconnected);
            Assert.Equal("runtime_state", stoppedControl.Type);
            Assert.Equal(InteractiveTerminalRuntimeStatus.Exited, stoppedControl.RuntimeStatus);
            Assert.Equal(1, native.StopCount);
            await WaitForDetachedAsync(localClient, session.InteractiveAgentSessionId);
        });
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    [Trait("Category", "RemoteBridgeIntegration")]
    public async Task RealApplications_LocalAndRemoteExplicitTakeover_KeepNativeIdentity()
    {
        await WithTestRuntimeAsync(async (native, localFactory, localClient) =>
        {
            var (session, runtime) = await CreateRunningSessionAsync(localClient);
            var local = await localClient.AttachInteractiveSessionAsync(session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest
            {
                SessionId = session.InteractiveAgentSessionId,
                CommandId = Guid.NewGuid().ToString("n"),
                ClientInstanceId = "windows-local",
                AttachmentKind = InteractiveAttachmentKind.WindowsTerminal,
            });
            await localClient.ActivateInteractiveSessionAttachmentAsync(session.InteractiveAgentSessionId, local.Attachment.AttachmentId, new ActivateInteractiveSessionAttachmentRequest { AttachmentToken = local.AttachmentToken, HelperProcessId = 1234 });

            await using var bridge = await RealBridgeHost.StartAsync(localFactory);
            var grantTask = bridge.CreateGrantAsync(session.InteractiveAgentSessionId, true, local.Attachment.AttachmentId);
            var heartbeat = await WaitForDetachRequestAsync(localClient, session.InteractiveAgentSessionId, local);
            Assert.Equal(InteractiveAttachmentControlAction.Detach, heartbeat.RequestedAction);
            await localClient.ReportInteractiveSessionAttachmentProcessExitAsync(session.InteractiveAgentSessionId, local.Attachment.AttachmentId, new InteractiveSessionAttachmentProcessExitRequest { AttachmentToken = local.AttachmentToken, Outcome = "detach_requested" });

            var grant = await grantTask;
            using var remote = await bridge.ConnectAsync(grant, 0, takeover: true);
            var attached = await ReceiveControlAsync(remote);
            Assert.Equal("attached", attached.Type);
            AssertCanonicalIdentity(runtime, await localClient.GetInteractiveTerminalAsync(session.InteractiveAgentSessionId));

            var localTakeoverTask = localClient.AttachInteractiveSessionAsync(session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest
            {
                SessionId = session.InteractiveAgentSessionId,
                CommandId = Guid.NewGuid().ToString("n"),
                ClientInstanceId = "windows-local-again",
                AttachmentKind = InteractiveAttachmentKind.WindowsTerminal,
                RequestTransfer = true,
                ExpectedAttachmentId = attached.AttachmentId,
            });
            var detach = await ReceiveControlAsync(remote, TimeSpan.FromSeconds(5));
            Assert.Equal("detach", detach.Type);
            var localAgain = await localTakeoverTask;
            await localClient.ActivateInteractiveSessionAttachmentAsync(session.InteractiveAgentSessionId, localAgain.Attachment.AttachmentId, new ActivateInteractiveSessionAttachmentRequest { AttachmentToken = localAgain.AttachmentToken, HelperProcessId = 5678 });

            var unchanged = await localClient.GetInteractiveTerminalAsync(session.InteractiveAgentSessionId);
            AssertCanonicalIdentity(runtime, unchanged);
            Assert.Equal(localAgain.Attachment.AttachmentId, unchanged.ActiveAttachmentId);
            Assert.Equal(0, native.StopCount);
            Assert.Empty(native.ReceivedInput);

            await localClient.ReportInteractiveSessionAttachmentProcessExitAsync(session.InteractiveAgentSessionId, localAgain.Attachment.AttachmentId, new InteractiveSessionAttachmentProcessExitRequest { AttachmentToken = localAgain.AttachmentToken, Outcome = "presentation_closed" });
            await localClient.StopInteractiveTerminalAsync(session.InteractiveAgentSessionId);
            Assert.Equal(1, native.StopCount);
        });
    }

    private async Task WithTestRuntimeAsync(Func<FakeInteractiveTerminalRuntime, ApiTestFactory, LocalHostClient, Task> test)
    {
        var previous = Environment.GetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME");
        Environment.SetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME", "1");
        try
        {
            var native = new FakeInteractiveTerminalRuntime();
            await using var factory = _environment.CreateFactory(services =>
            {
                services.ReplaceSingleton<IOpenCodeWorkspaceMcpService>(new FakeApiService());
                services.RemoveAll<InteractiveTerminalRuntimeService>();
                services.AddSingleton(provider => new InteractiveTerminalRuntimeService(
                    provider.GetRequiredService<InteractiveAgentSessionService>(),
                    provider.GetRequiredService<IOpenCodeWorkspaceMcpService>(),
                    provider.GetRequiredService<ISystemClock>(),
                    provider.GetRequiredService<LocalHostStateStore>(),
                    runtimeFactory: () => native));
            });
            using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1") });
            var localClient = new LocalHostClient(http, "http://127.0.0.1");
            await test(native, factory, localClient);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME", previous);
        }
    }

    private static async Task<(InteractiveAgentSessionRecord Session, InteractiveTerminalRuntimeRecord Runtime)> CreateRunningSessionAsync(LocalHostClient client)
    {
        var session = await client.CreateInteractiveAgentSessionAsync("alpha", new CreateInteractiveAgentSessionRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = "alpha", Title = "Bridge integration" });
        var identity = await client.AttachInteractiveSessionAsync(session.InteractiveAgentSessionId, new AttachInteractiveSessionRequest
        {
            SessionId = session.InteractiveAgentSessionId,
            CommandId = Guid.NewGuid().ToString("n"),
            ClientInstanceId = "identity-bootstrap",
            AttachmentKind = InteractiveAttachmentKind.WindowsTerminal,
        });
        await client.ActivateInteractiveSessionAttachmentAsync(session.InteractiveAgentSessionId, identity.Attachment.AttachmentId, new ActivateInteractiveSessionAttachmentRequest { AttachmentToken = identity.AttachmentToken, HelperProcessId = 100 });
        session = await client.ReportInteractiveSessionProviderSessionAsync(session.InteractiveAgentSessionId, identity.Attachment.AttachmentId, new InteractiveSessionAttachmentProviderSessionRequest { AttachmentToken = identity.AttachmentToken, ProviderSessionId = "provider-session-wave-8c", IdentitySource = ProviderSessionIdentitySource.DirectHandshake });
        await client.ReportInteractiveSessionAttachmentProcessExitAsync(session.InteractiveAgentSessionId, identity.Attachment.AttachmentId, new InteractiveSessionAttachmentProcessExitRequest { AttachmentToken = identity.AttachmentToken, Outcome = "presentation_closed" });
        var runtime = await client.StartInteractiveTerminalAsync(session.InteractiveAgentSessionId, new StartInteractiveTerminalRequest());
        Assert.Equal("provider-session-wave-8c", runtime.ProviderSessionId);
        return (session, runtime);
    }

    private static void AssertCanonicalIdentity(InteractiveTerminalRuntimeRecord expected, InteractiveTerminalRuntimeRecord actual)
    {
        Assert.Equal(expected.RuntimeId, actual.RuntimeId);
        Assert.Equal(expected.ProcessId, actual.ProcessId);
        Assert.Equal(expected.ProviderSessionId, actual.ProviderSessionId);
    }

    private static async Task<InteractiveSessionAttachmentHeartbeatResult> WaitForDetachRequestAsync(LocalHostClient client, string sessionId, InteractiveSessionAttachResult attachment)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var result = await client.HeartbeatInteractiveSessionAttachmentAsync(sessionId, attachment.Attachment.AttachmentId, new InteractiveSessionAttachmentHeartbeatRequest { AttachmentToken = attachment.AttachmentToken }, timeout.Token);
            if (result.RequestedAction == InteractiveAttachmentControlAction.Detach) return result;
            await Task.Delay(25, timeout.Token);
        }
    }

    private static async Task WaitForDetachedAsync(LocalHostClient client, string sessionId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            if (string.IsNullOrEmpty((await client.GetInteractiveAgentSessionAsync(sessionId, timeout.Token)).ActiveAttachmentId)) return;
            await Task.Delay(25, timeout.Token);
        }
        throw new TimeoutException("Terminal presentation did not detach.");
    }

    private static Task SendControlAsync(WebSocket socket, InteractiveTerminalWebSocketControl control)
        => socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(control, LocalHostContract.JsonOptions), WebSocketMessageType.Text, true, default);

    private static async Task<InteractiveTerminalWebSocketControl> ReceiveControlAsync(WebSocket socket, TimeSpan? timeout = null)
        => JsonSerializer.Deserialize<InteractiveTerminalWebSocketControl>(await ReceiveAsync(socket, WebSocketMessageType.Text, timeout), LocalHostContract.JsonOptions)!;

    private static Task<byte[]> ReceiveBinaryAsync(WebSocket socket) => ReceiveAsync(socket, WebSocketMessageType.Binary);

    private static async Task<byte[]> ReceiveAsync(WebSocket socket, WebSocketMessageType expected, TimeSpan? duration = null)
    {
        using var timeout = new CancellationTokenSource(duration ?? TimeSpan.FromSeconds(5));
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, timeout.Token);
            Assert.Equal(expected, result.MessageType);
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return stream.ToArray();
    }

    public void Dispose() => _environment.Dispose();
}

internal sealed class TestServerRemoteBridgeBackend : IRemoteBridgeBackend
{
    private readonly ApiTestFactory _factory;
    private readonly LocalHostClient _client;

    public TestServerRemoteBridgeBackend(ApiTestFactory factory)
    {
        _factory = factory;
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1") });
        _client = new LocalHostClient(http, "http://127.0.0.1");
    }

    public Task<IReadOnlyList<InteractiveAgentSessionRecord>> ListSessionsAsync(CancellationToken cancellationToken) => _client.ListInteractiveAgentSessionsAsync(cancellationToken: cancellationToken);
    public Task<InteractiveAgentSessionRecord> GetSessionAsync(string sessionId, CancellationToken cancellationToken) => _client.GetInteractiveAgentSessionAsync(sessionId, cancellationToken);
    public Task<InteractiveTerminalRuntimeRecord> GetTerminalAsync(string sessionId, CancellationToken cancellationToken) => _client.GetInteractiveTerminalAsync(sessionId, cancellationToken);

    public async Task<BackendAttachment> AttachAsync(string sessionId, bool takeover, string expectedAttachmentId, CancellationToken cancellationToken)
    {
        var clientId = $"test-remote-bridge-{Guid.NewGuid():n}";
        var result = await _client.AttachInteractiveSessionAsync(sessionId, new AttachInteractiveSessionRequest
        {
            SessionId = sessionId,
            CommandId = Guid.NewGuid().ToString("n"),
            ClientInstanceId = clientId,
            AttachmentKind = InteractiveAttachmentKind.WebTerminal,
            RequestTransfer = takeover,
            ExpectedAttachmentId = expectedAttachmentId,
        }, cancellationToken);
        return new BackendAttachment(sessionId, result.TerminalRuntime.RuntimeId, result.Attachment.AttachmentId, result.AttachmentToken, clientId);
    }

    public async Task<WebSocket> ConnectTerminalAsync(BackendAttachment attachment, long afterSequence, CancellationToken cancellationToken)
    {
        var client = _factory.Server.CreateWebSocketClient();
        client.SubProtocols.Add(InteractiveTerminalWebSocketService.SubProtocol);
        client.ConfigureRequest = request => request.Headers.Origin = "http://127.0.0.1";
        var socket = await client.ConnectAsync(new Uri($"ws://127.0.0.1/api/v1/local-host/interactive-agent-sessions/{attachment.SessionId}/terminal/ws"), cancellationToken);
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new InteractiveTerminalWebSocketHello
        {
            InteractiveAgentSessionId = attachment.SessionId,
            TerminalRuntimeId = attachment.RuntimeId,
            AttachmentId = attachment.AttachmentId,
            AttachmentToken = attachment.AttachmentToken,
            AfterSequence = afterSequence,
        }, LocalHostContract.JsonOptions), WebSocketMessageType.Text, true, cancellationToken);
        return socket;
    }
}

internal sealed class RealBridgeHost : IAsyncDisposable
{
    private readonly WebApplication _application;
    private readonly HttpClient _client;

    private RealBridgeHost(WebApplication application)
    {
        _application = application;
        _client = application.GetTestClient();
    }

    public static async Task<RealBridgeHost> StartAsync(ApiTestFactory localFactory)
    {
        var backend = new TestServerRemoteBridgeBackend(localFactory);
        var app = RemoteBridgeApplication.Build(customize: builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RemoteAccess:Enabled"] = "true",
                ["RemoteAccess:PublicOrigin"] = "https://remote.example.test",
                ["RemoteAccess:ListenerUrl"] = "http://127.0.0.1:38443",
                ["RemoteAccess:LocalHostBaseUrl"] = "http://127.0.0.1",
                ["Cloudflare:TeamDomain"] = "https://team.cloudflareaccess.com",
                ["Cloudflare:Issuer"] = "https://team.cloudflareaccess.com",
                ["Cloudflare:Audience"] = "integration-audience",
            });
            builder.Services.AddSingleton<ICloudflareAccessJwtValidator, IntegrationCloudflareValidator>();
            builder.Services.AddSingleton<IRemoteBridgeBackend>(backend);
        });
        await app.StartAsync();
        return new RealBridgeHost(app);
    }

    public async Task<AttachmentGrantResponse> CreateGrantAsync(string sessionId, bool takeover, string expectedAttachmentId = "")
    {
        using var request = Request(HttpMethod.Post, $"/api/v1/remote/sessions/{sessionId}/attachment-grants", origin: true);
        request.Content = JsonContent.Create(new AttachmentGrantRequest(takeover, expectedAttachmentId));
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AttachmentGrantResponse>())!;
    }

    public async Task<WebSocket> ConnectAsync(AttachmentGrantResponse grant, long afterSequence, bool takeover = false)
    {
        var client = _application.GetTestServer().CreateWebSocketClient();
        client.SubProtocols.Add(RemoteTerminalProxy.SubProtocol);
        client.ConfigureRequest = request =>
        {
            request.Headers.Host = "remote.example.test";
            request.Headers["Origin"] = "https://remote.example.test";
            request.Headers["Cf-Access-Jwt-Assertion"] = "integration-valid";
        };
        var socket = await client.ConnectAsync(new Uri($"ws://remote.example.test/api/v1/remote/sessions/{grant.SessionId}/terminal/ws"), default);
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new RemoteBridgeHello { SessionId = grant.SessionId, RuntimeId = grant.RuntimeId, GrantToken = grant.GrantToken, AfterSequence = afterSequence, Takeover = takeover }, LocalHostContract.JsonOptions), WebSocketMessageType.Text, true, default);
        return socket;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, bool origin)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Host = "remote.example.test";
        request.Headers.Add("Cf-Access-Jwt-Assertion", "integration-valid");
        if (origin) request.Headers.Add("Origin", "https://remote.example.test");
        return request;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _application.StopAsync();
        await _application.DisposeAsync();
    }
}

internal sealed class IntegrationCloudflareValidator : ICloudflareAccessJwtValidator
{
    public Task<ClaimsPrincipal?> ValidateAsync(string assertion, CancellationToken cancellationToken)
        => Task.FromResult<ClaimsPrincipal?>(assertion == "integration-valid"
            ? new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "integration-user")], "integration"))
            : null);
}
