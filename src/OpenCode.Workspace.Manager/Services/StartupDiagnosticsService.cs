using System.IO;

namespace OpenCode.Workspace.Manager.Services;

public sealed class StartupDiagnosticsService
{
    private readonly string _logFilePath;
    private readonly object _sync = new();

    public StartupDiagnosticsService(string applicationDataRoot)
    {
        Directory.CreateDirectory(applicationDataRoot);
        _logFilePath = Path.Combine(applicationDataRoot, "startup-diagnostics.log");
    }

    public void Log(string message)
    {
        var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
        lock (_sync)
        {
            File.AppendAllText(_logFilePath, line);
        }
    }
}
