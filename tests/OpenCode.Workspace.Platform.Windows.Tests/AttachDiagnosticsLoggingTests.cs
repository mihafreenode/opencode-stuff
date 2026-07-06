using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using System.Reflection;

namespace OpenCode.Workspace.Platform.Windows.Tests;

public sealed class AttachDiagnosticsLoggingTests
{
    [Fact]
    public async Task AttachDiagnostics_AreMirroredToAppLog()
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"ocwm-attach-log-{Guid.NewGuid():N}.log");
        try
        {
            File.WriteAllText(logPath, string.Join(Environment.NewLine, new[]
            {
                "[attach:Odip Analiza] Preflight checks passed.",
                "[attach:Odip Analiza] Attempted command: docker exec --user opencode -w /workspace odip-analiza-workspace bash /opt/opencode-workspace/config/opencode-workspace-shell.sh",
                "+ run_opencode opencode",
                "[attach] Failed at line 16: oracle_client_home=$(find /opt/oracle/instantclient ...)",
                "[attach:Odip Analiza] docker exec failed with exit code 1",
                "[attach:Odip Analiza] docker compose ps:",
                "[attach:Odip Analiza] Root cause: non-Oracle workspace shell probed missing /opt/oracle/instantclient",
            }));

            var entries = new List<CommandLogEntry>();
            await WindowsTerminalLauncher.MirrorAttachDiagnosticsAsync(logPath, entry => entries.Add(entry), CancellationToken.None);

            Assert.Contains(entries, entry => entry.Message.Contains("[attach:Odip Analiza]", StringComparison.Ordinal));
            Assert.Contains(entries, entry => entry.Message.Contains("Attempted command:", StringComparison.Ordinal));
            Assert.Contains(entries, entry => entry.Message.Contains("docker exec failed with exit code 1", StringComparison.Ordinal));
            Assert.Contains(entries, entry => entry.Message.Contains("Failed at line 16", StringComparison.Ordinal));
            Assert.Contains(entries, entry => entry.Message.Contains("Root cause:", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }

    [Fact]
    public async Task AttachPreflight_DoesNotMaskScriptFailure()
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"ocwm-attach-log-{Guid.NewGuid():N}.log");
        try
        {
            File.WriteAllText(logPath, string.Join(Environment.NewLine, new[]
            {
                "[attach:Odip Analiza] Verified script exists: /opt/opencode-workspace/config/opencode-workspace-shell.sh",
                "[attach:Odip Analiza] Verified script is executable: /opt/opencode-workspace/config/opencode-workspace-shell.sh",
                "[attach:Odip Analiza] Verified user exists: opencode",
                "[attach:Odip Analiza] Verified working directory exists: /workspace",
                "[attach:Odip Analiza] Preflight checks passed.",
                "[attach] Failed at line 16: oracle_client_home=$(find /opt/oracle/instantclient ...)",
                "[attach:Odip Analiza] docker exec failed with exit code 1",
            }));

            var entries = new List<CommandLogEntry>();
            await WindowsTerminalLauncher.MirrorAttachDiagnosticsAsync(logPath, entry => entries.Add(entry), CancellationToken.None);
            var messages = entries.Select(entry => entry.Message).ToList();

            Assert.Contains(messages, message => message.Contains("Preflight checks passed.", StringComparison.Ordinal));
            Assert.Contains(messages, message => message.Contains("Failed at line 16", StringComparison.Ordinal));
            Assert.DoesNotContain(messages, message => message.Contains("Script not found", StringComparison.Ordinal));
            Assert.DoesNotContain(messages, message => message.Contains("Script is not marked executable", StringComparison.Ordinal));
            Assert.DoesNotContain(messages, message => message.Contains("does not exist", StringComparison.Ordinal) && message.Contains("User", StringComparison.Ordinal));
            Assert.DoesNotContain(messages, message => message.Contains("Working directory missing", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }

    [Fact]
    public async Task AttachLifecycle_IsLoggedAsSingleOperation()
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"ocwm-attach-log-{Guid.NewGuid():N}.log");
        try
        {
            File.WriteAllText(logPath, string.Join(Environment.NewLine, new[]
            {
                "[attach:Odip Analiza] Container status: running",
                "[attach:Odip Analiza] Provisioning status: already provisioned",
                "[attach:Odip Analiza] Preflight checks passed.",
                "[attach:Odip Analiza] Attempted command: docker exec --user opencode -w /workspace odip-analiza-workspace bash /opt/opencode-workspace/config/opencode-workspace-shell.sh",
                "[attach:Odip Analiza] Attach session completed successfully.",
            }));

            var entries = new List<CommandLogEntry>();
            await WindowsTerminalLauncher.MirrorAttachDiagnosticsAsync(logPath, entry => entries.Add(entry), CancellationToken.None);
            var messages = entries.Select(entry => entry.Message).ToList();

            Assert.All(messages, message => Assert.Contains("[attach:Odip Analiza]", message));

            var containerIndex = messages.FindIndex(message => message.Contains("Container status", StringComparison.Ordinal));
            var provisioningIndex = messages.FindIndex(message => message.Contains("Provisioning status", StringComparison.Ordinal));
            var preflightIndex = messages.FindIndex(message => message.Contains("Preflight checks passed.", StringComparison.Ordinal));
            var commandIndex = messages.FindIndex(message => message.Contains("Attempted command:", StringComparison.Ordinal));
            var resultIndex = messages.FindIndex(message => message.Contains("completed successfully", StringComparison.Ordinal));

            Assert.True(containerIndex >= 0 && provisioningIndex > containerIndex && preflightIndex > provisioningIndex && commandIndex > preflightIndex && resultIndex > commandIndex);
        }
        finally
        {
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }

    [Fact]
    public void WindowsTerminalLauncherExit_DoesNotMarkAttachFailed_WhenTranscriptStarted()
    {
        var assessment = WindowsTerminalLauncher.AssessLaunchOutcome(
            "[attach:Odip Analiza]",
            "wt.exe new-tab --title \"OpenCode Stuff - Odip Analiza\" -- powershell.exe -NoExit -ExecutionPolicy Bypass -File C:\\Users\\miha.pirnat\\Sources\\Analiza\\attach-workspace.ps1",
            "powershell.exe -NoExit -ExecutionPolicy Bypass -File \"C:\\Users\\miha.pirnat\\Sources\\Analiza\\attach-workspace.ps1\"",
            23032,
            hasExited: true,
            exitCode: 0,
            transcriptLines:
            [
                "[attach:Odip Analiza] Preflight checks passed.",
                "[attach:Odip Analiza] Attempted command: docker exec --user opencode -w /workspace odip-analiza-workspace bash /opt/opencode-workspace/config/opencode-workspace-shell.sh",
            ]);

        Assert.False(assessment.Failed);
        Assert.Contains(assessment.Messages, message => message.Contains("Windows Terminal launch accepted", StringComparison.Ordinal) || message.Contains("attach transcript will be authoritative", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(assessment.Messages, message => message.Contains("Windows Terminal launcher process exited after handoff; attach transcript will be authoritative.", StringComparison.Ordinal));
        Assert.Contains(assessment.Messages, message => message.Contains("Windows Terminal process id: 23032", StringComparison.Ordinal));
        Assert.DoesNotContain(assessment.Messages, message => message.Contains("Windows Terminal launch failed", StringComparison.Ordinal));
        Assert.DoesNotContain(assessment.Messages, message => message.Contains("Terminal window handoff completed.", StringComparison.Ordinal));
    }

    [Fact]
    public void PostStartDiagnosticException_DoesNotBecomeLaunchFailure()
    {
        var messages = WindowsTerminalLauncher.CreatePostStartWarningMessages("[attach:Odip Analiza]");

        Assert.Contains(messages, message => message.Contains("Windows Terminal launch accepted; attach transcript is authoritative.", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("Post-start launcher verification raised a warning", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, message => message.Contains("Windows Terminal launch failed", StringComparison.Ordinal));
    }

    [Fact]
    public void TryDeleteAttachDiagnosticsLog_WhenFileLocked_LogsWarningAndDoesNotThrow()
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"ocwm-attach-lock-{Guid.NewGuid():N}.log");
        File.WriteAllText(logPath, "locked");
        var entries = new List<CommandLogEntry>();

        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var method = typeof(WindowsTerminalLauncher).GetMethod("TryDeleteAttachDiagnosticsLog", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var exception = Record.Exception(() => method.Invoke(null, [logPath, (Action<CommandLogEntry>?)(entry => entries.Add(entry)), "[attach:Odip Analiza]"]));

        Assert.Null(exception);
        Assert.True(File.Exists(logPath));
        Assert.Contains(entries, entry => entry.Message.Contains("Attach diagnostics log is locked and will be preserved", StringComparison.Ordinal));

        stream.Dispose();
        File.Delete(logPath);
    }
}
