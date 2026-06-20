namespace OpenCode.Workspace.Avalonia.Services;

public interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}
