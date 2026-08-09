using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using OpenCode.Workspace.Core.Runtime;
using OpenCode.Workspace.Core.Workspaces;
using OpenCode.Workspace.LocalClient;
using OpenCode.Workspace.Mcp;

namespace OpenCode.Workspace.Api;

// Platform adapters own native handles. LocalHost only exposes byte streams and metadata.
public interface IInteractiveTerminalRuntime
{
    Task<InteractiveTerminalRuntimeRecord> StartAsync(InteractiveAgentSessionRecord session, string fileName, IReadOnlyList<string> arguments, string workingDirectory, InteractiveTerminalDimensions dimensions, CancellationToken cancellationToken);
    Task WriteAsync(byte[] data, CancellationToken cancellationToken);
    Task ResizeAsync(InteractiveTerminalDimensions dimensions, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    InteractiveTerminalRuntimeRecord Record { get; }
    event Action<byte[]>? Output;
    event Action<InteractiveTerminalRuntimeRecord>? Changed;
}

// This stays internal: terminal clients receive identities and credentials, never a process command.
internal sealed record InteractiveProviderLaunchSpecification
{
    public required string Executable { get; init; }
    public required IReadOnlyList<string> ArgumentList { get; init; }
    public required string WorkingDirectory { get; init; }
    public required IReadOnlyDictionary<string, string> EnvironmentOverrides { get; init; }
    public required string ProviderKind { get; init; }
    public required string WorkspaceId { get; init; }
    public required string InteractiveAgentSessionId { get; init; }
    public string? ExistingProviderSessionId { get; init; }
}

public sealed class InteractiveTerminalRuntimeService(
    InteractiveAgentSessionService sessions,
    IOpenCodeWorkspaceMcpService workspaces,
    ISystemClock clock,
    LocalHostStateStore stateStore,
    IProviderSessionDiscovery? providerSessions = null,
    Func<IInteractiveTerminalRuntime>? runtimeFactory = null,
    TimeSpan? providerCorrelationTimeout = null)
{
    // 1 MiB is intentionally transient: enough for reconnects without retaining a transcript.
    private const int MaximumBufferedBytes = 1024 * 1024;
    private readonly ConcurrentDictionary<string, RuntimeState> _runtimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private int _loaded;
    internal static bool IsDeterministicTestRuntimeEnabled
        => string.Equals(Environment.GetEnvironmentVariable("OPENCODE_LOCALHOST_ENABLE_TERMINAL_TEST_RUNTIME"), "1", StringComparison.Ordinal);

    public async Task<InteractiveTerminalRuntimeRecord> StartAsync(string sessionId, StartInteractiveTerminalRequest request, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        await _startGate.WaitAsync(cancellationToken);
        try
        {
        var session = await sessions.GetAsync(sessionId, cancellationToken);
        if (_runtimes.TryGetValue(sessionId, out var existing) && existing.Runtime.Record.Status is InteractiveTerminalRuntimeStatus.Starting or InteractiveTerminalRuntimeStatus.Running or InteractiveTerminalRuntimeStatus.Stopping)
        {
            return existing.Snapshot();
        }

        // A supplied adapter is the explicit test seam; production still only hosts ConPTY on Windows.
        if (!OperatingSystem.IsWindows() && runtimeFactory is null)
        {
            var unavailable = RuntimeState.Unavailable(session, clock.UtcNow, request.Dimensions);
            _runtimes[sessionId] = unavailable;
            await PersistAsync(unavailable.Snapshot(), cancellationToken);
            return unavailable.Snapshot();
        }

        ValidateDimensions(request.Dimensions);
        var workspaceName = IsDeterministicTestRuntimeEnabled
            ? session.WorkspaceId
            : (await workspaces.GetWorkspaceAsync(session.WorkspaceId, cancellationToken)).Snapshot.Definition.Workspace.Name;
        var containerName = $"{WorkspacePathBuilder.Slugify(workspaceName)}-workspace";
        IReadOnlySet<string>? sessionsBeforeLaunch = null;
        if (!IsDeterministicTestRuntimeEnabled && string.IsNullOrWhiteSpace(session.ProviderSessionId) && providerSessions is not null)
        {
            sessionsBeforeLaunch = await providerSessions.ListWorkspaceSessionIdsAsync(containerName, cancellationToken);
        }
        var runtime = runtimeFactory?.Invoke() ?? new WindowsConPtyTerminalRuntime();
        var state = new RuntimeState(runtime, session, request.Dimensions, clock.UtcNow, QueuePersistence);
        runtime.Output += state.AppendOutput;
        runtime.Changed += changed =>
        {
            state.Update(changed);
            QueuePersistence(state.Snapshot());
        };
        _runtimes[sessionId] = state;
        await PersistAsync(state.Snapshot(), cancellationToken);
        try
        {
            var launch = BuildProviderLaunchSpecification(session, workspaceName);
            await runtime.StartAsync(session, launch.Executable, launch.ArgumentList, launch.WorkingDirectory, request.Dimensions, cancellationToken);
            state.Update(runtime.Record);
            if (!string.IsNullOrWhiteSpace(session.ProviderSessionId))
            {
                session = await sessions.RecordProviderSessionIdentityAsync(sessionId, session.ProviderSessionId, ProviderSessionIdentitySource.ExistingCanonicalIdentity, cancellationToken);
                state.SetProviderSession(session.ProviderSessionId);
            }
            else if (sessionsBeforeLaunch is not null && providerSessions is not null)
            {
                try
                {
                    var sessionsAfterLaunch = await DiscoverAfterLaunchAsync(providerSessions, containerName, sessionsBeforeLaunch, providerCorrelationTimeout ?? TimeSpan.FromSeconds(10), cancellationToken);
                    var newIds = sessionsAfterLaunch.Except(sessionsBeforeLaunch, StringComparer.OrdinalIgnoreCase).ToArray();
                    if (newIds.Length == 1)
                    {
                        session = await sessions.RecordProviderSessionIdentityAsync(sessionId, newIds[0], ProviderSessionIdentitySource.LaunchCorrelation, cancellationToken);
                        state.SetProviderSession(session.ProviderSessionId);
                    }
                }
                catch (OpenCodeWorkspaceMcpException) { throw; }
                catch
                {
                    // Correlation is diagnostic after a successful launch; unresolved identity does not stop the runtime.
                }
            }
            var result = state.Snapshot();
            await PersistAsync(result, cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            if (runtime.Record.Status is InteractiveTerminalRuntimeStatus.Starting or InteractiveTerminalRuntimeStatus.Running or InteractiveTerminalRuntimeStatus.Stopping)
            {
                try { await runtime.StopAsync(CancellationToken.None); } catch { }
            }
            state.Update(runtime.Record);
            state.Fail(exception.Message, clock.UtcNow);
            await PersistAsync(state.Snapshot(), CancellationToken.None);
            throw;
        }
        }
        finally
        {
            _startGate.Release();
        }
    }

    public async Task<InteractiveTerminalRuntimeRecord> GetAsync(string sessionId, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var result = _runtimes.TryGetValue(sessionId, out var state)
            ? state.Snapshot()
            : throw new OpenCodeWorkspaceMcpException("terminal_runtime_not_found", $"Interactive terminal runtime for '{sessionId}' was not found.", "Start a terminal runtime first.");
        await PersistAsync(result, cancellationToken);
        return result;
    }

    public async Task<InteractiveTerminalRuntimeRecord> InputAsync(string sessionId, TerminalInputRequest request, CancellationToken cancellationToken)
    {
        var state = Require(sessionId);
        EnsureRunning(state.Runtime.Record.Status, "input");
        await sessions.ValidateTerminalInputAuthorityAsync(sessionId, request.AttachmentId, request.AttachmentToken, cancellationToken);

        byte[] data;
        try { data = Convert.FromBase64String(request.DataBase64); }
        catch (FormatException) { throw new OpenCodeWorkspaceMcpException("invalid_terminal_input", "Terminal input must be base64 encoded bytes.", "Encode terminal bytes as base64."); }
        await state.Runtime.WriteAsync(data, cancellationToken);
        state.Update(state.Runtime.Record);
        var result = state.Snapshot();
        await PersistAsync(result, cancellationToken);
        return result;
    }

    public async Task<InteractiveTerminalRuntimeRecord> ResizeAsync(string sessionId, TerminalResizeRequest request, CancellationToken cancellationToken)
    {
        var dimensions = new InteractiveTerminalDimensions { Columns = request.Columns, Rows = request.Rows };
        ValidateDimensions(dimensions);
        var state = Require(sessionId);
        EnsureRunning(state.Runtime.Record.Status, "resize");
        await sessions.ValidateTerminalInputAuthorityAsync(sessionId, request.AttachmentId, request.AttachmentToken, cancellationToken);
        if (state.Runtime.Record.Dimensions == dimensions) return state.Snapshot();
        await state.Runtime.ResizeAsync(dimensions, cancellationToken);
        state.Update(state.Runtime.Record);
        var result = state.Snapshot();
        await PersistAsync(result, cancellationToken);
        return result;
    }

    public Task<TerminalOutputReadResult> ReadOutputAsync(string sessionId, long afterSequence, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Require(sessionId).ReadOutput(afterSequence));
    }

    public async Task<InteractiveTerminalRuntimeRecord> StopAsync(string sessionId, CancellationToken cancellationToken)
    {
        var state = Require(sessionId);
        await state.Runtime.StopAsync(cancellationToken);
        state.Update(state.Runtime.Record);
        var result = state.Snapshot();
        await PersistAsync(result, cancellationToken);
        return result;
    }

    public void SetActiveAttachment(string sessionId, string attachmentId)
    {
        var state = Require(sessionId);
        state.SetActiveAttachment(attachmentId);
        QueuePersistence(state.Snapshot());
    }

    public void ClearActiveAttachmentIfMatches(string sessionId, string attachmentId)
    {
        var state = Require(sessionId);
        if (state.ClearActiveAttachmentIfMatches(attachmentId)) QueuePersistence(state.Snapshot());
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _loaded) == 1) return;
        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            if (_loaded == 1) return;
            foreach (var sessionDirectory in Directory.Exists(stateStore.InteractiveSessionsRoot)
                         ? Directory.GetDirectories(stateStore.InteractiveSessionsRoot)
                         : Array.Empty<string>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var persisted = stateStore.ReadJson<PersistedTerminalRuntimeMetadata>(Path.Combine(sessionDirectory, "terminal-runtime.json"));
                if (persisted is null || string.IsNullOrWhiteSpace(persisted.InteractiveAgentSessionId)) continue;
                var normalized = persisted.NormalizeAfterRestart(clock.UtcNow);
                _runtimes[normalized.InteractiveAgentSessionId] = RuntimeState.Restored(normalized.ToRecord(), QueuePersistence);
                await stateStore.WriteJsonAsync(Path.Combine(sessionDirectory, "terminal-runtime.json"), normalized, cancellationToken);
            }
            Volatile.Write(ref _loaded, 1);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private Task PersistAsync(InteractiveTerminalRuntimeRecord record, CancellationToken cancellationToken)
        => stateStore.WriteJsonAsync(
            Path.Combine(stateStore.InteractiveSessionsRoot, record.InteractiveAgentSessionId, "terminal-runtime.json"),
            PersistedTerminalRuntimeMetadata.FromRecord(record),
            cancellationToken);

