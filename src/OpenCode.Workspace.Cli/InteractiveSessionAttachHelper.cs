using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using OpenCode.Workspace.LocalClient;

namespace OpenCode.Workspace.Cli;

internal sealed class InteractiveSessionAttachHelper
{
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public InteractiveSessionAttachHelper(TextWriter output, TextWriter error)
    {
        _output = output;
        _error = error;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var sessionId = RequireOption(args, "--session-id");
        var attachmentId = RequireOption(args, "--attachment-id");
        var attachmentToken = RequireOption(args, "--attachment-token");
        var attachmentRecoveryId = RequireOption(args, "--attachment-recovery-id");
        var recoverySecret = RequireOption(args, "--recovery-secret");
        var stateRoot = RequireOption(args, "--state-root");
        var helperStartedUtc = DateTimeOffset.UtcNow;
        var clientOptions = new LocalHostClientOptions { StateRoot = stateRoot };
        var logPath = Path.Combine(stateRoot, "interactive-agent-sessions", sessionId, $"helper-{attachmentId}.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        using var instanceGuard = HelperInstanceGuard.Acquire(stateRoot, sessionId, attachmentId);
        await WriteLogAsync(logPath, "helper-start");

        LocalHostClient? client = null;
        Process? child = null;
        var childStartedUtc = (DateTimeOffset?)null;
        var providerSessionId = string.Empty;
        var providerSource = ProviderSessionIdentitySource.None;
        var detachRequested = false;
        var heartbeatIntervalSeconds = 10;
        var nextHeartbeatUtc = DateTimeOffset.UtcNow;

        try
        {
            client = await LocalHostClient.ConnectAsync(clientOptions, cancellationToken);
            var activation = await client.ActivateInteractiveSessionAttachmentAsync(sessionId, attachmentId, new ActivateInteractiveSessionAttachmentRequest
            {
                AttachmentToken = attachmentToken,
                HelperProcessId = Environment.ProcessId,
            }, cancellationToken);
            heartbeatIntervalSeconds = Math.Max(1, activation.HeartbeatIntervalSeconds);
            nextHeartbeatUtc = DateTimeOffset.UtcNow.AddSeconds(heartbeatIntervalSeconds);

            IReadOnlyList<string> baselineWorkspaceSessions = Array.Empty<string>();
            if (!string.IsNullOrWhiteSpace(activation.Session.ProviderSessionId))
            {
                providerSessionId = activation.Session.ProviderSessionId;
                providerSource = ProviderSessionIdentitySource.ExistingCanonicalIdentity;
            }
            else if (activation.ProviderSessionProbeDescriptor is not null)
            {
                baselineWorkspaceSessions = await ProbeWorkspaceSessionsAsync(activation.ProviderSessionProbeDescriptor, cancellationToken);
                await WriteLogAsync(logPath, $"provider-baseline:{string.Join(',', baselineWorkspaceSessions)}");
            }

            child = StartApprovedProcess(activation.ProcessLaunchDescriptor);
            childStartedUtc = DateTimeOffset.UtcNow;
            await client.ReportInteractiveSessionAttachmentProcessStartedAsync(sessionId, attachmentId, new InteractiveSessionAttachmentProcessStartedRequest
            {
                AttachmentToken = attachmentToken,
                ChildProcessId = child.Id,
            }, cancellationToken);
            await WriteLogAsync(logPath, $"child-started:{child.Id}");

            var providerProbeSettled = !string.IsNullOrWhiteSpace(providerSessionId) || activation.ProviderSessionProbeDescriptor is null;
            while (!cancellationToken.IsCancellationRequested)
            {
                if (child.HasExited)
                {
                    break;
                }

                if (!providerProbeSettled && activation.ProviderSessionProbeDescriptor is not null)
                {
                    var afterSessions = await ProbeWorkspaceSessionsAsync(activation.ProviderSessionProbeDescriptor, cancellationToken);
                    var createdSessions = afterSessions.Except(baselineWorkspaceSessions, StringComparer.OrdinalIgnoreCase).ToArray();
                    if (createdSessions.Length == 1)
                    {
                        providerSessionId = createdSessions[0];
                        providerSource = ProviderSessionIdentitySource.LaunchCorrelation;
                        await client.ReportInteractiveSessionProviderSessionAsync(sessionId, attachmentId, new InteractiveSessionAttachmentProviderSessionRequest
                        {
                            AttachmentToken = attachmentToken,
                            ProviderSessionId = providerSessionId,
                            IdentitySource = providerSource,
                        }, cancellationToken);
                        providerProbeSettled = true;
                        await WriteLogAsync(logPath, $"provider-session:{providerSessionId}:{providerSource}");
                    }
                    else if (createdSessions.Length > 1)
                    {
                        providerProbeSettled = true;
                        await WriteLogAsync(logPath, "ambiguous_provider_session");
                    }
                }

                if (DateTimeOffset.UtcNow >= nextHeartbeatUtc)
                {
                    try
                    {
                        var heartbeat = await client.HeartbeatInteractiveSessionAttachmentAsync(sessionId, attachmentId, new InteractiveSessionAttachmentHeartbeatRequest
                        {
                            AttachmentToken = attachmentToken,
                        }, cancellationToken);
                        heartbeatIntervalSeconds = Math.Max(1, heartbeat.HeartbeatIntervalSeconds);
                        nextHeartbeatUtc = DateTimeOffset.UtcNow.AddSeconds(heartbeatIntervalSeconds);
                        if (heartbeat.RequestedAction == InteractiveAttachmentControlAction.Detach && !detachRequested)
                        {
                            detachRequested = true;
                            await WriteLogAsync(logPath, "detach-requested");
                            if (!await TryRequestGracefulTerminationAsync(child, TimeSpan.FromSeconds(5), cancellationToken))
                            {
                                await WriteLogAsync(logPath, "detach-graceful-timeout");
                                detachRequested = false;
                            }
                        }
                    }
                    catch (Exception exception) when (IsTransientHeartbeatFailure(exception))
                    {
                        await WriteLogAsync(logPath, $"heartbeat-failed:{exception.GetType().Name}:{exception.Message}");
                        var recovery = await TryRecoverAsync(client, clientOptions, sessionId, attachmentId, attachmentRecoveryId, recoverySecret, helperStartedUtc, child, childStartedUtc, providerSessionId, logPath, cancellationToken);
                        if (!recovery.Succeeded)
                        {
                            await WriteLogAsync(logPath, $"recovery-rejected:{recovery.Reason}");
                            await TryRequestGracefulTerminationAsync(child, TimeSpan.FromSeconds(5), CancellationToken.None);
                            break;
                        }

                        client = recovery.Client!;
                        attachmentToken = recovery.AttachmentToken;
                        heartbeatIntervalSeconds = Math.Max(1, recovery.HeartbeatIntervalSeconds);
                        nextHeartbeatUtc = DateTimeOffset.UtcNow.AddSeconds(heartbeatIntervalSeconds);
                        detachRequested = recovery.RequestedAction == InteractiveAttachmentControlAction.Detach;
                        await WriteLogAsync(logPath, $"recovery-granted:token-generation={recovery.TokenGeneration}");
                    }
                }

                await Task.Delay(250, cancellationToken);
            }

            if (child is null)
            {
                throw new InvalidOperationException("The approved attachment child process did not start.");
            }

            await client.ReportInteractiveSessionAttachmentProcessExitAsync(sessionId, attachmentId, new InteractiveSessionAttachmentProcessExitRequest
            {
                AttachmentToken = attachmentToken,
                ChildProcessId = child.Id,
                ExitCode = child.ExitCode,
                Outcome = detachRequested ? "detach_requested" : child.ExitCode == 0 ? "normal_exit" : "failed_exit",
                FailureMessage = child.ExitCode == 0 ? string.Empty : $"Attach child exited with code {child.ExitCode}.",
            }, CancellationToken.None);
            await WriteLogAsync(logPath, $"child-exit:{child.ExitCode}:{providerSessionId}");
            return child.ExitCode;
        }
        catch (Exception exception)
        {
            await WriteLogAsync(logPath, $"helper-failed:{exception.GetType().Name}:{exception.Message}");
            await _error.WriteLineAsync(exception.Message);
            if (client is not null && child is not null && !child.HasExited)
            {
                try
                {
                    await client.ReportInteractiveSessionAttachmentProcessExitAsync(sessionId, attachmentId, new InteractiveSessionAttachmentProcessExitRequest
                    {
                        AttachmentToken = attachmentToken,
                        ChildProcessId = child.Id,
                        Outcome = "launch_failed",
                        FailureMessage = exception.Message,
                    }, CancellationToken.None);
                }
                catch
                {
                }
            }

            return 7;
        }
        finally
        {
            if (client is not null)
            {
                await client.DisposeAsync();
            }
        }
    }

    private static bool IsTransientHeartbeatFailure(Exception exception)
        => exception is HttpRequestException
            || exception is TaskCanceledException
            || exception is LocalHostClientException localHost && localHost.Code is "invalid_attachment_credential" or "attachment_not_found" or "interactive_session_not_found";

    private static Process StartApprovedProcess(ApprovedProcessLaunchDescriptor descriptor, bool redirectOutput = false)
    {
        var startInfo = new ProcessStartInfo(descriptor.FileName)
        {
            UseShellExecute = false,
            WorkingDirectory = string.IsNullOrWhiteSpace(descriptor.WorkingDirectory) ? Environment.CurrentDirectory : descriptor.WorkingDirectory,
            RedirectStandardOutput = redirectOutput,
        };
        foreach (var argument in descriptor.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start approved process '{descriptor.FileName}'.");
    }

    private static async Task<IReadOnlyList<string>> ProbeWorkspaceSessionsAsync(ApprovedProcessLaunchDescriptor descriptor, CancellationToken cancellationToken)
    {
        using var probe = StartApprovedProcess(descriptor, redirectOutput: true);
        var output = await probe.StandardOutput.ReadToEndAsync(cancellationToken);
        await probe.WaitForExitAsync(cancellationToken);
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private async Task<RecoveryAttemptResult> TryRecoverAsync(LocalHostClient currentClient, LocalHostClientOptions clientOptions, string sessionId, string attachmentId, string attachmentRecoveryId, string recoverySecret, DateTimeOffset helperStartedUtc, Process child, DateTimeOffset? childStartedUtc, string providerSessionId, string logPath, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline && !child.HasExited)
        {
            try
            {
                await currentClient.DisposeAsync();
                var client = await LocalHostClient.ConnectAsync(clientOptions, cancellationToken);
                var recovered = await client.RecoverInteractiveSessionAttachmentAsync(sessionId, attachmentId, new RecoverInteractiveSessionAttachmentRequest
                {
                    AttachmentRecoveryId = attachmentRecoveryId,
                    RecoverySecret = recoverySecret,
                    HelperProcessId = Environment.ProcessId,
                    HelperStartedUtc = helperStartedUtc,
                    ChildProcessId = child.Id,
                    ChildStartedUtc = childStartedUtc,
                    ProviderSessionId = providerSessionId,
                }, cancellationToken);
                return new RecoveryAttemptResult(true, string.Empty, client, recovered.AttachmentToken, recovered.HeartbeatIntervalSeconds, recovered.TokenGeneration, recovered.RequestedAction);
            }
            catch (LocalHostClientException exception) when (exception.Code is "recovery_not_allowed" or "invalid_recovery_proof" or "provider_session_mismatch")
            {
                await WriteLogAsync(logPath, $"recovery-terminal:{exception.Code}");
                return new RecoveryAttemptResult(false, exception.Code, null, string.Empty, 0, 0, InteractiveAttachmentControlAction.None);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or LocalHostClientException)
            {
                await WriteLogAsync(logPath, $"recovery-retry:{exception.GetType().Name}:{exception.Message}");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        return new RecoveryAttemptResult(false, "recovery_timeout", null, string.Empty, 0, 0, InteractiveAttachmentControlAction.None);
    }

    private static async Task<bool> TryRequestGracefulTerminationAsync(Process child, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (child.HasExited)
        {
            return true;
        }

        if (OperatingSystem.IsWindows())
        {
            NativeConsoleControl.IgnoreControlC(true);
            try
            {
                NativeConsoleControl.SendBreakSignal();
            }
            finally
            {
                NativeConsoleControl.IgnoreControlC(false);
            }
        }
        else
        {
            try
            {
                child.CloseMainWindow();
            }
            catch
            {
            }
        }

        var started = DateTimeOffset.UtcNow;
        while (!child.HasExited && DateTimeOffset.UtcNow - started < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(100, cancellationToken);
        }

        return child.HasExited;
    }

    private async Task WriteLogAsync(string logPath, string message)
        => await File.AppendAllTextAsync(logPath, $"[{DateTimeOffset.UtcNow:O}] {message}{Environment.NewLine}", Encoding.UTF8);

    private static string RequireOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        throw new ArgumentException($"Missing required option {name}.");
    }

    private sealed record RecoveryAttemptResult(bool Succeeded, string Reason, LocalHostClient? Client, string AttachmentToken, int HeartbeatIntervalSeconds, int TokenGeneration, InteractiveAttachmentControlAction RequestedAction);

    private sealed class HelperInstanceGuard : IDisposable
    {
        private readonly FileStream _stream;

        private HelperInstanceGuard(FileStream stream)
        {
            _stream = stream;
        }

        public static HelperInstanceGuard Acquire(string stateRoot, string sessionId, string attachmentId)
        {
            var lockPath = Path.Combine(stateRoot, "interactive-agent-sessions", sessionId, $"helper-{attachmentId}.lock");
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            try
            {
                return new HelperInstanceGuard(new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException($"Another helper instance is already running for attachment '{attachmentId}'.", exception);
            }
        }

        public void Dispose() => _stream.Dispose();
    }

    private static class NativeConsoleControl
    {
        private const uint CtrlBreakEvent = 1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate? handler, bool add);

        private delegate bool ConsoleCtrlDelegate(uint ctrlType);

        public static void IgnoreControlC(bool ignore)
            => SetConsoleCtrlHandler(null, ignore);

        public static void SendBreakSignal()
        {
            if (!GenerateConsoleCtrlEvent(CtrlBreakEvent, 0))
            {
                throw new InvalidOperationException("Could not send a cooperative console break signal to the attach child.");
            }
        }
    }
}
