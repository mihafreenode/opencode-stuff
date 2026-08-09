using ModelContextProtocol.Client;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;

namespace OpenCode.Workspace.Mcp.Tests;

internal sealed class PackagedProcessHarness : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _name;
    private readonly BoundedLineBuffer _stdoutTail = new(80);
    private readonly BoundedLineBuffer _stderrTail = new(80);
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private readonly List<string> _stdoutLines = [];
    private readonly Task _stdoutTask;
    private readonly Task _stderrTask;
    private bool _stdinClosed;
    private bool _gracefulShutdownRequested;
    private bool _forcedKillRequired;

    private PackagedProcessHarness(Process process, string name, string executablePath, IReadOnlyList<string> arguments, string workingDirectory)
    {
        _process = process;
        _name = name;
        Report = new PackagedProcessLifecycleReport
        {
            Name = name,
            ExecutablePath = executablePath,
            Arguments = arguments.ToArray(),
            WorkingDirectory = workingDirectory,
            ProcessId = process.Id,
            StartedUtc = DateTimeOffset.UtcNow,
            RedirectsStandardInput = process.StartInfo.RedirectStandardInput,
            RedirectsStandardOutput = process.StartInfo.RedirectStandardOutput,
            RedirectsStandardError = process.StartInfo.RedirectStandardError,
        };
        _stdoutTask = CaptureAsync(process.StandardOutput, _stdout, _stdoutTail, _stdoutLines);
        _stderrTask = CaptureAsync(process.StandardError, _stderr, _stderrTail);
    }

    public PackagedProcessLifecycleReport Report { get; }
    public string StandardOutput => _stdout.ToString();
    public string StandardError => _stderr.ToString();
    public IReadOnlyList<string> StandardErrorLines => _stderr.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    public IReadOnlyList<string> StandardOutputLines { get { lock (_stdoutLines) return _stdoutLines.ToArray(); } }
    public bool HasExited => _process.HasExited;
    public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

    public static Task<PackagedProcessHarness> StartAsync(string name, string executablePath, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string?>? environment = null)
    {
        Directory.CreateDirectory(workingDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                if (pair.Value is null)
                {
                    startInfo.Environment.Remove(pair.Key);
                }
                else
                {
                    startInfo.Environment[pair.Key] = pair.Value;
                }
            }
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{executablePath}'.");
        return Task.FromResult(new PackagedProcessHarness(process, name, executablePath, arguments, workingDirectory));
    }

    public async Task WaitForExitAsync(TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        try
        {
            await WaitForProcessExitCoreAsync(_process, timeoutCts.Token);
            try
            {
                await Task.WhenAll(_stdoutTask, _stderrTask).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            catch (TimeoutException)
            {
            }

            Report.ExitedUtc = DateTimeOffset.UtcNow;
            Report.ExitCode = _process.ExitCode;
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException(BuildTimeoutMessage(timeout), exception);
        }
    }

    public async Task RequestGracefulShutdownByClosingStandardInputAsync(TimeSpan timeout)
    {
        _gracefulShutdownRequested = true;
        Report.GracefulShutdownRequestedUtc = DateTimeOffset.UtcNow;
        Report.ExitRequestMechanism = "stdin-eof";
        if (!_stdinClosed)
        {
            _process.StandardInput.Close();
            _stdinClosed = true;
            Report.StandardInputClosedUtc = DateTimeOffset.UtcNow;
        }

        await WaitForExitAsync(timeout);
    }

    public async Task WriteStandardInputAsync(string value)
    {
        await _process.StandardInput.WriteLineAsync(value);
        await _process.StandardInput.FlushAsync();
    }

    public async Task ForceKillAsync(TimeSpan timeout)
    {
        _forcedKillRequired = true;
        Report.ForcedTerminationUtc = DateTimeOffset.UtcNow;
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }

        await WaitForExitAsync(timeout);
    }

    public string BuildTimeoutMessage(TimeSpan timeout)
        => $"{_name} did not exit within {timeout}. pid={_process.Id} executable={Report.ExecutablePath} workingDirectory={Report.WorkingDirectory} stdinClosed={_stdinClosed} gracefulShutdownRequested={_gracefulShutdownRequested}{Environment.NewLine}stdout tail:{Environment.NewLine}{_stdoutTail.Render()}{Environment.NewLine}stderr tail:{Environment.NewLine}{_stderrTail.Render()}";

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            try
            {
                await ForceKillAsync(TimeSpan.FromSeconds(10));
            }
            catch
            {
            }
        }

        _process.Dispose();
        Report.ForcedTerminationRequired = _forcedKillRequired;
    }

    private static async Task CaptureAsync(StreamReader reader, StringBuilder target, BoundedLineBuffer tail, List<string>? lines = null)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                return;
            }

            target.AppendLine(line);
            tail.Add(line);
            if (lines is not null)
            {
                lock (lines) lines.Add(line);
            }
        }
    }

    private static async Task WaitForProcessExitCoreAsync(Process process, CancellationToken cancellationToken)
    {
        if (process.HasExited)
        {
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnExited(object? sender, EventArgs args) => completion.TrySetResult();

        process.EnableRaisingEvents = true;
        process.Exited += OnExited;
        try
        {
            if (process.HasExited)
            {
                return;
            }

            using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            await completion.Task;
        }
        finally
        {
            process.Exited -= OnExited;
        }
    }
}

