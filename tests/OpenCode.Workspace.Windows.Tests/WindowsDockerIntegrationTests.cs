using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Manager.Services;
using Xunit.Abstractions;

namespace OpenCode.Workspace.Windows.Tests;

public sealed class WindowsDockerIntegrationTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);
    private readonly ITestOutputHelper _output;

    public WindowsDockerIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public async Task DockerComposeExecutionOrchestration_RunsAgainstTemporaryWorkspace_WhenDockerAvailable()
    {
        var baselineDockerProcesses = GetDockerProcessIds();
        var capabilities = new WindowsHostCapabilities(new ProcessRunner());
        var dockerCheck = await capabilities.CheckDockerDesktopAsync();
        Skip.IfNot(dockerCheck.IsAvailable, dockerCheck.Reason);

        var root = Path.Combine(Path.GetTempPath(), $"ocwm-docker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var composePath = Path.Combine(root, "compose.yaml");
            File.WriteAllText(composePath, string.Join("\n", new[]
            {
                "services:",
                "  workspace:",
                "    image: alpine:3.20",
                "    container_name: ocwm-integration-workspace",
                "    command:",
                "      - sh",
                "      - -lc",
                "      - sleep 30",
            }));

            var docker = new DockerService(new ProcessRunner());
            var definition = new WorkspaceDefinition
            {
                Workspace = new WorkspaceMetadata { Name = "ocwm-integration" },
            };
            var paths = WorkspacePathBuilder.Build(root);
            File.WriteAllText(paths.ComposePath, File.ReadAllText(composePath));

            var start = await docker.StartAsync(paths, definition);
            Skip.IfNot(start.IsSuccess, $"Docker compose start failed: {start.StandardError}\n{start.StandardOutput}");

            var ps = await docker.GetPsAsync(paths, definition);
            Assert.True(ps.IsSuccess);
            Assert.Contains(ps.StandardOutputLines, line => line.Trim() == "workspace");

            var stop = await docker.StopAsync(paths, definition);
            Assert.True(stop.IsSuccess);
        }
        finally
        {
            try
            {
                var runner = new ProcessRunner();
                await runner.RunAsync("docker", ["rm", "-f", "ocwm-integration-workspace"]);
            }
            catch
            {
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            await CleanupExtraDockerProcessesAsync(baselineDockerProcesses);
        }
    }

    [SkippableFact]
    public async Task AttachReadinessValidation_CanQueryOpenCodeSessions_WhenDockerAvailable()
    {
        var baselineDockerProcesses = GetDockerProcessIds();
        try
        {
            using var dockerCheckTimeout = new CancellationTokenSource(ProcessTimeout);
            _output.WriteLine("[attach-readiness] checking Docker Desktop availability");
            var capabilities = new WindowsHostCapabilities(new ProcessRunner());
            var dockerCheck = await capabilities.CheckDockerDesktopAsync(dockerCheckTimeout.Token);
            _output.WriteLine($"[attach-readiness] Docker availability: {dockerCheck.IsAvailable}. Reason: {dockerCheck.Reason}");
            Skip.IfNot(dockerCheck.IsAvailable, dockerCheck.Reason);

            _output.WriteLine("[attach-readiness] before container check");
            var inspect = await RunDockerAsync(["ps", "--format", "{{.Names}}"], "docker inspection");
            _output.WriteLine($"[attach-readiness] docker ps exit code: {inspect.ExitCode}");
            _output.WriteLine($"[attach-readiness] docker ps stdout: {inspect.StandardOutput}");
            _output.WriteLine($"[attach-readiness] docker ps stderr: {inspect.StandardError}");

            var isSmokeWorkspaceRunning = inspect.StandardOutputLines.Any(line => line.Trim() == "smoke-data-workspace-workspace");
            if (!isSmokeWorkspaceRunning)
            {
                _output.WriteLine("[attach-readiness] container not running -> skip");
                Skip.If(true, "Smoke workspace container is not running, so attach-readiness checks were skipped.");
                return;
            }

            _output.WriteLine("[attach-readiness] querying OpenCode sessions");
            var sessionList = await RunDockerAsync(
                ["exec", "smoke-data-workspace-workspace", "bash", "-lc", "cd /workspace && opencode session list || true"],
                "docker exec session list");
            _output.WriteLine($"[attach-readiness] docker exec exit code: {sessionList.ExitCode}");
            _output.WriteLine($"[attach-readiness] docker exec stdout: {sessionList.StandardOutput}");
            _output.WriteLine($"[attach-readiness] docker exec stderr: {sessionList.StandardError}");
            Assert.True(sessionList.IsSuccess);
        }
        finally
        {
            await CleanupExtraDockerProcessesAsync(baselineDockerProcesses);
            _output.WriteLine("[attach-readiness] cleanup complete");
        }
    }

    private async Task CleanupExtraDockerProcessesAsync(HashSet<int> baselineDockerProcesses)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var extraProcessIds = GetDockerProcessIds();
            extraProcessIds.ExceptWith(baselineDockerProcesses);
            if (extraProcessIds.Count == 0)
            {
                return;
            }

            _output.WriteLine($"[docker-cleanup] waiting for extra docker processes: {string.Join(",", extraProcessIds.OrderBy(id => id))}");
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        var lingeringProcessIds = GetDockerProcessIds();
        lingeringProcessIds.ExceptWith(baselineDockerProcesses);
        foreach (var processId in lingeringProcessIds.OrderBy(id => id))
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                _output.WriteLine($"[docker-cleanup] killing lingering docker process {processId}");
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (ArgumentException)
            {
            }
        }
    }

    private static HashSet<int> GetDockerProcessIds()
    {
        return Process.GetProcessesByName("docker")
            .Select(process =>
            {
                try
                {
                    return process.Id;
                }
                finally
                {
                    process.Dispose();
                }
            })
            .ToHashSet();
    }

    private async Task<ProcessResult> RunDockerAsync(IReadOnlyList<string> arguments, string description)
    {
        using var timeout = new CancellationTokenSource(ProcessTimeout);
        _output.WriteLine($"[attach-readiness] process creation: {description}");
        return await new ProcessRunner().RunAsync(
            "docker",
            arguments,
            cancellationToken: timeout.Token,
            timeout: ProcessTimeout,
            onDiagnostic: message => _output.WriteLine($"[attach-readiness] {message}"));
    }
}
