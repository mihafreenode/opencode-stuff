using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.TestHost;
using OpenCode.Workspace.LocalClient;
using OpenCode.Workspace.RemoteBridge;

namespace OpenCode.Workspace.RemoteBridge.Tests;

public sealed class RemoteBridgeProxyTests
{
    [Fact]
    public async Task WebSocket_ProxiesRawBinaryAndTypedControlsUnchanged()
    {
        await using var host = await BridgeTestHost.StartAsync();
        var grant = await CreateGrantAsync(host, takeover: false);
        var client = host.WebSockets;
        using var socket = await ConnectAsync(client, grant);

        var attached = await ReceiveAsync(socket);
        Assert.Equal("attached", JsonDocument.Parse(attached.Data).RootElement.GetProperty("type").GetString());
        var binary = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Binary, binary.Type);
        Assert.Equal(new byte[] { 0, 255, 1, 128, 10 }, binary.Data);

        var input = new byte[] { 9, 0, 250, 4 };
        await socket.SendAsync(input, WebSocketMessageType.Binary, true, default);
        var resize = Encoding.UTF8.GetBytes("{\"type\":\"resize\",\"columns\":90,\"rows\":30}");
        await socket.SendAsync(resize, WebSocketMessageType.Text, true, default);

        await host.Backend.WaitForBrowserMessagesAsync(2);
        Assert.Equal(input, host.Backend.BrowserMessages[0].Data);
        Assert.Equal(resize, host.Backend.BrowserMessages[1].Data);
        Assert.Contains("attachmentToken", Encoding.UTF8.GetString(host.Backend.CanonicalHello!));
        Assert.DoesNotContain(grant.GrantToken, Encoding.UTF8.GetString(host.Backend.CanonicalHello!));
    }

    [Fact]
    public async Task WebSocket_PreservesLargeCanonicalOutputFrame()
    {
        await using var host = await BridgeTestHost.StartAsync();
        host.Backend.Output = Enumerable.Repeat((byte)0xa5, 700_000).ToArray();
        var grant = await CreateGrantAsync(host, takeover: false);
        using var socket = await ConnectAsync(host.WebSockets, grant);

        Assert.Equal("attached", JsonDocument.Parse((await ReceiveAsync(socket)).Data).RootElement.GetProperty("type").GetString());
        var binary = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Binary, binary.Type);
        Assert.Equal(host.Backend.Output, binary.Data);
    }

    [Fact]
    public async Task DisconnectThenNewGrant_ReconnectsWithoutStoppingRuntime()
    {
        await using var host = await BridgeTestHost.StartAsync();
        var firstGrant = await CreateGrantAsync(host, takeover: false);
        using (var first = await ConnectAsync(host.WebSockets, firstGrant))
            await first.CloseAsync(WebSocketCloseStatus.NormalClosure, "test", default);

        var secondGrant = await CreateGrantAsync(host, takeover: false);
        using var second = await ConnectAsync(host.WebSockets, secondGrant);
        Assert.Equal(WebSocketMessageType.Text, (await ReceiveAsync(second)).Type);
        Assert.Equal(2, host.Backend.AttachCalls);
        Assert.True(host.Backend.ConnectionDisposals >= 1);
        Assert.Equal(0, host.Backend.StopCalls);
    }

    [Fact]
    public async Task Grant_ReplayIsRejectedAfterFirstHello()
    {
        await using var host = await BridgeTestHost.StartAsync();
        var grant = await CreateGrantAsync(host, takeover: false);
        using var first = await ConnectAsync(host.WebSockets, grant);
        Assert.Equal("attached", JsonDocument.Parse((await ReceiveAsync(first)).Data).RootElement.GetProperty("type").GetString());

        using var replay = await ConnectAsync(host.WebSockets, grant);
        var rejected = JsonDocument.Parse((await ReceiveAsync(replay)).Data).RootElement;
        Assert.Equal("error", rejected.GetProperty("type").GetString());
        Assert.Equal("bridge_connection_rejected", rejected.GetProperty("code").GetString());
        Assert.Equal(1, host.Backend.AttachCalls);
    }

    [Fact]
    public async Task Takeover_IsExplicitlyDelegatedToBackendAttach()
    {
        await using var host = await BridgeTestHost.StartAsync();
        _ = await CreateGrantAsync(host, takeover: true);
        Assert.True(host.Backend.LastTakeover);
    }

    private static async Task<AttachmentGrantResponse> CreateGrantAsync(BridgeTestHost host, bool takeover)
    {
        using var request = BridgeTestHost.Request(HttpMethod.Post, "/api/v1/remote/sessions/session-1/attachment-grants", origin: true);
        request.Content = new StringContent(JsonSerializer.Serialize(new { takeover }), Encoding.UTF8, "application/json");
        var response = await host.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AttachmentGrantResponse>())!;
    }

    private static async Task<WebSocket> ConnectAsync(WebSocketClient client, AttachmentGrantResponse grant)
    {
        client.ConfigureRequest = request =>
        {
            request.Headers.Host = "remote.example.test";
            request.Headers["Origin"] = "https://remote.example.test";
            request.Headers["Cf-Access-Jwt-Assertion"] = "valid";
        };
        client.SubProtocols.Add(RemoteTerminalProxy.SubProtocol);
        var socket = await client.ConnectAsync(new Uri($"ws://remote.example.test/api/v1/remote/sessions/{grant.SessionId}/terminal/ws"), default);
        var hello = JsonSerializer.SerializeToUtf8Bytes(new RemoteBridgeHello { SessionId = grant.SessionId, RuntimeId = grant.RuntimeId, GrantToken = grant.GrantToken });
        await socket.SendAsync(hello, WebSocketMessageType.Text, true, default);
        return socket;
    }

    private static async Task<(WebSocketMessageType Type, byte[] Data)> ReceiveAsync(WebSocket socket)
    {
        using var content = new MemoryStream();
        var buffer = new byte[65536];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, default);
            content.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return (result.MessageType, content.ToArray());
    }
}

