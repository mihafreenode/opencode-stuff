using Avalonia.Controls;

namespace OpenCode.Workspace.Avalonia.Services;

public sealed class AvaloniaClipboardService : IClipboardService
{
    private readonly Window _window;

    public AvaloniaClipboardService(Window window)
    {
        _window = window;
    }

    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_window.Clipboard is not null)
        {
            await _window.Clipboard.SetTextAsync(text);
        }
    }
}
