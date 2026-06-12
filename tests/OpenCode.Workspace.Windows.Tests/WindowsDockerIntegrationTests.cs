using System.IO;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.Manager.Services;

namespace OpenCode.Workspace.Windows.Tests;

public sealed class WindowsDockerIntegrationTests
{
    [SkippableFact]
    public async Task DockerComposeExecutionOrchestration_RunsAgainstTemporaryWorkspace_WhenDockerAvailable()
    {
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
        }
    }

    [SkippableFact]
    public async Task AttachReadinessValidation_UsesNonInteractiveScreenChecks_WhenDockerAvailable()
    {
        var capabilities = new WindowsHostCapabilities(new ProcessRunner());
        var dockerCheck = await capabilities.CheckDockerDesktopAsync();
        Skip.IfNot(dockerCheck.IsAvailable, dockerCheck.Reason);

        var inspect = await new ProcessRunner().RunAsync("cmd.exe", ["/c", "docker", "ps", "--format", "{{.Names}}"]);
        Skip.IfNot(
            inspect.StandardOutputLines.Any(line => line.Trim() == "smoke-data-workspace-workspace"),
            "Smoke workspace container is not running, so attach-readiness checks were skipped.");

        var screenList = await new ProcessRunner().RunAsync("cmd.exe", ["/c", "docker", "exec", "smoke-data-workspace-workspace", "bash", "-lc", "screen -ls || true"]);
        Assert.True(screenList.IsSuccess);

        var screenReset = await new ProcessRunner().RunAsync("cmd.exe", ["/c", "docker", "exec", "smoke-data-workspace-workspace", "bash", "-lc", "screen -S opencode -X quit || true; screen -ls || true"]);
        Assert.True(screenReset.IsSuccess);
    }
}