internal sealed class FakeBackend : IRemoteBridgeBackend
{
    public int AttachCalls { get; private set; }
    public bool LastTakeover { get; private set; }
    public int StopCalls { get; private set; }
    public int ConnectionDisposals { get; private set; }
    public byte[]? CanonicalHello { get; private set; }
    public List<(WebSocketMessageType Type, byte[] Data)> BrowserMessages { get; } = [];
    public byte[] Output { get; set; } = [0, 255, 1, 128, 10];

    public Task<IReadOnlyList<InteractiveAgentSessionRecord>> ListSessionsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<InteractiveAgentSessionRecord>>([Session()]);
    public Task<InteractiveAgentSessionRecord> GetSessionAsync(string sessionId, CancellationToken cancellationToken) => Task.FromResult(Session());
    public Task<InteractiveTerminalRuntimeRecord> GetTerminalAsync(string sessionId, CancellationToken cancellationToken) => Task.FromResult(Runtime());
    public Task<BackendAttachment> AttachAsync(string sessionId, bool takeover, string expectedAttachmentId, CancellationToken cancellationToken)
    {
        AttachCalls++;
        LastTakeover = takeover;
        return Task.FromResult(new BackendAttachment(sessionId, "runtime-1", $"attachment-{AttachCalls}", "local-secret-token", $"bridge-{AttachCalls}"));
    }

    public async Task<WebSocket> ConnectTerminalAsync(BackendAttachment attachment, long afterSequence, CancellationToken cancellationToken)
    {
        CanonicalHello = JsonSerializer.SerializeToUtf8Bytes(new InteractiveTerminalWebSocketHello
        {
            InteractiveAgentSessionId = attachment.SessionId,
            TerminalRuntimeId = attachment.RuntimeId,
            AttachmentId = attachment.AttachmentId,
            AttachmentToken = attachment.AttachmentToken,
            AfterSequence = afterSequence,
        }, LocalHostContract.JsonOptions);
        var socket = new ScriptedWebSocket(message =>
        {
            lock (BrowserMessages) BrowserMessages.Add(message);
        }, () => ConnectionDisposals++);
        await socket.QueueAsync(WebSocketMessageType.Text, Encoding.UTF8.GetBytes("{\"type\":\"attached\",\"terminalRuntimeId\":\"runtime-1\"}"));
        await socket.QueueAsync(WebSocketMessageType.Binary, Output);
        return socket;
    }

    public async Task WaitForBrowserMessagesAsync(int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (BrowserMessages.Count < count) await Task.Delay(10, timeout.Token);
    }

    private static InteractiveAgentSessionRecord Session() => new() { InteractiveAgentSessionId = "session-1", Title = "Remote test" };
    private static InteractiveTerminalRuntimeRecord Runtime() => new() { InteractiveAgentSessionId = "session-1", RuntimeId = "runtime-1", Status = InteractiveTerminalRuntimeStatus.Running };
}

internal sealed class ScriptedWebSocket(Action<(WebSocketMessageType Type, byte[] Data)> onSend, Action onDispose) : WebSocket
{
    private readonly Channel<(WebSocketMessageType Type, byte[] Data)> _incoming = Channel.CreateUnbounded<(WebSocketMessageType, byte[])>();
    private WebSocketState _state = WebSocketState.Open;
    private (WebSocketMessageType Type, byte[] Data)? _current;
    private int _currentOffset;
    public override WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;
    public override WebSocketState State => _state;
    public override string? SubProtocol => RemoteTerminalProxy.SubProtocol;
    public ValueTask QueueAsync(WebSocketMessageType type, byte[] data) => _incoming.Writer.WriteAsync((type, data));
    public override void Abort() => _state = WebSocketState.Aborted;
    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) { _state = WebSocketState.Closed; return Task.CompletedTask; }
    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) { _state = WebSocketState.CloseSent; return Task.CompletedTask; }
    public override void Dispose() { _state = WebSocketState.Closed; onDispose(); _incoming.Writer.TryComplete(); }
    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        _current ??= await _incoming.Reader.ReadAsync(cancellationToken);
        var message = _current.Value;
        var count = Math.Min(buffer.Count, message.Data.Length - _currentOffset);
        Array.Copy(message.Data, _currentOffset, buffer.Array!, buffer.Offset, count);
        _currentOffset += count;
        var endOfMessage = _currentOffset == message.Data.Length;
        if (endOfMessage)
        {
            _current = null;
            _currentOffset = 0;
        }
        return new WebSocketReceiveResult(count, message.Type, endOfMessage);
    }
    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        onSend((messageType, buffer.ToArray()));
        return Task.CompletedTask;
    }
}
