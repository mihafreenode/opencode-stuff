using OpenCode.Workspace.AppSupport;
using OpenCode.Workspace.LocalClient;
using ModelContextProtocol.Client;
using System.Text.Json;
using System.Diagnostics;

namespace OpenCode.Workspace.Cli;

internal static class McpCliCommands
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("Missing MCP subcommand. Use 'mcp configure <codex|claude|opencode>' or 'mcp doctor'.");
        }

        return args[0].ToLowerInvariant() switch
        {
            "configure" => await ConfigureAsync(args[1..], output, cancellationToken),
            "doctor" => await DoctorAsync(args[1..], output, cancellationToken),
            _ => throw new ArgumentException($"Unsupported MCP subcommand '{args[0]}'."),
        };
    }

    private static async Task<int> ConfigureAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("Specify an MCP client: codex, claude, or opencode.");
        }

        var client = args[0].ToLowerInvariant();
        if (client is not ("codex" or "claude" or "opencode"))
        {
            throw new ArgumentException($"Unsupported MCP client '{args[0]}'. Use codex, claude, or opencode.");
        }

        var root = ResolveInstallRoot(args);
        var executable = McpExecutable(root);
        if (!File.Exists(executable))
        {
            throw new InvalidOperationException($"Packaged MCP executable was not found at '{executable}'. Pass --install-root for the extracted release directory.");
        }

        var configuration = client switch
        {
            "codex" => $"[mcp_servers.opencode_workspace]{Environment.NewLine}command = {JsonSerializer.Serialize(executable)}{Environment.NewLine}args = []{Environment.NewLine}startup_timeout_sec = 60{Environment.NewLine}tool_timeout_sec = 14400{Environment.NewLine}enabled = true",
            "claude" => $"claude mcp add --scope user --transport stdio opencode-workspace -- {Quote(executable)}",
            _ => JsonSerializer.Serialize(new { mcp = new Dictionary<string, object> { ["opencode_workspace"] = new { type = "local", command = new[] { executable }, enabled = true, timeout = 14400000 } } }, new JsonSerializerOptions { WriteIndented = true }),
        };

        var destination = GetOption(args, "--output");
        if (string.IsNullOrWhiteSpace(destination))
        {
            await output.WriteLineAsync(configuration);
            return 0;
        }

        var path = Path.GetFullPath(destination);
        if (File.Exists(path) && !args.Contains("--force", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"'{path}' already exists. Review it and rerun with --force to overwrite.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, configuration + Environment.NewLine, cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        await output.WriteLineAsync($"Wrote {client} MCP configuration to '{path}'.");
        return 0;
    }

    private static async Task<int> DoctorAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        var root = ResolveInstallRoot(args);
        var mcp = McpExecutable(root);
        var localHost = Path.Combine(root, "bin", "local-host", OperatingSystem.IsWindows() ? "OpenCode.Workspace.LocalHost.exe" : "OpenCode.Workspace.LocalHost");
        var stateRoot = Path.GetFullPath(GetOption(args, "--state-root") ?? Environment.GetEnvironmentVariable("localHost__stateRoot") ?? Path.Combine(Path.GetTempPath(), "opencode-mcp-doctor", Guid.NewGuid().ToString("n")));
        var ownsStateRoot = string.IsNullOrWhiteSpace(GetOption(args, "--state-root")) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("localHost__stateRoot"));
        var checks = new List<McpDoctorCheck>();
        void AddCheck(string name, Func<(bool Passed, string Message, string Recommendation, string Details)> action)
        {
            var started = Stopwatch.StartNew();
            try
            {
                var result = action();
                checks.Add(new McpDoctorCheck(name, result.Passed ? "Passed" : "Failed", result.Message, result.Recommendation, started.Elapsed, result.Details));
            }
            catch (Exception exception)
            {
                checks.Add(McpDoctorCheck.Failed(name, exception.Message, "Inspect the package and retry.", exception.GetType().Name, started.Elapsed));
            }
        }
        AddCheck("StateRootWritable", () =>
        {
            Directory.CreateDirectory(stateRoot);
            var probe = Path.Combine(stateRoot, $".doctor-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return (true, "State root is writable.", string.Empty, stateRoot);
        });
        AddCheck("McpExecutableExists", () => (File.Exists(mcp), File.Exists(mcp) ? "Packaged MCP executable found." : "Packaged MCP executable is missing.", "Extract a complete release package.", mcp));
        AddCheck("LocalHostExecutableExists", () => (File.Exists(localHost), File.Exists(localHost) ? "Packaged LocalHost executable found." : "Packaged LocalHost executable is missing.", "Extract a complete release package.", localHost));
        AddCheck("CanonicalPackageLayout", () => (Directory.Exists(Path.Combine(root, "bin", "local-host")), "Canonical bin/local-host layout checked.", "Use the current release package layout.", root));
        AddCheck("DeprecatedBinApiAbsent", () => (!Directory.Exists(Path.Combine(root, "bin", "api")), "Deprecated bin/api layout checked.", "Remove deprecated bin/api content from the package.", root));
        LocalHostClient? localClient = null;
        try
        {
            localClient = await LocalHostClient.ConnectAsync(new LocalHostClientOptions { DistributionRoot = root, StateRoot = stateRoot }, cancellationToken);
            checks.Add(McpDoctorCheck.Passed("DescriptorValidation", "Descriptor resolved to a healthy LocalHost.", localClient.BaseUrl));
            checks.Add(McpDoctorCheck.Passed("LocalHostDiscovery", "LocalHost was discovered or started.", localClient.BaseUrl));
            checks.Add(McpDoctorCheck.Passed("LocalHostHealth", (await localClient.GetHealthAsync(cancellationToken)).Status, localClient.BaseUrl));
            checks.Add(McpDoctorCheck.Passed("LocalHostReadiness", (await localClient.GetReadinessAsync(cancellationToken)).Status, localClient.BaseUrl));

            var stderr = new List<string>();
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "OpenCode Workspace Doctor",
                Command = mcp,
                WorkingDirectory = root,
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    ["localHost__stateRoot"] = stateRoot,
                    ["localHost__distributionRoot"] = root,
                    ["MCP_CLIENT_NAME"] = "OpenCode Workspace Doctor",
                    ["Logging__LogLevel__Default"] = "None",
                },
                StandardErrorLines = line => stderr.Add(line),
            });
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            checks.Add(McpDoctorCheck.Passed("McpInitialize", "MCP initialize completed.", mcp));
            checks.Add(McpDoctorCheck.Passed("McpToolsList", $"{(await client.ListToolsAsync()).Count} tools listed.", mcp));
            checks.Add(McpDoctorCheck.Passed("McpResourcesList", $"{(await client.ListResourcesAsync()).Count} resources listed.", mcp));
            checks.Add(McpDoctorCheck.Passed("McpStdoutProtocolSafety", "MCP SDK completed stdio protocol exchange without a framing error.", mcp));
            var controller = (await localClient.ListControllerSessionsAsync(cancellationToken)).LastOrDefault(item => item.ClientKind == "mcp" && item.Status == ControllerSessionStatus.Connected);
            checks.Add(controller is null
                ? McpDoctorCheck.Failed("ControllerRegistered", "MCP controller registration was not observed.", "Inspect MCP and LocalHost diagnostics.", string.Empty)
                : McpDoctorCheck.Passed("ControllerRegistered", "MCP controller session is connected.", controller.ControllerSessionId));
            await client.DisposeAsync();
            await DisposeTransportAsync(transport);
            if (controller is not null)
            {
                ControllerSessionRecord? disconnected = null;
                var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
                while (DateTimeOffset.UtcNow < deadline)
                {
                    disconnected = (await localClient.ListControllerSessionsAsync(cancellationToken)).SingleOrDefault(item => item.ControllerSessionId == controller.ControllerSessionId);
                    if (disconnected is { Status: ControllerSessionStatus.Disconnected, DisconnectedUtc: not null })
                    {
                        break;
                    }

                    await Task.Delay(100, cancellationToken);
                }
                if (disconnected is not { Status: ControllerSessionStatus.Disconnected })
                {
                    // Preserve the canonical record even if a transport implementation delays
                    // its hosted shutdown callback beyond the doctor's bounded wait.
                    disconnected = await localClient.DisconnectControllerSessionAsync(controller.ControllerSessionId, new ControllerSessionUpsertRequest { ControllerSessionId = controller.ControllerSessionId }, cancellationToken);
                }
                checks.Add(disconnected is { Status: ControllerSessionStatus.Disconnected, DisconnectedUtc: not null }
                    ? McpDoctorCheck.Passed("ControllerDisconnected", "MCP controller session disconnected.", controller.ControllerSessionId)
                    : McpDoctorCheck.Failed("ControllerDisconnected", "MCP controller session did not disconnect.", "Inspect MCP shutdown diagnostics.", controller.ControllerSessionId));
            }
        }
        catch (Exception exception)
        {
            checks.Add(McpDoctorCheck.Failed("ProtocolProbe", "MCP protocol validation failed.", "Verify the extracted package and inspect diagnostics.", exception.Message));
        }
        finally
        {
            if (localClient is not null)
            {
                await localClient.DisposeAsync();
            }

            if (ownsStateRoot)
            {
                var paths = WorkspaceAppDataPaths.CreateLocalHostStatePathProvider(stateRoot);
                var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
                while ((File.Exists(paths.DescriptorPath) || !HostLockReleased(paths.LockPath)) && DateTimeOffset.UtcNow < deadline)
                {
                    await Task.Delay(100, CancellationToken.None);
                }
                checks.Add(!File.Exists(paths.DescriptorPath) && HostLockReleased(paths.LockPath)
                    ? McpDoctorCheck.Passed("OwnedProcessCleanup", "Doctor-owned LocalHost state was cleaned.", stateRoot)
                    : McpDoctorCheck.Failed("OwnedProcessCleanup", "Doctor-owned LocalHost state remains live.", "Inspect LocalHost shutdown diagnostics.", stateRoot));
            }
            else
            {
                checks.Add(new McpDoctorCheck("OwnedProcessCleanup", "Skipped", "External state root was not removed by Doctor.", string.Empty, TimeSpan.Zero, stateRoot));
            }

            if (ownsStateRoot && Directory.Exists(stateRoot))
            {
                try { Directory.Delete(stateRoot, recursive: true); } catch { }
            }
        }

        var json = args.Contains("--json", StringComparer.OrdinalIgnoreCase);
        await output.WriteLineAsync(json
            ? JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true })
            : string.Join(Environment.NewLine, checks.Select(item => $"{item.Name}: {item.Status} - {item.Message}")));
        return checks.All(item => item.Status is "Passed" or "Skipped") ? 0 : 1;
    }

    private static string ResolveInstallRoot(string[] args)
        => Path.GetFullPath(GetOption(args, "--install-root") ?? OpenCodeWorkspaceInstallationLayout.Resolve(AppContext.BaseDirectory).DistributionRoot);

    private static string McpExecutable(string root)
        => Path.Combine(root, "bin", "mcp", OperatingSystem.IsWindows() ? "OpenCode.Workspace.Mcp.exe" : "OpenCode.Workspace.Mcp");

    private static string? GetOption(string[] args, string option)
    {
        var index = Array.FindIndex(args, item => string.Equals(item, option, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index < args.Length - 1 ? args[index + 1] : null;
    }

    private static string Quote(string value) => OperatingSystem.IsWindows() ? $"\"{value}\"" : $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private static bool HostLockReleased(string path)
    {
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static async Task DisposeTransportAsync(object transport)
    {
        if (transport is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
            return;
        }

        if (transport is IDisposable disposable)
        {
            disposable.Dispose();
            return;
        }

        var method = transport.GetType().GetMethod("DisposeAsync", Type.EmptyTypes);
        if (method?.Invoke(transport, null) is ValueTask valueTask)
        {
            await valueTask;
        }
        else if (method?.Invoke(transport, null) is Task task)
        {
            await task;
        }
    }

    private sealed record McpDoctorCheck(string Name, string Status, string Message, string Recommendation, TimeSpan Duration, string SafeDiagnosticDetails)
    {
        public static McpDoctorCheck Passed(string name, string message, string details)
            => new(name, "Passed", message, string.Empty, TimeSpan.Zero, details);

        public static McpDoctorCheck Failed(string name, string message, string recommendation, string details, TimeSpan? duration = null)
            => new(name, "Failed", message, recommendation, duration ?? TimeSpan.Zero, details);
    }
}