internal sealed class PackagedMcpHarness : IAsyncDisposable
{
    private readonly string _executablePath;
    private readonly string _workingDirectory;
    private readonly IReadOnlyDictionary<string, string?> _environment;
    private readonly BoundedLineBuffer _stderrTail = new(80);
    private readonly List<string> _stderr = [];
    private StdioClientTransport? _transport;
    private McpClient? _client;
    private Process? _process;
    private bool _stdinClosed;

    private PackagedMcpHarness(string executablePath, string workingDirectory, IReadOnlyDictionary<string, string?> environment)
    {
        _executablePath = executablePath;
        _workingDirectory = workingDirectory;
        _environment = environment;
        Report = new PackagedProcessLifecycleReport
        {
            Name = "mcp",
            ExecutablePath = executablePath,
            Arguments = Array.Empty<string>(),
            WorkingDirectory = workingDirectory,
            RedirectsStandardInput = true,
            RedirectsStandardOutput = true,
            RedirectsStandardError = true,
            StartedUtc = DateTimeOffset.UtcNow,
            ExitRequestMechanism = "dispose-client-and-transport",
        };
    }

    public PackagedProcessLifecycleReport Report { get; }
    public McpClient Client => _client ?? throw new InvalidOperationException("MCP client not initialized.");
    public IReadOnlyList<string> StandardErrorLines => _stderr;

