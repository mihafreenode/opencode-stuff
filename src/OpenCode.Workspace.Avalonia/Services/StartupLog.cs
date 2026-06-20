using System.IO;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class StartupLog
{
    private readonly string _logPath;
    private readonly object _gate = new();

    public StartupLog(string appDataRoot)
    {
        Directory.CreateDirectory(appDataRoot);
        _logPath = Path.Combine(appDataRoot, "avalonia-startup.log");
    }

    public void Write(string message)
    {
        lock (_gate)
        {
            File.AppendAllLines(_logPath, [$"[{DateTimeOffset.Now:O}] {message}"]);
        }
    }

    public void WriteException(string stage, Exception exception)
        => Write($"{stage}: {exception}");
}
