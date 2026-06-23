using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia;

public partial class CreateWorkspaceWindow : Window
{
    private readonly IReadOnlyList<TemplateManifest> _templates;
    private readonly ComboBox _templateComboBox;
    private readonly TextBox _workspaceNameTextBox;
    private readonly TextBox _workspacePathTextBox;
    private readonly TextBlock _templateSummaryTextBlock;
    private readonly TextBlock _validationMessageTextBlock;
    private readonly TextBlock _statusTextBlock;

    public CreateWorkspaceWindow()
        : this(Array.Empty<TemplateManifest>())
    {
    }

    public CreateWorkspaceWindow(IReadOnlyList<TemplateManifest> templates)
    {
        InitializeComponent();
        _templates = templates;
        _templateComboBox = this.FindControl<ComboBox>("TemplateComboBox") ?? throw new InvalidOperationException("TemplateComboBox was not found.");
        _workspaceNameTextBox = this.FindControl<TextBox>("WorkspaceNameTextBox") ?? throw new InvalidOperationException("WorkspaceNameTextBox was not found.");
        _workspacePathTextBox = this.FindControl<TextBox>("WorkspacePathTextBox") ?? throw new InvalidOperationException("WorkspacePathTextBox was not found.");
        _templateSummaryTextBlock = this.FindControl<TextBlock>("TemplateSummaryTextBlock") ?? throw new InvalidOperationException("TemplateSummaryTextBlock was not found.");
        _validationMessageTextBlock = this.FindControl<TextBlock>("ValidationMessageTextBlock") ?? throw new InvalidOperationException("ValidationMessageTextBlock was not found.");
        _statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock") ?? throw new InvalidOperationException("StatusTextBlock was not found.");
        _templateComboBox.ItemsSource = templates;
        _templateComboBox.SelectedItem = templates.FirstOrDefault();
        _templateComboBox.SelectionChanged += (_, _) => UpdateTemplateSummary();
        UpdateTemplateSummary();
    }

    public CreateWorkspaceDraft? Result { get; private set; }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void BrowseWorkspaceFolder(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
        {
            return;
        }

        var folder = (await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select workspace folder", AllowMultiple = false })).FirstOrDefault();
        if (folder is not null)
        {
            _workspacePathTextBox.Text = folder.TryGetLocalPath();
        }
    }

    private void CancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void CreateClicked(object? sender, RoutedEventArgs e)
    {
        var template = _templateComboBox.SelectedItem as TemplateManifest;
        var name = _workspaceNameTextBox.Text?.Trim() ?? string.Empty;
        var path = _workspacePathTextBox.Text?.Trim() ?? string.Empty;

        if (template is null)
        {
            _validationMessageTextBlock.Text = "Select a template.";
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            _validationMessageTextBlock.Text = "Enter a workspace name.";
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            _validationMessageTextBlock.Text = "Choose a destination folder.";
            return;
        }

        Result = new CreateWorkspaceDraft { WorkspaceName = name, WorkspaceRootPath = path, Template = template };
        Close(Result);
    }

    private void UpdateTemplateSummary()
    {
        if (_templateComboBox.SelectedItem is not TemplateManifest template)
        {
            _templateSummaryTextBlock.Text = "No template selected.";
            _statusTextBlock.Text = string.Empty;
            return;
        }

        _templateSummaryTextBlock.Text = $"{template.Description}\nFeatures: {(template.Features.Count == 0 ? "core" : string.Join(", ", template.Features))}\nServices: {(template.Services.Count == 0 ? "none" : string.Join(", ", template.Services))}";
        _statusTextBlock.Text = string.IsNullOrWhiteSpace(template.WorkspaceImage) ? "Uses the default Ubuntu workspace image." : $"Workspace image: {template.WorkspaceImage}";
    }
}