    private void QueuePersistence(InteractiveTerminalRuntimeRecord record)
    {
        _ = PersistAsync(record, CancellationToken.None).ContinueWith(
            static _ => { },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private RuntimeState Require(string sessionId) => _runtimes.TryGetValue(sessionId, out var state) ? state : throw new OpenCodeWorkspaceMcpException("terminal_runtime_not_found", $"Interactive terminal runtime for '{sessionId}' was not found.", "Start a terminal runtime first.");
    private static void EnsureRunning(InteractiveTerminalRuntimeStatus status, string operation)
    {
        if (status == InteractiveTerminalRuntimeStatus.Unavailable) throw new OpenCodeWorkspaceMcpException("terminal_runtime_unavailable", $"Terminal {operation} is unavailable on this host.", "Use a Windows LocalHost.");
        if (status != InteractiveTerminalRuntimeStatus.Running) throw new OpenCodeWorkspaceMcpException($"{operation}_after_exit", $"Terminal {operation} is unavailable because the runtime has exited.", "Start a new terminal runtime.");
    }
    private static InteractiveProviderLaunchSpecification BuildProviderLaunchSpecification(InteractiveAgentSessionRecord session, string workspaceName)
    {
        var testExecutable = Environment.GetEnvironmentVariable("OPENCODE_LOCALHOST_TERMINAL_TEST_EXECUTABLE");
        if (IsDeterministicTestRuntimeEnabled
            && !string.IsNullOrWhiteSpace(testExecutable))
        {
            var resolvedTestExecutable = Path.GetFullPath(testExecutable);
            var localHostRoot = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolvedTestExecutable.StartsWith(localHostRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolvedTestExecutable))
            {
                throw new InvalidOperationException("The terminal test executable must exist beneath the packaged LocalHost directory.");
            }
            return new InteractiveProviderLaunchSpecification
            {
                Executable = resolvedTestExecutable,
                ArgumentList = Array.Empty<string>(),
                WorkingDirectory = AppContext.BaseDirectory,
                EnvironmentOverrides = new Dictionary<string, string>(),
                ProviderKind = "DeterministicConPtyTestChild",
                WorkspaceId = session.WorkspaceId,
                InteractiveAgentSessionId = session.InteractiveAgentSessionId,
                ExistingProviderSessionId = session.ProviderSessionId,
            };
        }

        var containerName = $"{WorkspacePathBuilder.Slugify(workspaceName)}-workspace";
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOME"] = "/home/opencode",
            ["TERM"] = "xterm-256color",
            ["LANG"] = "C.UTF-8",
            ["LC_ALL"] = "C.UTF-8",
        };
        var arguments = new List<string> { "exec", "-i", "-t", "--user", "opencode", "-w", "/workspace" };
        foreach (var item in environment) arguments.AddRange(["-e", $"{item.Key}={item.Value}"]);
        arguments.Add(containerName);
        arguments.Add("opencode");
        if (!string.IsNullOrWhiteSpace(session.ProviderSessionId)) arguments.AddRange(["--session", session.ProviderSessionId]);
        return new InteractiveProviderLaunchSpecification
        {
            Executable = "docker.exe",
            ArgumentList = arguments,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            EnvironmentOverrides = environment,
            ProviderKind = "OpenCodeDockerExec",
            WorkspaceId = session.WorkspaceId,
            InteractiveAgentSessionId = session.InteractiveAgentSessionId,
            ExistingProviderSessionId = session.ProviderSessionId,
        };
    }
    private static async Task<IReadOnlySet<string>> DiscoverAfterLaunchAsync(IProviderSessionDiscovery discovery, string containerName, IReadOnlySet<string> baseline, TimeSpan correlationTimeout, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(correlationTimeout);
        IReadOnlySet<string> latest = baseline;
        while (!timeout.IsCancellationRequested)
        {
            latest = await discovery.ListWorkspaceSessionIdsAsync(containerName, timeout.Token);
            if (latest.Except(baseline, StringComparer.OrdinalIgnoreCase).Any()) return latest;
            await Task.Delay(100, timeout.Token);
        }
        return latest;
    }
    private static void ValidateDimensions(InteractiveTerminalDimensions dimensions)
    {
        if (dimensions.Columns is < 20 or > 500 || dimensions.Rows is < 5 or > 300) throw new OpenCodeWorkspaceMcpException("invalid_terminal_dimensions", "Terminal dimensions are outside supported bounds.", "Use 20-500 columns and 5-300 rows.");
    }

