using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace OpenCode.Workspace.Platform.Windows;

public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;

    public SingleInstanceService(string mutexName)
    {
        _mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        IsPrimaryInstance = createdNew;
    }

    public bool IsPrimaryInstance { get; }

    public bool TryActivateExistingInstance(Process currentProcess)
    {
        var currentPath = currentProcess.MainModule?.FileName;
        foreach (var process in Process.GetProcessesByName(currentProcess.ProcessName))
        {
            if (process.Id == currentProcess.Id)
            {
                continue;
            }

            if (!string.Equals(process.MainModule?.FileName, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (process.MainWindowHandle == IntPtr.Zero)
            {
                continue;
            }

            NativeMethods.ShowWindow(process.MainWindowHandle, 9);
            return NativeMethods.SetForegroundWindow(process.MainWindowHandle);
        }

        return false;
    }

    public void Dispose()
    {
        if (IsPrimaryInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
