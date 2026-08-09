using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using OpenCode.Workspace.LocalClient;
using OpenCode.Workspace.Mcp;

namespace OpenCode.Workspace.Api;

public sealed class InteractiveTerminalWebSocketService(
    InteractiveAgentSessionService sessions,
    InteractiveTerminalRuntimeService terminals,
    InteractiveSessionAttachmentService attachments,
    ILogger<InteractiveTerminalWebSocketService> logger)
{
    public const string SubProtocol = "opencode-terminal-v1";
    private const int MaximumInputBytes = 64 * 1024;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExitOutputQuiescence = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ExitOutputDrainTimeout = TimeSpan.FromSeconds(2);
    private readonly ConcurrentDictionary<string, string> _activeConnections = new(StringComparer.OrdinalIgnoreCase);

    public async Task HandleAsync(HttpContext context, string interactiveAgentSessionId)
    {
        if ((context.Connection.RemoteIpAddress is not null && !IPAddress.IsLoopback(context.Connection.RemoteIpAddress))
            || !IsAuthorizedLocalRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest
            || !context.WebSockets.WebSocketRequestedProtocols.Contains(SubProtocol, StringComparer.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync(SubProtocol);
        InteractiveTerminalWebSocketHello? hello = null;
        var attachmentActivated = false;
        var credentialValidated = false;
        var connectionId = Guid.NewGuid().ToString("n");
        var connectionClaimed = false;
        try
        {
            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            helloTimeout.CancelAfter(HelloTimeout);
            hello = await ReceiveHelloAsync(socket, helloTimeout.Token);
            ValidateHello(interactiveAgentSessionId, hello);
            await sessions.ValidateTerminalInputAuthorityAsync(interactiveAgentSessionId, hello.AttachmentId, hello.AttachmentToken, context.RequestAborted);
            credentialValidated = true;
            var runtime = await terminals.GetAsync(interactiveAgentSessionId, context.RequestAborted);
            if (!string.Equals(runtime.RuntimeId, hello.TerminalRuntimeId, StringComparison.OrdinalIgnoreCase))
            {
                throw new OpenCodeWorkspaceMcpException("terminal_runtime_mismatch", "The requested terminal runtime is not current for this interactive session.", "Refresh canonical session state and attach again.");
            }

            if (!_activeConnections.TryAdd(hello.AttachmentId, connectionId))
            {
                credentialValidated = false;
                throw new OpenCodeWorkspaceMcpException("attachment_already_connected", "This terminal attachment already has an active WebSocket.", "Use the existing presentation or request an explicit takeover.");
            }
            connectionClaimed = true;
            var activation = await attachments.ActivateAsync(
                interactiveAgentSessionId,
                hello.AttachmentId,
                new ActivateInteractiveSessionAttachmentRequest { AttachmentToken = hello.AttachmentToken },
                context.RequestAborted);
            attachmentActivated = true;
            await SendControlAsync(socket, new InteractiveTerminalWebSocketControl
            {
                Type = "attached",
                InteractiveAgentSessionId = interactiveAgentSessionId,
                TerminalRuntimeId = runtime.RuntimeId,
                AttachmentId = hello.AttachmentId,
                RuntimeStatus = runtime.Status,
                EarliestAvailableSequence = runtime.EarliestSequence,
                LatestAvailableSequence = runtime.LatestSequence,
            }, context.RequestAborted);

            await RunConnectionAsync(socket, interactiveAgentSessionId, hello, activation.HeartbeatIntervalSeconds, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (WebSocketException exception)
        {
            logger.LogDebug(exception, "Terminal WebSocket disconnected for session {InteractiveAgentSessionId}.", interactiveAgentSessionId);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Terminal WebSocket rejected for session {InteractiveAgentSessionId}: {ErrorType}.", interactiveAgentSessionId, exception.GetType().Name);
            await TrySendErrorAsync(socket, exception, context.RequestAborted);
        }
        finally
        {
            if ((attachmentActivated || credentialValidated) && hello is not null)
            {
                try
                {
                    await attachments.ReportProcessExitAsync(interactiveAgentSessionId, hello.AttachmentId, new InteractiveSessionAttachmentProcessExitRequest
                    {
                        AttachmentToken = hello.AttachmentToken,
                        Outcome = "presentation_closed",
                    }, CancellationToken.None);
                    terminals.ClearActiveAttachmentIfMatches(interactiveAgentSessionId, hello.AttachmentId);
                }
                catch (OpenCodeWorkspaceMcpException)
                {
                    // Takeover, expiry, or runtime stop may already have invalidated this presentation.
                }
            }

            if (connectionClaimed && hello is not null)
                _activeConnections.TryRemove(new KeyValuePair<string, string>(hello.AttachmentId, connectionId));

            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var closeTimeout = new CancellationTokenSource(CloseTimeout);
                try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "presentation closed", closeTimeout.Token); }
                catch (Exception exception) when (exception is WebSocketException or OperationCanceledException) { }
            }
        }
    }

    internal static bool IsAuthorizedLocalRequest(HttpRequest request)
    {
        if (!TryGetLoopbackHost(request.Host.Host, out _)) return false;
        var origin = request.Headers.Origin.ToString();
        return IsSameOrigin(request, origin);
    }

    internal static bool IsSameOrigin(HttpRequest request, string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri)) return false;
        return string.Equals(originUri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(originUri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && originUri.Port == (request.Host.Port ?? (request.IsHttps ? 443 : 80));
    }

    internal static bool TryGetLoopbackHost(string host, out IPAddress? address)
        => IPAddress.TryParse(host, out address) && IPAddress.IsLoopback(address);

    private async Task RunConnectionAsync(WebSocket socket, string sessionId, InteractiveTerminalWebSocketHello hello, int configuredHeartbeatSeconds, CancellationToken cancellationToken)
    {
        var cursor = hello.AfterSequence;
        var acknowledgedSequence = hello.AfterSequence;
        var sentSequence = hello.AfterSequence;
        DateTimeOffset? acknowledgementDeadline = null;
        DateTimeOffset? terminalStatusObserved = null;
        var terminalOutputLastChanged = DateTimeOffset.UtcNow;
        var terminalLatestSequence = 0L;
        var heartbeatEvery = TimeSpan.FromSeconds(Math.Max(1, Math.Min(configuredHeartbeatSeconds, (int)HeartbeatInterval.TotalSeconds)));
        var nextHeartbeat = DateTimeOffset.UtcNow;
        Task<ReceivedMessage>? receive = null;

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            receive ??= ReceiveMessageAsync(socket, cancellationToken);
            var delay = Task.Delay(PollInterval, cancellationToken);
            var completed = await Task.WhenAny(receive, delay);
            if (completed == receive)
            {
                var message = await receive;
                receive = null;
                if (message.MessageType == WebSocketMessageType.Close) return;
                if (message.MessageType == WebSocketMessageType.Binary)
                {
                    await terminals.InputAsync(sessionId, new TerminalInputRequest
                    {
                        AttachmentId = hello.AttachmentId,
                        AttachmentToken = hello.AttachmentToken,
                        DataBase64 = Convert.ToBase64String(message.Data),
                    }, cancellationToken);
                    await SendControlAsync(socket, new InteractiveTerminalWebSocketControl { Type = "ack", ByteLength = message.Data.Length }, cancellationToken);
                }
                else
                {
                    var controlResult = await HandleControlAsync(socket, sessionId, hello, message.Data, cancellationToken);
                    if (controlResult.AcknowledgedSequence > acknowledgedSequence && controlResult.AcknowledgedSequence <= sentSequence)
                    {
                        acknowledgedSequence = controlResult.AcknowledgedSequence;
                        acknowledgementDeadline = null;
                    }
                    if (controlResult.Close) return;
                }
            }

            if (DateTimeOffset.UtcNow >= nextHeartbeat)
            {
                var heartbeat = await attachments.HeartbeatAsync(sessionId, hello.AttachmentId, new InteractiveSessionAttachmentHeartbeatRequest { AttachmentToken = hello.AttachmentToken }, cancellationToken);
                if (heartbeat.RequestedAction == InteractiveAttachmentControlAction.Detach)
                {
                    await SendControlAsync(socket, new InteractiveTerminalWebSocketControl { Type = "detach", Message = "Attachment takeover requested." }, cancellationToken);
                    return;
                }
                nextHeartbeat = DateTimeOffset.UtcNow + heartbeatEvery;
            }

            if (acknowledgementDeadline is not null && DateTimeOffset.UtcNow >= acknowledgementDeadline)
                throw new WebSocketException("Terminal presentation client did not acknowledge bounded output in time.");

            var output = await terminals.ReadOutputAsync(sessionId, cursor, cancellationToken);
            if (output.GapDetected)
            {
                await SendControlAsync(socket, new InteractiveTerminalWebSocketControl
                {
                    Type = "gap",
                    RequestedAfterSequence = output.RequestedAfterSequence,
                    EarliestAvailableSequence = output.EarliestSequence,
                    LatestAvailableSequence = output.LatestSequence,
                }, cancellationToken);
                cursor = output.EarliestSequence - 1;
                acknowledgedSequence = cursor;
                sentSequence = cursor;
                acknowledgementDeadline = null;
            }

            if (sentSequence == acknowledgedSequence)
            {
                var chunk = output.Chunks.FirstOrDefault(item => item.Sequence > cursor);
                if (chunk is not null)
                {
                    var bytes = Convert.FromBase64String(chunk.DataBase64);
                    await SendControlAsync(socket, new InteractiveTerminalWebSocketControl { Type = "output", Sequence = chunk.Sequence, ByteLength = bytes.Length, TimestampUtc = chunk.TimestampUtc }, cancellationToken);
                    await SendAsync(socket, bytes, WebSocketMessageType.Binary, cancellationToken);
                    cursor = chunk.Sequence;
                    sentSequence = chunk.Sequence;
                    acknowledgementDeadline = DateTimeOffset.UtcNow + SendTimeout;
                }
            }

            var runtime = await terminals.GetAsync(sessionId, cancellationToken);
            if (runtime.Status is InteractiveTerminalRuntimeStatus.Starting or InteractiveTerminalRuntimeStatus.Running)
            {
                terminalStatusObserved = null;
                terminalLatestSequence = runtime.LatestSequence;
            }
            else
            {
                var now = DateTimeOffset.UtcNow;
                if (terminalStatusObserved is null)
                {
                    terminalStatusObserved = now;
                    terminalOutputLastChanged = now;
                    terminalLatestSequence = runtime.LatestSequence;
                }
                else if (runtime.LatestSequence != terminalLatestSequence)
                {
                    terminalLatestSequence = runtime.LatestSequence;
                    terminalOutputLastChanged = now;
                }

                var outputDrained = cursor >= runtime.LatestSequence && sentSequence == acknowledgedSequence;
                var outputQuiescent = now - terminalOutputLastChanged >= ExitOutputQuiescence;
                var drainTimedOut = now - terminalStatusObserved >= ExitOutputDrainTimeout;
                if ((outputDrained && outputQuiescent) || drainTimedOut)
                {
                    await SendControlAsync(socket, new InteractiveTerminalWebSocketControl { Type = "runtime_state", TerminalRuntimeId = runtime.RuntimeId, RuntimeStatus = runtime.Status }, cancellationToken);
                    return;
                }
            }
        }
    }

    private async Task<ControlResult> HandleControlAsync(WebSocket socket, string sessionId, InteractiveTerminalWebSocketHello hello, byte[] data, CancellationToken cancellationToken)
    {
        var control = JsonSerializer.Deserialize<InteractiveTerminalWebSocketControl>(data, LocalHostContract.JsonOptions)
            ?? throw new OpenCodeWorkspaceMcpException("invalid_terminal_message", "Terminal control message was empty.", "Send a typed terminal control message.");
        switch (control.Type)
        {
            case "resize":
                await terminals.ResizeAsync(sessionId, new TerminalResizeRequest { AttachmentId = hello.AttachmentId, AttachmentToken = hello.AttachmentToken, Columns = control.Columns, Rows = control.Rows }, cancellationToken);
                await SendControlAsync(socket, new InteractiveTerminalWebSocketControl { Type = "ack", Columns = control.Columns, Rows = control.Rows }, cancellationToken);
                return new ControlResult(false, 0);
            case "detach":
                await attachments.DetachAsync(sessionId, hello.AttachmentId, new DetachInteractiveSessionAttachmentRequest { ClientInstanceId = (await sessions.GetAsync(sessionId, cancellationToken)).ActiveLease?.HolderClientInstanceId ?? string.Empty, Reason = "browser_detach" }, cancellationToken);
                return new ControlResult(true, 0);
            case "stop":
                await sessions.ValidateTerminalInputAuthorityAsync(sessionId, hello.AttachmentId, hello.AttachmentToken, cancellationToken);
                var stopped = await terminals.StopAsync(sessionId, cancellationToken);
                await sessions.DetachForRuntimeStopAsync(sessionId, cancellationToken);
                terminals.SetActiveAttachment(sessionId, string.Empty);
                await SendControlAsync(socket, new InteractiveTerminalWebSocketControl { Type = "runtime_state", TerminalRuntimeId = stopped.RuntimeId, RuntimeStatus = stopped.Status }, cancellationToken);
                return new ControlResult(true, 0);
            case "ping":
                await SendControlAsync(socket, new InteractiveTerminalWebSocketControl { Type = "pong" }, cancellationToken);
                return new ControlResult(false, 0);
            case "ack":
                return new ControlResult(false, control.Sequence);
            default:
                throw new OpenCodeWorkspaceMcpException("invalid_terminal_message", $"Terminal control type '{control.Type}' is not supported.", "Use resize, detach, stop, ping, or ack.");
        }
    }

    private static void ValidateHello(string routeSessionId, InteractiveTerminalWebSocketHello hello)
    {
        if (!string.Equals(hello.Type, "hello", StringComparison.Ordinal)
            || !string.Equals(hello.InteractiveAgentSessionId, routeSessionId, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(hello.TerminalRuntimeId)
            || string.IsNullOrWhiteSpace(hello.AttachmentId)
            || string.IsNullOrWhiteSpace(hello.AttachmentToken)
            || hello.AfterSequence < 0)
        {
            throw new OpenCodeWorkspaceMcpException("invalid_terminal_hello", "Terminal WebSocket hello did not identify a valid session, runtime, attachment, and cursor.", "Refresh canonical session state and attach again.");
        }
    }

    private static async Task<InteractiveTerminalWebSocketHello> ReceiveHelloAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var message = await ReceiveMessageAsync(socket, cancellationToken);
        if (message.MessageType != WebSocketMessageType.Text)
            throw new OpenCodeWorkspaceMcpException("invalid_terminal_hello", "The first terminal WebSocket message must be a JSON hello.", "Send hello before terminal bytes.");
        return JsonSerializer.Deserialize<InteractiveTerminalWebSocketHello>(message.Data, LocalHostContract.JsonOptions)
            ?? throw new OpenCodeWorkspaceMcpException("invalid_terminal_hello", "Terminal WebSocket hello was empty.", "Send a typed hello message.");
    }

    private static async Task<ReceivedMessage> ReceiveMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using var content = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (content.Length + result.Count > MaximumInputBytes)
                throw new OpenCodeWorkspaceMcpException("terminal_message_too_large", "Terminal WebSocket message exceeded 64 KiB.", "Send smaller terminal input frames.");
            content.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return new ReceivedMessage(result.MessageType, content.ToArray());
    }

    private static Task SendControlAsync(WebSocket socket, InteractiveTerminalWebSocketControl control, CancellationToken cancellationToken)
        => SendAsync(socket, JsonSerializer.SerializeToUtf8Bytes(control, LocalHostContract.JsonOptions), WebSocketMessageType.Text, cancellationToken);

    private static async Task SendAsync(WebSocket socket, byte[] data, WebSocketMessageType type, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SendTimeout);
        try { await socket.SendAsync(data, type, true, timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WebSocketException("Terminal presentation client exceeded the bounded send timeout.");
        }
    }

    private static async Task TrySendErrorAsync(WebSocket socket, Exception exception, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open) return;
        var code = exception is OpenCodeWorkspaceMcpException known ? known.Code : "terminal_transport_error";
        try
        {
            await SendControlAsync(socket, new InteractiveTerminalWebSocketControl { Type = "error", Code = code, Message = exception.Message }, cancellationToken);
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, code, cancellationToken);
        }
        catch (Exception) { }
    }

    private sealed record ReceivedMessage(WebSocketMessageType MessageType, byte[] Data);
    private sealed record ControlResult(bool Close, long AcknowledgedSequence);
}
