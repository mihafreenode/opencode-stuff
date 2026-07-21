using System.Diagnostics;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.LocalClient;

namespace OpenCode.Workspace.Avalonia.Services;

public interface IDesktopTerminalLauncher
{
    Task<DesktopTerminalLaunchResult> LaunchAsync(ApprovedTerminalLaunchDescriptor descriptor, CancellationToken cancellationToken = default);
}

public sealed record DesktopTerminalLaunchResult
{
    public bool Succeeded { get; init; }
    public int? ProcessId { get; init; }
}

public sealed class WindowsDesktopTerminalLauncher : IDesktopTerminalLauncher
{
    public Task<DesktopTerminalLaunchResult> LaunchAsync(ApprovedTerminalLaunchDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new NotSupportedException("Interactive terminal attachment is not yet supported on this platform.");
        }

        var startInfo = CreateStartInfo(descriptor);
        var process = new Process { StartInfo = startInfo };
        process.Start();
        return Task.FromResult(new DesktopTerminalLaunchResult { Succeeded = true, ProcessId = process.Id });
    }

    internal static ProcessStartInfo CreateStartInfo(ApprovedTerminalLaunchDescriptor descriptor)
    {
        var startInfo = new ProcessStartInfo(descriptor.FileName)
        {
            UseShellExecute = false,
            WorkingDirectory = string.IsNullOrWhiteSpace(descriptor.WorkingDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : descriptor.WorkingDirectory,
        };
        foreach (var argument in descriptor.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}

public interface IDesktopInteractiveSessionApplicationService
{
    Task<IReadOnlyList<InteractiveAgentSessionRecord>> LoadSessionsAsync(string? workspaceId, CancellationToken cancellationToken = default);
    Task<InteractiveAgentSessionRecord> CreateSessionAsync(WorkspaceSnapshot workspace, string? title, CancellationToken cancellationToken = default);
    Task<InteractiveSessionAttachResult> AttachAsync(string interactiveAgentSessionId, CancellationToken cancellationToken = default);
    Task<InteractiveSessionAttachResult> RequestTransferAsync(string interactiveAgentSessionId, CancellationToken cancellationToken = default);
    Task<InteractiveAgentSessionRecord> DetachAsync(InteractiveAgentSessionRecord session, CancellationToken cancellationToken = default);
}

public sealed class LocalHostDesktopInteractiveSessionApplicationService : IDesktopInteractiveSessionApplicationService
{
    private readonly IDesktopTerminalLauncher _launcher;
    private readonly string _clientInstanceId = Guid.NewGuid().ToString("n");

    public LocalHostDesktopInteractiveSessionApplicationService(IDesktopTerminalLauncher launcher)
    {
        _launcher = launcher;
    }

    public async Task<IReadOnlyList<InteractiveAgentSessionRecord>> LoadSessionsAsync(string? workspaceId, CancellationToken cancellationToken = default)
    {
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        return await client.ListInteractiveAgentSessionsAsync(workspaceId, cancellationToken);
    }

    public async Task<InteractiveAgentSessionRecord> CreateSessionAsync(WorkspaceSnapshot workspace, string? title, CancellationToken cancellationToken = default)
    {
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        return await client.CreateInteractiveAgentSessionAsync(workspace.Definition.Workspace.Id, new CreateInteractiveAgentSessionRequest
        {
            CommandId = Guid.NewGuid().ToString("n"),
            WorkspaceId = workspace.Definition.Workspace.Id,
            Title = title ?? string.Empty,
        }, cancellationToken);
    }

    public Task<InteractiveSessionAttachResult> AttachAsync(string interactiveAgentSessionId, CancellationToken cancellationToken = default)
        => AttachInternalAsync(interactiveAgentSessionId, requestTransfer: false, cancellationToken);

    public Task<InteractiveSessionAttachResult> RequestTransferAsync(string interactiveAgentSessionId, CancellationToken cancellationToken = default)
        => AttachInternalAsync(interactiveAgentSessionId, requestTransfer: true, cancellationToken);

    public async Task<InteractiveAgentSessionRecord> DetachAsync(InteractiveAgentSessionRecord session, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(session.ActiveAttachmentId))
        {
            return session;
        }

        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        return await client.DetachInteractiveSessionAttachmentAsync(session.InteractiveAgentSessionId, session.ActiveAttachmentId, new DetachInteractiveSessionAttachmentRequest
        {
            ClientInstanceId = _clientInstanceId,
            Reason = "desktop_detach",
        }, cancellationToken);
    }

    private async Task<InteractiveSessionAttachResult> AttachInternalAsync(string interactiveAgentSessionId, bool requestTransfer, CancellationToken cancellationToken)
    {
        await using var client = await LocalHostClient.ConnectAsync(cancellationToken);
        var attached = await client.AttachInteractiveSessionAsync(interactiveAgentSessionId, new AttachInteractiveSessionRequest
        {
            SessionId = interactiveAgentSessionId,
            CommandId = Guid.NewGuid().ToString("n"),
            ClientInstanceId = _clientInstanceId,
            AttachmentKind = InteractiveAttachmentKind.WindowsTerminal,
            RequestTransfer = requestTransfer,
        }, cancellationToken);

        try
        {
            await _launcher.LaunchAsync(attached.LaunchDescriptor, cancellationToken);
        }
        catch (Exception exception)
        {
            await client.ReportInteractiveSessionAttachmentLaunchFailureAsync(interactiveAgentSessionId, attached.Attachment.AttachmentId, new InteractiveSessionAttachmentLaunchFailureRequest
            {
                ClientInstanceId = _clientInstanceId,
                FailureMessage = exception.Message,
            }, cancellationToken);
            throw;
        }

        return attached;
    }
}

public sealed class UnsupportedDesktopInteractiveSessionApplicationService : IDesktopInteractiveSessionApplicationService
{
    public Task<IReadOnlyList<InteractiveAgentSessionRecord>> LoadSessionsAsync(string? workspaceId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<InteractiveAgentSessionRecord>>([]);

    public Task<InteractiveAgentSessionRecord> CreateSessionAsync(WorkspaceSnapshot workspace, string? title, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Interactive sessions are not available from this desktop application service.");

    public Task<InteractiveSessionAttachResult> AttachAsync(string interactiveAgentSessionId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Interactive terminal attachment is not yet supported on this platform.");

    public Task<InteractiveSessionAttachResult> RequestTransferAsync(string interactiveAgentSessionId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Interactive terminal attachment is not yet supported on this platform.");

    public Task<InteractiveAgentSessionRecord> DetachAsync(InteractiveAgentSessionRecord session, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Interactive terminal attachment is not yet supported on this platform.");
}
