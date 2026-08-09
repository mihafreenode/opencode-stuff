using System.Net.WebSockets;
using System.Text.Json;
using OpenCode.Workspace.LocalClient;

namespace OpenCode.Workspace.RemoteBridge;

public interface IRemoteBridgeBackend
{
    Task<IReadOnlyList<InteractiveAgentSessionRecord>> ListSessionsAsync(CancellationToken cancellationToken);
    Task<InteractiveAgentSessionRecord> GetSessionAsync(string sessionId, CancellationToken cancellationToken);
    Task<InteractiveTerminalRuntimeRecord> GetTerminalAsync(string sessionId, CancellationToken cancellationToken);
    Task<BackendAttachment> AttachAsync(string sessionId, bool takeover, string expectedAttachmentId, CancellationToken cancellationToken);
    Task<WebSocket> ConnectTerminalAsync(BackendAttachment attachment, long afterSequence, CancellationToken cancellationToken);
}

public sealed record BackendAttachment(string SessionId, string RuntimeId, string AttachmentId, string AttachmentToken, string ClientInstanceId);

public sealed class LocalHostRemoteBridgeBackend : IRemoteBridgeBackend
{
    private readonly Uri _baseUri;
    private readonly LocalHostClient _client;

    public LocalHostRemoteBridgeBackend(Microsoft.Extensions.Options.IOptions<RemoteBridgeOptions> options)
    {
        _baseUri = new Uri(options.Value.RemoteAccess.LocalHostBaseUrl.TrimEnd('/') + "/");
        _client = new LocalHostClient(new HttpClient { BaseAddress = _baseUri }, _baseUri.ToString());
    }

    public Task<IReadOnlyList<InteractiveAgentSessionRecord>> ListSessionsAsync(CancellationToken cancellationToken) => _client.ListInteractiveAgentSessionsAsync(cancellationToken: cancellationToken);
    public Task<InteractiveAgentSessionRecord> GetSessionAsync(string sessionId, CancellationToken cancellationToken) => _client.GetInteractiveAgentSessionAsync(sessionId, cancellationToken);
    public Task<InteractiveTerminalRuntimeRecord> GetTerminalAsync(string sessionId, CancellationToken cancellationToken) => _client.GetInteractiveTerminalAsync(sessionId, cancellationToken);

    public async Task<BackendAttachment> AttachAsync(string sessionId, bool takeover, string expectedAttachmentId, CancellationToken cancellationToken)
    {
        var clientId = $"remote-bridge-{Guid.NewGuid():n}";
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
        var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol(RemoteTerminalProxy.SubProtocol);
        socket.Options.SetRequestHeader("Origin", _baseUri.GetLeftPart(UriPartial.Authority));
        var scheme = _baseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
        var endpoint = new UriBuilder(_baseUri) { Scheme = scheme, Path = $"/api/v1/local-host/interactive-agent-sessions/{Uri.EscapeDataString(attachment.SessionId)}/terminal/ws" }.Uri;
        await socket.ConnectAsync(endpoint, cancellationToken);
        var hello = new InteractiveTerminalWebSocketHello
        {
            InteractiveAgentSessionId = attachment.SessionId,
            TerminalRuntimeId = attachment.RuntimeId,
            AttachmentId = attachment.AttachmentId,
            AttachmentToken = attachment.AttachmentToken,
            AfterSequence = afterSequence,
        };
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(hello, LocalHostContract.JsonOptions), WebSocketMessageType.Text, true, cancellationToken);
        return socket;
    }
}
