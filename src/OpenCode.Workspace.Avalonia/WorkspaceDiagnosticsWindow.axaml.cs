using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Avalonia.ViewModels;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia;

public partial class WorkspaceDiagnosticsWindow : Window
{
    public WorkspaceDiagnosticsWindow()
        : this(new WorkspaceDiagnosticsSession())
    {
    }

    public WorkspaceDiagnosticsWindow(WorkspaceDiagnosticsSession session)
        : this(session, null)
    {
    }

    public WorkspaceDiagnosticsWindow(WorkspaceDiagnosticsSession session, IClipboardService? clipboardService)
    {
        InitializeComponent();
        AppWindowIcons.Apply(this);
        DataContext = new WorkspaceDiagnosticsWindowViewModel(
            session,
            clipboardService ?? new AvaloniaClipboardService(this),
            ShowExportDestinationDialogAsync,
            new WorkspaceDiagnosticsBundleExportService().ExportAsync);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async Task<string?> ShowExportDestinationDialogAsync(string suggestedFileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (StorageProvider is null)
        {
            return null;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export diagnostics bundle",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "zip",
            FileTypeChoices =
            [
                new FilePickerFileType("Zip archive")
                {
                    Patterns = ["*.zip"],
                    MimeTypes = ["application/zip"],
                },
            ],
        });

        return file?.TryGetLocalPath();
    }

    private void CloseClicked(object? sender, RoutedEventArgs e) => Close();
}
