using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenCode.Workspace.Api;
using OpenCode.Workspace.LocalClient;
using OpenCode.Workspace.Mcp;
using Xunit;

namespace OpenCode.Workspace.Api.IntegrationTests;

public sealed class InteractiveTerminalWebSocketTests : IDisposable
{
    private readonly ApiIntegrationEnvironment _environment = new();

    [Fact]
    [Trait("Category", "FastIntegration")]
    [Trait("Category", "WebSocketIntegration")]
    public async Task RealWebSocket_PreservesBinaryIo_Resize_AndRuntimeAcrossReconnect()
    {
        var previous = Environment.GetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME");
        Environment.SetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME", "1");
        try
        {
            var native = new FakeInteractiveTerminalRuntime();
            await using var factory = CreateFactory(native);
            using var client = factory.CreateClient();
            var session = await CreateSessionAsync(client);
            var runtime = await PostAsync<InteractiveTerminalRuntimeRecord>(client, $"/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/terminal/start", new StartInteractiveTerminalRequest());
            var first = await AttachAsync(client, session.InteractiveAgentSessionId, "browser-one");

            using (var socket = await ConnectAsync(factory, session.InteractiveAgentSessionId))
            {
                await SendHelloAsync(socket, session.InteractiveAgentSessionId, runtime.RuntimeId, first.Attachment.AttachmentId, first.AttachmentToken, 0);
                Assert.Equal("attached", (await ReceiveControlAsync(socket)).Type);

                var outputBytes = new byte[] { 0x00, 0x1b, 0x5b, 0x31, 0xff };
                native.EmitOutput(outputBytes);
                var output = await ReceiveControlAsync(socket);
                Assert.Equal("output", output.Type);
                Assert.Equal(outputBytes, await ReceiveBinaryAsync(socket));

                var inputBytes = new byte[] { 0x00, 0xfe, 0x0d };
                await socket.SendAsync(inputBytes, WebSocketMessageType.Binary, true, CancellationToken.None);
                Assert.Equal("ack", (await ReceiveControlAsync(socket)).Type);
                Assert.Equal(inputBytes, Assert.Single(native.ReceivedInput));

                await SendControlAsync(socket, new InteractiveTerminalWebSocketControl { Type = "resize", Columns = 132, Rows = 41 });
                Assert.Equal("ack", (await ReceiveControlAsync(socket)).Type);
                Assert.Equal(new InteractiveTerminalDimensions { Columns = 132, Rows = 41 }, Assert.Single(native.ResizeHistory));

                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test drop", CancellationToken.None);
            }

            await WaitForDetachedAsync(client, session.InteractiveAgentSessionId);
            var stillRunning = await GetAsync<InteractiveTerminalRuntimeRecord>(client, $"/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/terminal");
            Assert.Equal(InteractiveTerminalRuntimeStatus.Running, stillRunning.Status);
            Assert.Equal(runtime.RuntimeId, stillRunning.RuntimeId);
            Assert.Equal(runtime.ProcessId, stillRunning.ProcessId);

            native.EmitOutput([0x52, 0x45, 0x53, 0x55, 0x4d, 0x45]);
            var second = await AttachAsync(client, session.InteractiveAgentSessionId, "browser-two");
            using var reconnected = await ConnectAsync(factory, session.InteractiveAgentSessionId);
            await SendHelloAsync(reconnected, session.InteractiveAgentSessionId, runtime.RuntimeId, second.Attachment.AttachmentId, second.AttachmentToken, 1);
            Assert.Equal("attached", (await ReceiveControlAsync(reconnected)).Type);
            Assert.Equal("output", (await ReceiveControlAsync(reconnected)).Type);
            Assert.Equal("RESUME", System.Text.Encoding.ASCII.GetString(await ReceiveBinaryAsync(reconnected)));

            await SendControlAsync(reconnected, new InteractiveTerminalWebSocketControl { Type = "stop" });
            Assert.Equal("runtime_state", (await ReceiveControlAsync(reconnected)).Type);
            var stopped = await GetAsync<InteractiveTerminalRuntimeRecord>(client, $"/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/terminal");
            Assert.Equal(InteractiveTerminalRuntimeStatus.Exited, stopped.Status);
            Assert.Equal(1, native.StopCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME", previous);
        }
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    [Trait("Category", "WebSocketIntegration")]
    public async Task WebSocket_RejectsCrossOriginWrongRuntimeAndStaleCredential()
    {
        var previous = Environment.GetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME");
        Environment.SetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME", "1");
        try
        {
            var native = new FakeInteractiveTerminalRuntime();
            await using var factory = CreateFactory(native);
            using var client = factory.CreateClient();
            var session = await CreateSessionAsync(client);
            var runtime = await PostAsync<InteractiveTerminalRuntimeRecord>(client, $"/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/terminal/start", new StartInteractiveTerminalRequest());
            var attached = await AttachAsync(client, session.InteractiveAgentSessionId, "browser-auth");

            using (var crossOriginHttp = factory.CreateClient())
            {
                crossOriginHttp.DefaultRequestHeaders.Host = "127.0.0.1";
                crossOriginHttp.DefaultRequestHeaders.Add("Origin", "http://attacker.invalid");
                var response = await crossOriginHttp.PostAsJsonAsync($"/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/terminal/stop", new { });
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }

            var crossOrigin = factory.Server.CreateWebSocketClient();
            crossOrigin.SubProtocols.Add(InteractiveTerminalWebSocketService.SubProtocol);
            crossOrigin.ConfigureRequest = request => request.Headers.Origin = "http://attacker.invalid";
            var rejected = await Assert.ThrowsAnyAsync<Exception>(() => crossOrigin.ConnectAsync(new Uri($"ws://127.0.0.1/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/terminal/ws"), CancellationToken.None));
            Assert.NotNull(rejected);

            using var socket = await ConnectAsync(factory, session.InteractiveAgentSessionId);
            await SendHelloAsync(socket, session.InteractiveAgentSessionId, "wrong-runtime", attached.Attachment.AttachmentId, attached.AttachmentToken, 0);
            var error = await ReceiveControlAsync(socket);
            Assert.Equal("error", error.Type);
            Assert.Equal("terminal_runtime_mismatch", error.Code);

            using var stale = await ConnectAsync(factory, session.InteractiveAgentSessionId);
            await SendHelloAsync(stale, session.InteractiveAgentSessionId, runtime.RuntimeId, attached.Attachment.AttachmentId, "wrong-token", 0);
            error = await ReceiveControlAsync(stale);
            Assert.Equal("invalid_attachment_credential", error.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME", previous);
        }
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    [Trait("Category", "WebSocketIntegration")]
    public async Task BrowserAndWindowsAttachments_ExplicitlyTakeOverSameRuntime()
    {
        var previous = Environment.GetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME");
        Environment.SetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME", "1");
        try
        {
            var native = new FakeInteractiveTerminalRuntime();
            await using var factory = CreateFactory(native);
            using var client = factory.CreateClient();
            var session = await CreateSessionAsync(client);
            var runtime = await PostAsync<InteractiveTerminalRuntimeRecord>(client, $"/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/terminal/start", new StartInteractiveTerminalRequest());
            var browser = await AttachAsync(client, session.InteractiveAgentSessionId, "browser-owner");
            using var socket = await ConnectAsync(factory, session.InteractiveAgentSessionId);
            await SendHelloAsync(socket, session.InteractiveAgentSessionId, runtime.RuntimeId, browser.Attachment.AttachmentId, browser.AttachmentToken, 0);
            Assert.Equal("attached", (await ReceiveControlAsync(socket)).Type);

            var windowsTask = PostAsync<InteractiveSessionAttachResult>(client, $"/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/attachments", new AttachInteractiveSessionRequest
            {
                SessionId = session.InteractiveAgentSessionId,
                CommandId = Guid.NewGuid().ToString("n"),
                ClientInstanceId = "windows-owner",
                AttachmentKind = InteractiveAttachmentKind.WindowsTerminal,
                RequestTransfer = true,
            });
            Assert.Equal("detach", (await ReceiveControlAsync(socket)).Type);
            var windows = await windowsTask;
            await PostAsync<InteractiveSessionAttachmentActivationResult>(client, $"/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/attachments/{windows.Attachment.AttachmentId}/activate", new ActivateInteractiveSessionAttachmentRequest { AttachmentToken = windows.AttachmentToken, HelperProcessId = 1234 });
            await PostAsync<InteractiveTerminalRuntimeRecord>(client, $"/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/terminal/input", new TerminalInputRequest { AttachmentId = windows.Attachment.AttachmentId, AttachmentToken = windows.AttachmentToken, DataBase64 = Convert.ToBase64String([0x57]) });
            Assert.Equal([0x57], Assert.Single(native.ReceivedInput));

            await PostAsync<InteractiveAgentSessionRecord>(client, $"/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/attachments/{windows.Attachment.AttachmentId}/process-exit", new InteractiveSessionAttachmentProcessExitRequest { AttachmentToken = windows.AttachmentToken, Outcome = "presentation_closed" });
            var browserAgain = await AttachAsync(client, session.InteractiveAgentSessionId, "browser-owner-two");
            Assert.Equal(InteractiveAttachmentKind.WebTerminal, browserAgain.Attachment.Kind);
            Assert.Equal(runtime.RuntimeId, (await GetAsync<InteractiveTerminalRuntimeRecord>(client, $"/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/terminal")).RuntimeId);
            Assert.Equal(runtime.ProcessId, native.Record.ProcessId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME", previous);
        }
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    [Trait("Category", "WebSocketIntegration")]
    public async Task RolledOutputHistory_SendsGapThenEarliestRetainedBinaryChunk()
    {
        var previous = Environment.GetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME");
        Environment.SetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME", "1");
        try
        {
            var native = new FakeInteractiveTerminalRuntime();
            await using var factory = CreateFactory(native);
            using var client = factory.CreateClient();
            var session = await CreateSessionAsync(client);
            var runtime = await PostAsync<InteractiveTerminalRuntimeRecord>(client, $"/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/terminal/start", new StartInteractiveTerminalRequest());
            var attached = await AttachAsync(client, session.InteractiveAgentSessionId, "browser-gap");
            native.EmitOutput(new byte[700_000]);
            native.EmitOutput(Enumerable.Repeat((byte)0x42, 700_000).ToArray());

            using var socket = await ConnectAsync(factory, session.InteractiveAgentSessionId);
            await SendHelloAsync(socket, session.InteractiveAgentSessionId, runtime.RuntimeId, attached.Attachment.AttachmentId, attached.AttachmentToken, 0);
            Assert.Equal("attached", (await ReceiveControlAsync(socket)).Type);
            var gap = await ReceiveControlAsync(socket);
            Assert.Equal("gap", gap.Type);
            Assert.Equal(2, gap.EarliestAvailableSequence);
            var output = await ReceiveControlAsync(socket);
            Assert.Equal(2, output.Sequence);
            var bytes = await ReceiveBinaryAsync(socket);
            Assert.Equal(700_000, bytes.Length);
            Assert.All(bytes, item => Assert.Equal(0x42, item));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME", previous);
        }
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    [Trait("Category", "WebSocketIntegration")]
    public async Task ReplayedAttachmentCredential_CannotCreateSecondSocketOrRevokeFirst()
    {
        var previous = Environment.GetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME");
        Environment.SetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME", "1");
        try
        {
            var native = new FakeInteractiveTerminalRuntime();
            await using var factory = CreateFactory(native);
            using var client = factory.CreateClient();
            var session = await CreateSessionAsync(client);
            var runtime = await PostAsync<InteractiveTerminalRuntimeRecord>(client, $"/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/terminal/start", new StartInteractiveTerminalRequest());
            var attached = await AttachAsync(client, session.InteractiveAgentSessionId, "browser-replay");
            using var first = await ConnectAsync(factory, session.InteractiveAgentSessionId);
            await SendHelloAsync(first, session.InteractiveAgentSessionId, runtime.RuntimeId, attached.Attachment.AttachmentId, attached.AttachmentToken, 0);
            Assert.Equal("attached", (await ReceiveControlAsync(first)).Type);

            using var replay = await ConnectAsync(factory, session.InteractiveAgentSessionId);
            await SendHelloAsync(replay, session.InteractiveAgentSessionId, runtime.RuntimeId, attached.Attachment.AttachmentId, attached.AttachmentToken, 0);
            var rejected = await ReceiveControlAsync(replay);
            Assert.Equal("attachment_already_connected", rejected.Code);

            await first.SendAsync(new byte[] { 0x41 }, WebSocketMessageType.Binary, true, CancellationToken.None);
            Assert.Equal("ack", (await ReceiveControlAsync(first)).Type);
            Assert.Equal([0x41], Assert.Single(native.ReceivedInput));
            Assert.Equal(attached.Attachment.AttachmentId, (await GetAsync<InteractiveAgentSessionRecord>(client, $"/api/v1/interactive-agent-sessions/{session.InteractiveAgentSessionId}")).ActiveAttachmentId);
            Assert.Equal(attached.Attachment.AttachmentId, (await GetAsync<InteractiveTerminalRuntimeRecord>(client, $"/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/terminal")).ActiveAttachmentId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME", previous);
        }
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    [Trait("Category", "WebSocketIntegration")]
    public async Task MissingOutputAcknowledgement_DisconnectsClientButKeepsRuntimeRunning()
    {
        var previous = Environment.GetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME");
        Environment.SetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME", "1");
        try
        {
            var native = new FakeInteractiveTerminalRuntime();
            await using var factory = CreateFactory(native);
            using var client = factory.CreateClient();
            var session = await CreateSessionAsync(client);
            var runtime = await PostAsync<InteractiveTerminalRuntimeRecord>(client, $"/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/terminal/start", new StartInteractiveTerminalRequest());
            var attached = await AttachAsync(client, session.InteractiveAgentSessionId, "browser-slow");
            using var socket = await ConnectAsync(factory, session.InteractiveAgentSessionId);
            await SendHelloAsync(socket, session.InteractiveAgentSessionId, runtime.RuntimeId, attached.Attachment.AttachmentId, attached.AttachmentToken, 0);
            Assert.Equal("attached", (await ReceiveControlAsync(socket)).Type);
            native.EmitOutput([0x42]);
            Assert.Equal("output", (await ReceiveControlAsync(socket)).Type);
            Assert.Equal([0x42], await ReceiveBinaryAsync(socket));

            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            {
                var closed = await socket.ReceiveAsync(new byte[128], timeout.Token);
                Assert.Equal(WebSocketMessageType.Close, closed.MessageType);
            }
            await WaitForDetachedAsync(client, session.InteractiveAgentSessionId);
            Assert.Equal(InteractiveTerminalRuntimeStatus.Running, (await GetAsync<InteractiveTerminalRuntimeRecord>(client, $"/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/terminal")).Status);
            Assert.Equal(0, native.StopCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME", previous);
        }
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    [Trait("Category", "WebSocketIntegration")]
    public async Task RuntimeExit_DrainsOutputThatArrivesAfterTerminalStatus()
    {
        var previous = Environment.GetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME");
        Environment.SetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME", "1");
        try
        {
            var native = new FakeInteractiveTerminalRuntime();
            await using var factory = CreateFactory(native);
            using var client = factory.CreateClient();
            var session = await CreateSessionAsync(client);
            var runtime = await PostAsync<InteractiveTerminalRuntimeRecord>(client, $"/api/v1/local-host/interactive-agent-sessions/{session.InteractiveAgentSessionId}/terminal/start", new StartInteractiveTerminalRequest());
            var attached = await AttachAsync(client, session.InteractiveAgentSessionId, "browser-exit-drain");
            using var socket = await ConnectAsync(factory, session.InteractiveAgentSessionId);
            await SendHelloAsync(socket, session.InteractiveAgentSessionId, runtime.RuntimeId, attached.Attachment.AttachmentId, attached.AttachmentToken, 0);
            Assert.Equal("attached", (await ReceiveControlAsync(socket)).Type);

            native.Exit();
            await Task.Delay(100);
            native.EmitOutput([0x46, 0x49, 0x4e, 0x41, 0x4c]);
            var output = await ReceiveControlAsync(socket);
            Assert.Equal("output", output.Type);
            Assert.Equal("FINAL", System.Text.Encoding.ASCII.GetString(await ReceiveBinaryAsync(socket)));
            await SendControlAsync(socket, new InteractiveTerminalWebSocketControl { Type = "ack", Sequence = output.Sequence });
            Assert.Equal("runtime_state", (await ReceiveControlAsync(socket)).Type);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME", previous);
        }
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    public void TransportSource_HasBoundedSendAndNoProcessLaunchSurface()
    {
        var source = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Api", "InteractiveTerminalWebSocketService.cs"));
        Assert.Contains("SendTimeout", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Channel<", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartInteractiveTerminalAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Executable", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkingDirectory", source, StringComparison.Ordinal);
        Assert.True(InteractiveTerminalWebSocketService.TryGetLoopbackHost("127.0.0.1", out _));
        Assert.True(InteractiveTerminalWebSocketService.TryGetLoopbackHost("::1", out _));
        Assert.False(InteractiveTerminalWebSocketService.TryGetLoopbackHost("0.0.0.0", out _));
        Assert.False(InteractiveTerminalWebSocketService.TryGetLoopbackHost("::", out _));
        var program = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "src", "OpenCode.Workspace.Api", "Program.cs"));
        Assert.Contains("ConfigureLoopbackListener(builder)", program, StringComparison.Ordinal);
        Assert.Contains("LocalHost listeners must bind to an explicit loopback IP address", program, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "FastIntegration")]
    public async Task BrowserPage_IsPackagedLocally_AndHardened()
    {
        await using var factory = _environment.CreateFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/terminal/example-session");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("/terminal/terminal.js", html);
        Assert.Contains("/terminal/vendor/xterm.js", html);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/terminal/terminal.js")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/terminal/terminal.css")).StatusCode);
        var xtermAsset = await client.GetAsync("/terminal/vendor/xterm.js");
        Assert.True(xtermAsset.IsSuccessStatusCode, $"xterm.js asset returned {(int)xtermAsset.StatusCode}: {await xtermAsset.Content.ReadAsStringAsync()}");
    }

    private ApiTestFactory CreateFactory(FakeInteractiveTerminalRuntime native)
        => _environment.CreateFactory(services =>
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

    private static async Task<InteractiveAgentSessionRecord> CreateSessionAsync(HttpClient client)
        => await PostAsync<InteractiveAgentSessionRecord>(client, "/api/v1/local-host/workspaces/alpha/interactive-sessions", new CreateInteractiveAgentSessionRequest { CommandId = Guid.NewGuid().ToString("n"), WorkspaceId = "alpha", Title = "Browser terminal" });

    private static Task<InteractiveSessionAttachResult> AttachAsync(HttpClient client, string sessionId, string clientId)
        => PostAsync<InteractiveSessionAttachResult>(client, $"/api/v1/local-host/interactive-agent-sessions/{sessionId}/attachments", new AttachInteractiveSessionRequest { SessionId = sessionId, CommandId = Guid.NewGuid().ToString("n"), ClientInstanceId = clientId, AttachmentKind = InteractiveAttachmentKind.WebTerminal });

    private static async Task<WebSocket> ConnectAsync(ApiTestFactory factory, string sessionId)
    {
        var client = factory.Server.CreateWebSocketClient();
        client.SubProtocols.Add(InteractiveTerminalWebSocketService.SubProtocol);
        client.ConfigureRequest = request => request.Headers.Origin = "http://127.0.0.1";
        return await client.ConnectAsync(new Uri($"ws://127.0.0.1/api/v1/local-host/interactive-agent-sessions/{sessionId}/terminal/ws"), CancellationToken.None);
    }

    private static Task SendHelloAsync(WebSocket socket, string sessionId, string runtimeId, string attachmentId, string token, long afterSequence)
        => SendJsonAsync(socket, new InteractiveTerminalWebSocketHello { InteractiveAgentSessionId = sessionId, TerminalRuntimeId = runtimeId, AttachmentId = attachmentId, AttachmentToken = token, AfterSequence = afterSequence });

    private static Task SendControlAsync(WebSocket socket, InteractiveTerminalWebSocketControl control) => SendJsonAsync(socket, control);

    private static Task SendJsonAsync<T>(WebSocket socket, T value)
        => socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value, LocalHostContract.JsonOptions), WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<InteractiveTerminalWebSocketControl> ReceiveControlAsync(WebSocket socket, TimeSpan? timeout = null)
        => JsonSerializer.Deserialize<InteractiveTerminalWebSocketControl>(await ReceiveAsync(socket, WebSocketMessageType.Text, timeout), LocalHostContract.JsonOptions)!;

    private static Task<byte[]> ReceiveBinaryAsync(WebSocket socket) => ReceiveAsync(socket, WebSocketMessageType.Binary);

    private static async Task<byte[]> ReceiveAsync(WebSocket socket, WebSocketMessageType expected, TimeSpan? receiveTimeout = null)
    {
        using var timeout = new CancellationTokenSource(receiveTimeout ?? TimeSpan.FromSeconds(5));
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

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    {
        var response = await client.PostAsJsonAsync(path, request, LocalHostContract.JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LocalHostEnvelope<T>>(LocalHostContract.JsonOptions))!.Data;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string path)
        => (await client.GetFromJsonAsync<LocalHostEnvelope<T>>(path, LocalHostContract.JsonOptions))!.Data;

    private static async Task WaitForDetachedAsync(HttpClient client, string sessionId)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var session = await GetAsync<InteractiveAgentSessionRecord>(client, $"/api/v1/interactive-agent-sessions/{sessionId}");
            if (string.IsNullOrWhiteSpace(session.ActiveAttachmentId)) return;
            await Task.Delay(25);
        }
        throw new TimeoutException("WebSocket presentation did not detach.");
    }

    public void Dispose() => _environment.Dispose();
}
