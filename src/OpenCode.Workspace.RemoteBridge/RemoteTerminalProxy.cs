using System.Net.WebSockets;
using System.Text.Json;
using OpenCode.Workspace.LocalClient;

namespace OpenCode.Workspace.RemoteBridge;

public sealed record RemoteBridgeHello
{
    public string Type { get; init; } = "hello";
    public string SessionId { get; init; } = string.Empty;
    public string RuntimeId { get; init; } = string.Empty;
    public long AfterSequence { get; init; }
    public bool Takeover { get; init; }
    public string GrantToken { get; init; } = string.Empty;
}

public sealed class RemoteTerminalProxy(IRemoteBridgeBackend backend, BridgeGrantStore grants, ILogger<RemoteTerminalProxy> logger)
{
    public const string SubProtocol = "opencode-terminal-v1";
    private const int MaximumBrowserMessageBytes = 64 * 1024;
    private const int MaximumLocalHostMessageBytes = 1024 * 1024;
    private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

    public async Task HandleAsync(HttpContext context, string sessionId)
    {
        if (!context.WebSockets.IsWebSocketRequest || !context.WebSockets.WebSocketRequestedProtocols.Contains(SubProtocol, StringComparer.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var browser = await context.WebSockets.AcceptWebSocketAsync(SubProtocol);
        try
        {
            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            helloTimeout.CancelAfter(HelloTimeout);
            var first = await ReceiveAsync(browser, MaximumBrowserMessageBytes, helloTimeout.Token);
            var hello = first.Type == WebSocketMessageType.Text ? JsonSerializer.Deserialize<RemoteBridgeHello>(first.Data, LocalHostContract.JsonOptions) : null;
            var owner = context.User.FindFirst("sub")?.Value ?? context.User.FindFirst("email")?.Value ?? string.Empty;
            var grant = hello is null ? null : grants.Consume(hello.GrantToken, owner);
            if (hello is null || hello.Type != "hello" || hello.SessionId != sessionId || hello.AfterSequence < 0 || grant is null
                || grant.Takeover != hello.Takeover || grant.Attachment.SessionId != sessionId || grant.Attachment.RuntimeId != hello.RuntimeId)
                throw new InvalidOperationException("The bridge hello or connection grant is invalid or expired.");

            using var localHost = await backend.ConnectTerminalAsync(grant.Attachment, hello.AfterSequence, context.RequestAborted);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            var upstream = CopyAsync(browser, localHost, true, linked.Token);
            var downstream = CopyAsync(localHost, browser, false, linked.Token);
            await Task.WhenAny(upstream, downstream);
            linked.Cancel();
            try { await Task.WhenAll(upstream, downstream); } catch (OperationCanceledException) { }
        }
        catch (Exception exception) when (exception is WebSocketException or InvalidOperationException or OperationCanceledException)
        {
            logger.LogDebug(exception, "Remote terminal proxy closed for session {SessionId}.", sessionId);
            if (browser.State == WebSocketState.Open)
            {
                var error = JsonSerializer.SerializeToUtf8Bytes(new { type = "error", code = "bridge_connection_rejected", message = exception.Message });
                await browser.SendAsync(error, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        finally
        {
            if (browser.State is WebSocketState.Open or WebSocketState.CloseReceived)
                try { await browser.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bridge presentation closed", CancellationToken.None); } catch (WebSocketException) { }
        }
    }

    private static async Task CopyAsync(WebSocket source, WebSocket destination, bool browserToLocalHost, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && source.State == WebSocketState.Open && destination.State == WebSocketState.Open)
        {
            var maximumMessageBytes = browserToLocalHost ? MaximumBrowserMessageBytes : MaximumLocalHostMessageBytes;
            var message = await ReceiveAsync(source, maximumMessageBytes, cancellationToken);
            if (message.Type == WebSocketMessageType.Close) return;
            if (message.Type == WebSocketMessageType.Text && !IsAllowedControl(message.Data, browserToLocalHost))
                throw new InvalidOperationException("Unsupported terminal control message.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SendTimeout);
            await destination.SendAsync(message.Data, message.Type, true, timeout.Token);
        }
    }

    private static bool IsAllowedControl(byte[] data, bool browserToLocalHost)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            var type = document.RootElement.GetProperty("type").GetString();
            return browserToLocalHost
                ? type is "resize" or "detach" or "stop" or "ping" or "ack"
                : type is "attached" or "output" or "gap" or "detach" or "runtime_state" or "error" or "ack" or "pong";
        }
        catch (JsonException) { return false; }
    }

    private static async Task<SocketMessage> ReceiveAsync(WebSocket socket, int maximumMessageBytes, CancellationToken cancellationToken)
    {
        using var content = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (content.Length + result.Count > maximumMessageBytes) throw new InvalidOperationException($"Terminal message exceeded {maximumMessageBytes} bytes.");
            content.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return new SocketMessage(result.MessageType, content.ToArray());
    }

    private sealed record SocketMessage(WebSocketMessageType Type, byte[] Data);
}
