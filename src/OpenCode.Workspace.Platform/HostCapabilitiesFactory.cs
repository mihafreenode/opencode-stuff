namespace OpenCode.Workspace.Platform;

public sealed class HostCapabilitiesFactory
{
    private readonly Func<IHostCapabilities> _windowsFactory;
    private readonly Func<IHostCapabilities> _linuxFactory;
    private readonly Func<IHostCapabilities> _macFactory;
    private readonly IPlatformRuntime _platformRuntime;

    public HostCapabilitiesFactory(Func<IHostCapabilities> windowsFactory, Func<IHostCapabilities> linuxFactory, Func<IHostCapabilities> macFactory, IPlatformRuntime? platformRuntime = null)
    {
        _windowsFactory = windowsFactory;
        _linuxFactory = linuxFactory;
        _macFactory = macFactory;
        _platformRuntime = platformRuntime ?? new RuntimeInformationPlatformRuntime();
    }

    public IHostCapabilities CreateForCurrentPlatform()
    {
        if (_platformRuntime.IsWindows)
        {
            return _windowsFactory();
        }

        if (_platformRuntime.IsMacOS)
        {
            return _macFactory();
        }

        if (_platformRuntime.IsLinux)
        {
            return _linuxFactory();
        }

        return new UnsupportedHostCapabilities(_platformRuntime);
    }

    private sealed class UnsupportedHostCapabilities : IHostCapabilities
    {
        private readonly IPlatformRuntime _platformRuntime;

        public UnsupportedHostCapabilities(IPlatformRuntime platformRuntime)
        {
            _platformRuntime = platformRuntime;
        }

        public PlatformKind Platform => PlatformKind.Unknown;

        public Task<HostCapabilityReport> DetectAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(HostCapabilityReport.Empty(Platform, _platformRuntime.Architecture));
    }
}