    public static async Task<PackagedMcpHarness> StartAsync(string executablePath, string workingDirectory, IReadOnlyDictionary<string, string?> environment, TimeSpan startupTimeout)
    {
        var harness = new PackagedMcpHarness(executablePath, workingDirectory, environment);
        var before = SnapshotProcesses(executablePath);
        var (command, arguments) = GetLaunchCommand(executablePath);
        harness._transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "Packaged OpenCode Workspace MCP",
            Command = command,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            EnvironmentVariables = environment.ToDictionary(pair => pair.Key, pair => pair.Value),
            StandardErrorLines = line =>
            {
                lock (harness._stderr)
                {
                    harness._stderr.Add(line);
                    harness._stderrTail.Add(line);
                }
            },
        });

        using var timeoutCts = new CancellationTokenSource(startupTimeout);
        try
        {
            harness._client = await McpClient.CreateAsync(harness._transport, cancellationToken: timeoutCts.Token);
        }
        catch (OperationCanceledException exception)
        {
            var stderr = string.Join(Environment.NewLine, harness._stderr);
            throw new TimeoutException($"MCP initialization timed out for '{executablePath}'. command='{command}'. stderr tail:{Environment.NewLine}{harness._stderrTail.Render()}{Environment.NewLine}{stderr}", exception);
        }

        harness._process = await DetectNewProcessAsync(executablePath, before, startupTimeout);
        harness.Report.ProcessId = harness._process.Id;

        return harness;
    }

    public async Task DisposeClientAndTransportAsync(TimeSpan shutdownTimeout)
    {
        Report.GracefulShutdownRequestedUtc = DateTimeOffset.UtcNow;
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }

        if (!_stdinClosed && _process is not null && !_process.HasExited)
        {
            _process.StandardInput.Close();
            _stdinClosed = true;
            Report.StandardInputClosedUtc = DateTimeOffset.UtcNow;
        }

        if (_transport is not null)
        {
            await TransportDisposal.TryDisposeAsync(_transport);
            _transport = null;
        }

        if (_process is not null)
        {
            try
            {
                await WaitForProcessExitCoreAsync(_process, new CancellationTokenSource(shutdownTimeout).Token);
                Report.ExitedUtc = DateTimeOffset.UtcNow;
                Report.ExitCode = TryGetExitCode(_process);
            }
            catch (OperationCanceledException exception)
            {
                throw new TimeoutException($"MCP host did not exit after client/transport disposal within {shutdownTimeout}. pid={_process.Id} executable={_executablePath}{Environment.NewLine}stderr tail:{Environment.NewLine}{_stderrTail.Render()}", exception);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await DisposeClientAndTransportAsync(TimeSpan.FromSeconds(20));
        }
        catch
        {
            if (_process is { HasExited: false })
            {
                Report.ForcedTerminationRequired = true;
                Report.ForcedTerminationUtc = DateTimeOffset.UtcNow;
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        finally
        {
            _process?.Dispose();
            if (_transport is not null)
            {
                await TransportDisposal.TryDisposeAsync(_transport);
            }

        }
    }

    private static async Task WaitForProcessExitCoreAsync(Process process, CancellationToken cancellationToken)
    {
        if (process.HasExited)
        {
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnExited(object? sender, EventArgs args) => completion.TrySetResult();

        process.EnableRaisingEvents = true;
        process.Exited += OnExited;
        try
        {
            if (process.HasExited)
            {
                return;
            }

            using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            await completion.Task;
        }
        finally
        {
            process.Exited -= OnExited;
        }
    }

    private static (string Command, IList<string> Arguments) GetLaunchCommand(string executablePath)
    {
        if (!OperatingSystem.IsWindows() && executablePath.Contains(' ', StringComparison.Ordinal))
        {
            return ("/usr/bin/env", [executablePath]);
        }

        return (executablePath, []);
    }

    private static async Task<Process> DetectNewProcessAsync(string executablePath, IReadOnlyDictionary<int, DateTimeOffset?> before, TimeSpan timeout)
    {
        var physicalExecutablePath = UnixPackageArchive.ResolvePhysicalPath(executablePath);
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < timeout)
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (before.ContainsKey(process.Id))
                    {
                        process.Dispose();
                        continue;
                    }

                    var path = process.MainModule?.FileName;
                    if (path is not null && string.Equals(UnixPackageArchive.ResolvePhysicalPath(path), physicalExecutablePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return process;
                    }
                }
                catch
                {
                    process.Dispose();
                }
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Could not detect MCP child process for '{executablePath}'.");
    }

    private static IReadOnlyDictionary<int, DateTimeOffset?> SnapshotProcesses(string executablePath)
    {
        var physicalExecutablePath = UnixPackageArchive.ResolvePhysicalPath(executablePath);
        var results = new Dictionary<int, DateTimeOffset?>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (path is not null && string.Equals(UnixPackageArchive.ResolvePhysicalPath(path), physicalExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    results[process.Id] = TryGetStartTime(process);
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return results;
    }

    private static DateTimeOffset? TryGetStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch
        {
            return null;
        }
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class PackagedProcessLifecycleReport
{
    public string Name { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public string WorkingDirectory { get; init; } = string.Empty;
    public int ProcessId { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public bool RedirectsStandardInput { get; init; }
    public bool RedirectsStandardOutput { get; init; }
    public bool RedirectsStandardError { get; init; }
    public string ExitRequestMechanism { get; set; } = string.Empty;
    public DateTimeOffset? GracefulShutdownRequestedUtc { get; set; }
    public DateTimeOffset? StandardInputClosedUtc { get; set; }
    public DateTimeOffset? ExitedUtc { get; set; }
    public int? ExitCode { get; set; }
    public DateTimeOffset? ForcedTerminationUtc { get; set; }
    public bool ForcedTerminationRequired { get; set; }
}

internal sealed class BoundedLineBuffer
{
    private readonly int _capacity;
    private readonly ConcurrentQueue<string> _lines = new();

    public BoundedLineBuffer(int capacity)
    {
        _capacity = capacity;
    }

    public void Add(string line)
    {
        _lines.Enqueue(line);
        while (_lines.Count > _capacity && _lines.TryDequeue(out _))
        {
        }
    }

    public string Render()
        => string.Join(Environment.NewLine, _lines);
}

internal static class PackagedHostValidationHelpers
{
    public static async Task WaitForApiHealthyAsync(HttpClient client, TimeSpan timeout)
    {
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < timeout)
        {
            try
            {
                var response = await client.GetAsync("api/v1/health/live");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"API host did not become healthy within {timeout}.");
    }

    public static int GetFreeTcpPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