    private sealed class RuntimeState
    {
        private readonly object _gate = new();
        private readonly Queue<(long Sequence, DateTimeOffset Timestamp, byte[] Data)> _output = new();
        private int _bufferedBytes;
        private long _nextSequence;
        public IInteractiveTerminalRuntime Runtime { get; }
        public InteractiveTerminalRuntimeRecord Record { get; private set; }
        private readonly Action<InteractiveTerminalRuntimeRecord> _recordChanged;
        public RuntimeState(IInteractiveTerminalRuntime runtime, InteractiveAgentSessionRecord session, InteractiveTerminalDimensions dimensions, DateTimeOffset now, Action<InteractiveTerminalRuntimeRecord> recordChanged)
        {
            Runtime = runtime;
            _recordChanged = recordChanged;
            Record = new InteractiveTerminalRuntimeRecord { RuntimeId = Guid.NewGuid().ToString("n"), InteractiveAgentSessionId = session.InteractiveAgentSessionId, WorkspaceId = session.WorkspaceId, ProviderSessionId = session.ProviderSessionId, Status = InteractiveTerminalRuntimeStatus.Starting, CreatedUtc = now, UpdatedUtc = now, LastActivityUtc = now, Dimensions = dimensions };
        }
        private RuntimeState(IInteractiveTerminalRuntime runtime, InteractiveTerminalRuntimeRecord record, Action<InteractiveTerminalRuntimeRecord> recordChanged)
        {
            Runtime = runtime;
            Record = record;
            _recordChanged = recordChanged;
        }
        public static RuntimeState Restored(InteractiveTerminalRuntimeRecord record, Action<InteractiveTerminalRuntimeRecord> recordChanged)
            => new(new HistoricalTerminalRuntime(record), record, recordChanged);
        public static RuntimeState Unavailable(InteractiveAgentSessionRecord session, DateTimeOffset now, InteractiveTerminalDimensions dimensions)
        {
            var state = new RuntimeState(new UnsupportedTerminalRuntime(), session, dimensions, now, _ => { });
            state.Record = state.Record with { Status = InteractiveTerminalRuntimeStatus.Unavailable, UpdatedUtc = now };
            return state;
        }
        public void AppendOutput(byte[] data)
        {
            if (data.Length == 0) return;
            lock (_gate)
            {
                var copy = data.ToArray();
                _output.Enqueue((++_nextSequence, DateTimeOffset.UtcNow, copy));
                _bufferedBytes += copy.Length;
                while (_bufferedBytes > MaximumBufferedBytes && _output.TryDequeue(out var discarded)) _bufferedBytes -= discarded.Data.Length;
                var now = DateTimeOffset.UtcNow;
                Record = Record with { UpdatedUtc = now, LastActivityUtc = now, EarliestSequence = _output.TryPeek(out var first) ? first.Sequence : _nextSequence, LatestSequence = _nextSequence };
                _recordChanged(Record);
            }
        }
        public void Update(InteractiveTerminalRuntimeRecord runtime)
        {
            lock (_gate)
            {
                Record = runtime with { RuntimeId = Record.RuntimeId, UpdatedUtc = DateTimeOffset.UtcNow, ActiveAttachmentId = Record.ActiveAttachmentId, EarliestSequence = Record.EarliestSequence, LatestSequence = Record.LatestSequence };
            }
        }
        public InteractiveTerminalRuntimeRecord Snapshot()
        {
            lock (_gate)
            {
                // Native exit observation is asynchronous. Preserve LocalHost-owned metadata while
                // reflecting the adapter's current lifecycle state to every reader.
                if (Runtime.Record.Status != Record.Status)
                {
                    Record = Runtime.Record with { RuntimeId = Record.RuntimeId, UpdatedUtc = DateTimeOffset.UtcNow, ActiveAttachmentId = Record.ActiveAttachmentId, EarliestSequence = Record.EarliestSequence, LatestSequence = Record.LatestSequence };
                }
                return Record;
            }
        }
        public void SetActiveAttachment(string attachmentId) { lock (_gate) Record = Record with { ActiveAttachmentId = attachmentId, UpdatedUtc = DateTimeOffset.UtcNow }; }
        public bool ClearActiveAttachmentIfMatches(string attachmentId)
        {
            lock (_gate)
            {
                if (!string.Equals(Record.ActiveAttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase)) return false;
                Record = Record with { ActiveAttachmentId = string.Empty, UpdatedUtc = DateTimeOffset.UtcNow };
                return true;
            }
        }
        public void SetProviderSession(string? providerSessionId) { lock (_gate) Record = Record with { ProviderSessionId = providerSessionId, UpdatedUtc = DateTimeOffset.UtcNow }; }
        public void Fail(string summary, DateTimeOffset now) { lock (_gate) Record = Record with { Status = InteractiveTerminalRuntimeStatus.Failed, FailureSummary = summary, UpdatedUtc = now, LastActivityUtc = now }; }
        public TerminalOutputReadResult ReadOutput(long after)
        {
            lock (_gate)
            {
                var gap = _output.Count > 0 && after < Record.EarliestSequence - 1;
                var effectiveAfter = gap ? Record.EarliestSequence - 1 : after;
                return new TerminalOutputReadResult
                {
                    EarliestSequence = Record.EarliestSequence,
                    LatestSequence = Record.LatestSequence,
                    GapDetected = gap,
                    RequestedAfterSequence = after,
                    Chunks = _output.Where(item => item.Sequence > effectiveAfter).Select(item => new TerminalOutputChunk { Sequence = item.Sequence, TimestampUtc = item.Timestamp, DataBase64 = Convert.ToBase64String(item.Data) }).ToArray(),
                };
            }
        }
    }
}

internal sealed record PersistedTerminalRuntimeMetadata
{
    public string TerminalRuntimeId { get; init; } = string.Empty;
    public string InteractiveAgentSessionId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public string? ProviderSessionId { get; init; }
    public InteractiveTerminalRuntimeStatus Status { get; init; }
    public int? ProcessId { get; init; }
    public DateTimeOffset? ProcessStartedUtc { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public DateTimeOffset LastActivityUtc { get; init; }
    public int? ExitCode { get; init; }
    public int Columns { get; init; } = 120;
    public int Rows { get; init; } = 30;
    public string FailureSummary { get; init; } = string.Empty;

    public static PersistedTerminalRuntimeMetadata FromRecord(InteractiveTerminalRuntimeRecord record)
        => new()
        {
            TerminalRuntimeId = record.RuntimeId,
            InteractiveAgentSessionId = record.InteractiveAgentSessionId,
            WorkspaceId = record.WorkspaceId,
            ProviderSessionId = record.ProviderSessionId,
            Status = record.Status,
            ProcessId = record.ProcessId,
            ProcessStartedUtc = record.ProcessStartedUtc,
            CreatedUtc = record.CreatedUtc,
            UpdatedUtc = record.UpdatedUtc,
            LastActivityUtc = record.LastActivityUtc,
            ExitCode = record.ExitCode,
            Columns = record.Dimensions.Columns,
            Rows = record.Dimensions.Rows,
            FailureSummary = record.FailureSummary,
        };

    public PersistedTerminalRuntimeMetadata NormalizeAfterRestart(DateTimeOffset now)
    {
        var normalizedStatus = Status switch
        {
            InteractiveTerminalRuntimeStatus.Starting => InteractiveTerminalRuntimeStatus.Unavailable,
            InteractiveTerminalRuntimeStatus.Running => InteractiveTerminalRuntimeStatus.Unavailable,
            InteractiveTerminalRuntimeStatus.Stopping => InteractiveTerminalRuntimeStatus.Exited,
            _ => Status,
        };

        // PID and start time remain historical. Without the original native handles and a persisted
        // executable relationship, process identity cannot be proven, so LocalHost never adopts or kills it.
        return this with
        {
            Status = normalizedStatus,
            UpdatedUtc = normalizedStatus == Status ? UpdatedUtc : now,
            FailureSummary = normalizedStatus == InteractiveTerminalRuntimeStatus.Unavailable && string.IsNullOrWhiteSpace(FailureSummary)
                ? "LocalHost restarted; the native ConPTY runtime cannot be rehydrated."
                : FailureSummary,
        };
    }

    public InteractiveTerminalRuntimeRecord ToRecord()
        => new()
        {
            RuntimeId = TerminalRuntimeId,
            InteractiveAgentSessionId = InteractiveAgentSessionId,
            WorkspaceId = WorkspaceId,
            ProviderSessionId = ProviderSessionId,
            Status = Status,
            ProcessId = ProcessId,
            ProcessStartedUtc = ProcessStartedUtc,
            CreatedUtc = CreatedUtc,
            UpdatedUtc = UpdatedUtc,
            LastActivityUtc = LastActivityUtc,
            ExitCode = ExitCode,
            Dimensions = new InteractiveTerminalDimensions { Columns = Columns, Rows = Rows },
            FailureSummary = FailureSummary,
        };
}

internal sealed class HistoricalTerminalRuntime(InteractiveTerminalRuntimeRecord record) : IInteractiveTerminalRuntime
{
    public InteractiveTerminalRuntimeRecord Record { get; } = record;
    public event Action<byte[]>? Output { add { } remove { } }
    public event Action<InteractiveTerminalRuntimeRecord>? Changed { add { } remove { } }
    public Task<InteractiveTerminalRuntimeRecord> StartAsync(InteractiveAgentSessionRecord session, string fileName, IReadOnlyList<string> arguments, string workingDirectory, InteractiveTerminalDimensions dimensions, CancellationToken cancellationToken) => throw new InvalidOperationException("Historical terminal runtimes cannot be restarted.");
    public Task WriteAsync(byte[] data, CancellationToken cancellationToken) => throw new InvalidOperationException("Historical terminal runtimes do not accept input.");
    public Task ResizeAsync(InteractiveTerminalDimensions dimensions, CancellationToken cancellationToken) => throw new InvalidOperationException("Historical terminal runtimes cannot be resized.");
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

// Windows adapter boundary. Native handles never leave this file.
internal sealed class WindowsConPtyTerminalRuntime : IInteractiveTerminalRuntime
{
    private IntPtr _pseudoConsole;
    private IntPtr _process;
    private IntPtr _inputHandle;
    private IntPtr _outputHandle;
    private Task? _outputTask;
    private Task? _exitTask;
    private Exception? _outputFailure;
    private InteractiveTerminalRuntimeRecord _record = new();
    public InteractiveTerminalRuntimeRecord Record => _record;
    internal bool NativeResourcesClosed => _process == IntPtr.Zero && _pseudoConsole == IntPtr.Zero && _inputHandle == IntPtr.Zero && _outputHandle == IntPtr.Zero;
    internal Exception? OutputFailure => _outputFailure;
    public event Action<byte[]>? Output;
    public event Action<InteractiveTerminalRuntimeRecord>? Changed;
    public async Task<InteractiveTerminalRuntimeRecord> StartAsync(InteractiveAgentSessionRecord session, string fileName, IReadOnlyList<string> arguments, string workingDirectory, InteractiveTerminalDimensions dimensions, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows ConPTY is unavailable on this platform.");
        CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0);
        CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0);
        try
        {
            SetHandleInformation(inputWrite, 1, 0); SetHandleInformation(outputRead, 1, 0);
            ThrowIfFailed(CreatePseudoConsole(new Coord((short)dimensions.Columns, (short)dimensions.Rows), inputRead, outputWrite, 0, out _pseudoConsole));
            _inputHandle = inputWrite;
            inputWrite = IntPtr.Zero;
            _outputHandle = outputRead;
            _outputTask = Task.Factory.StartNew(() => Copy(_outputHandle), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            outputRead = IntPtr.Zero;
            var size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            var attributes = Marshal.AllocHGlobal(size);
            try
            {
                ThrowIfFalse(InitializeProcThreadAttributeList(attributes, 1, 0, ref size));
                ThrowIfFalse(UpdateProcThreadAttribute(attributes, 0, (IntPtr)0x00020016, _pseudoConsole, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero));
                var startup = new StartupInfoEx { StartupInfo = new StartupInfo { cb = Marshal.SizeOf<StartupInfoEx>() }, lpAttributeList = attributes };
                var command = new StringBuilder(Quote(fileName) + string.Concat(arguments.Select(argument => " " + Quote(argument))));
                ThrowIfFalse(CreateProcess(null, command, IntPtr.Zero, IntPtr.Zero, false, 0x00080000, IntPtr.Zero, workingDirectory, ref startup, out var process));
                CloseHandle(inputRead);
                inputRead = IntPtr.Zero;
                CloseHandle(outputWrite);
                outputWrite = IntPtr.Zero;
                _process = process.hProcess;
                CloseHandle(process.hThread);
                var now = DateTimeOffset.UtcNow;
                _record = new InteractiveTerminalRuntimeRecord { RuntimeId = Guid.NewGuid().ToString("n"), InteractiveAgentSessionId = session.InteractiveAgentSessionId, WorkspaceId = session.WorkspaceId, ProviderSessionId = session.ProviderSessionId, ProcessId = unchecked((int)process.dwProcessId), ProcessStartedUtc = now, Status = InteractiveTerminalRuntimeStatus.Running, CreatedUtc = now, UpdatedUtc = now, LastActivityUtc = now, Dimensions = dimensions };
                Changed?.Invoke(_record);
                _exitTask = ObserveExitAsync(_process);
            }
            finally { DeleteProcThreadAttributeList(attributes); Marshal.FreeHGlobal(attributes); }
        }
        finally { if (inputRead != IntPtr.Zero) CloseHandle(inputRead); if (outputWrite != IntPtr.Zero) CloseHandle(outputWrite); if (inputWrite != IntPtr.Zero) CloseHandle(inputWrite); if (outputRead != IntPtr.Zero) CloseHandle(outputRead); }
        await Task.CompletedTask;
        return _record;
    }
    public Task WriteAsync(byte[] data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_inputHandle == IntPtr.Zero || _record.Status != InteractiveTerminalRuntimeStatus.Running) throw new InvalidOperationException("Terminal runtime is not running.");
        if (!WriteFile(_inputHandle, data, data.Length, out var written, IntPtr.Zero) || written != data.Length) ThrowIfFalse(false);
        _record = _record with { LastActivityUtc = DateTimeOffset.UtcNow };
        return Task.CompletedTask;
    }
    public Task ResizeAsync(InteractiveTerminalDimensions dimensions, CancellationToken cancellationToken) { if (_record.Status != InteractiveTerminalRuntimeStatus.Running) throw new InvalidOperationException("Terminal runtime is not running."); ThrowIfFailed(ResizePseudoConsole(_pseudoConsole, new Coord((short)dimensions.Columns, (short)dimensions.Rows))); _record = _record with { Dimensions = dimensions, LastActivityUtc = DateTimeOffset.UtcNow }; return Task.CompletedTask; }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_process == IntPtr.Zero || _record.Status is InteractiveTerminalRuntimeStatus.Exited or InteractiveTerminalRuntimeStatus.Failed) return;
        _record = _record with { Status = InteractiveTerminalRuntimeStatus.Stopping };
        Changed?.Invoke(_record);
        // The process handle belongs exclusively to this runtime. Do not use a process-name kill.
        // For docker.exe exec this proves only host-child termination; container-side provider
        // termination semantics are intentionally not claimed until a separate live smoke proves them.
        var process = _process;
        if (!TerminateProcess(process, 1) && Marshal.GetLastWin32Error() != 5) ThrowIfFalse(false);
        if (_exitTask is not null) await _exitTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }
    private void Copy(IntPtr handle)
    {
        var buffer = new byte[8192];
        try
        {
            while (ReadFile(handle, buffer, buffer.Length, out var count, IntPtr.Zero) && count > 0) Output?.Invoke(buffer[..count]);
        }
        catch (Exception exception) { _outputFailure = exception; }
    }
    private async Task ObserveExitAsync(IntPtr process)
    {
        await Task.Factory.StartNew(() => WaitForSingleObject(process, 0xffffffff), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        GetExitCodeProcess(process, out var code);
        _record = _record with { Status = code == 0 || _record.Status == InteractiveTerminalRuntimeStatus.Stopping ? InteractiveTerminalRuntimeStatus.Exited : InteractiveTerminalRuntimeStatus.Failed, ExitCode = unchecked((int)code), LastActivityUtc = DateTimeOffset.UtcNow };
        Changed?.Invoke(_record);
        if (_inputHandle != IntPtr.Zero) CloseHandle(_inputHandle);
        _inputHandle = IntPtr.Zero;
        if (_pseudoConsole != IntPtr.Zero) ClosePseudoConsole(_pseudoConsole);
        _pseudoConsole = IntPtr.Zero;
        if (_outputTask is not null)
        {
            try { await _outputTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch (TimeoutException) { }
        }
        if (_outputHandle != IntPtr.Zero) CloseHandle(_outputHandle);
        _outputHandle = IntPtr.Zero;
        CloseHandle(process);
        _process = IntPtr.Zero;
    }
    private static string Quote(string value)
    {
        if (value.Length > 0 && !value.Any(char.IsWhiteSpace) && !value.Contains('"')) return value;
        var result = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { backslashes++; continue; }
            if (character == '"') result.Append('\\', backslashes * 2 + 1).Append(character);
            else { result.Append('\\', backslashes).Append(character); }
            backslashes = 0;
        }
        return result.Append('\\', backslashes * 2).Append('"').ToString();
    }
    private static void ThrowIfFalse(bool value) { if (!value) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
    private static void ThrowIfFailed(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    [StructLayout(LayoutKind.Sequential)] private struct Coord { public short X; public short Y; public Coord(short x, short y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfo { public int cb; public IntPtr lpReserved; public IntPtr lpDesktop; public IntPtr lpTitle; public int dwX; public int dwY; public int dwXSize; public int dwYSize; public int dwXCountChars; public int dwYCountChars; public int dwFillAttribute; public int dwFlags; public short wShowWindow; public short cbReserved2; public IntPtr lpReserved2; public IntPtr hStdInput; public IntPtr hStdOutput; public IntPtr hStdError; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo StartupInfo; public IntPtr lpAttributeList; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr hProcess; public IntPtr hThread; public uint dwProcessId; public uint dwThreadId; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out IntPtr read, out IntPtr write, IntPtr attributes, int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
    [DllImport("kernel32.dll")] private static extern int CreatePseudoConsole(Coord size, IntPtr input, IntPtr output, uint flags, out IntPtr console);
    [DllImport("kernel32.dll")] private static extern int ResizePseudoConsole(IntPtr console, Coord size);
    [DllImport("kernel32.dll")] private static extern void ClosePseudoConsole(IntPtr console);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcess(string? app, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(IntPtr handle, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(IntPtr handle, byte[] buffer, int bytesToRead, out int bytesRead, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(IntPtr handle, byte[] buffer, int bytesToWrite, out int bytesWritten, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
}

internal sealed class UnsupportedTerminalRuntime : IInteractiveTerminalRuntime
{
    public InteractiveTerminalRuntimeRecord Record => new() { Status = InteractiveTerminalRuntimeStatus.Unavailable };
    public event Action<byte[]>? Output { add { } remove { } }
    public event Action<InteractiveTerminalRuntimeRecord>? Changed { add { } remove { } }
    public Task<InteractiveTerminalRuntimeRecord> StartAsync(InteractiveAgentSessionRecord session, string fileName, IReadOnlyList<string> arguments, string workingDirectory, InteractiveTerminalDimensions dimensions, CancellationToken cancellationToken) => throw new PlatformNotSupportedException();
    public Task WriteAsync(byte[] data, CancellationToken cancellationToken) => throw new PlatformNotSupportedException();
    public Task ResizeAsync(InteractiveTerminalDimensions dimensions, CancellationToken cancellationToken) => throw new PlatformNotSupportedException();
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

// Deterministic test adapter. It deliberately treats all terminal traffic as bytes.
internal sealed class FakeInteractiveTerminalRuntime : IInteractiveTerminalRuntime
{
    private InteractiveTerminalRuntimeRecord _record = new();
    public List<byte[]> ReceivedInput { get; } = [];
    public List<InteractiveTerminalDimensions> ResizeHistory { get; } = [];
    public int StopCount { get; private set; }
    public InteractiveTerminalRuntimeRecord Record => _record;
    public event Action<byte[]>? Output;
    public event Action<InteractiveTerminalRuntimeRecord>? Changed;

    public Task<InteractiveTerminalRuntimeRecord> StartAsync(InteractiveAgentSessionRecord session, string fileName, IReadOnlyList<string> arguments, string workingDirectory, InteractiveTerminalDimensions dimensions, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        _record = new InteractiveTerminalRuntimeRecord { ProcessId = 4242, ProcessStartedUtc = now, InteractiveAgentSessionId = session.InteractiveAgentSessionId, WorkspaceId = session.WorkspaceId, ProviderSessionId = session.ProviderSessionId, Status = InteractiveTerminalRuntimeStatus.Running, CreatedUtc = now, UpdatedUtc = now, LastActivityUtc = now, Dimensions = dimensions };
        return Task.FromResult(_record);
    }
    public Task WriteAsync(byte[] data, CancellationToken cancellationToken) { ReceivedInput.Add(data.ToArray()); return Task.CompletedTask; }
    public Task ResizeAsync(InteractiveTerminalDimensions dimensions, CancellationToken cancellationToken) { ResizeHistory.Add(dimensions); _record = _record with { Dimensions = dimensions }; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken cancellationToken) { StopCount++; _record = _record with { Status = InteractiveTerminalRuntimeStatus.Exited, ExitCode = 0 }; Changed?.Invoke(_record); return Task.CompletedTask; }
    public void EmitOutput(byte[] data) => Output?.Invoke(data.ToArray());
    public void Exit(int exitCode = 0) => _record = _record with { Status = exitCode == 0 ? InteractiveTerminalRuntimeStatus.Exited : InteractiveTerminalRuntimeStatus.Failed, ExitCode = exitCode };
    public void Fail() => Exit(1);
}
