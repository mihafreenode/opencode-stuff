using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Core.Models;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Avalonia;

public partial class CreateWorkspaceWindow : Window
{
    private readonly record struct ValidationState(ValidationStateKind Kind, string Message)
    {
        public bool CanCreate => Kind == ValidationStateKind.Success;
    }

    private enum ValidationStateKind
    {
        Success,
        Warning,
        Error,
    }

    private readonly IReadOnlyList<TemplateManifest> _templates;
    private readonly ComboBox _templateComboBox;
    private readonly TextBox _workspaceNameTextBox;
    private readonly TextBox _workspacePathTextBox;
    private readonly TextBlock _workspaceFolderPreviewTextBlock;
    private readonly TextBlock _templateDisplayNameTextBlock;
    private readonly TextBlock _templateDescriptionTextBlock;
    private readonly TextBlock _templateFeaturesTextBlock;
    private readonly TextBlock _templateServicesTextBlock;
    private readonly TextBlock _templateImageTextBlock;
    private readonly TextBlock _templateDocumentationTextBlock;
    private readonly TextBlock _templateReviewTextBlock;
    private readonly TextBlock _validationMessageTextBlock;
    private readonly TextBlock _statusTextBlock;
    private readonly Button _createButton;

    public CreateWorkspaceWindow()
        : this(Array.Empty<TemplateManifest>())
    {
    }

    public CreateWorkspaceWindow(IReadOnlyList<TemplateManifest> templates)
    {
        InitializeComponent();
        AppWindowIcons.Apply(this);
        _templates = templates;
        _templateComboBox = this.FindControl<ComboBox>("TemplateComboBox") ?? throw new InvalidOperationException("TemplateComboBox was not found.");
        _workspaceNameTextBox = this.FindControl<TextBox>("WorkspaceNameTextBox") ?? throw new InvalidOperationException("WorkspaceNameTextBox was not found.");
        _workspacePathTextBox = this.FindControl<TextBox>("WorkspacePathTextBox") ?? throw new InvalidOperationException("WorkspacePathTextBox was not found.");
        _workspaceFolderPreviewTextBlock = this.FindControl<TextBlock>("WorkspaceFolderPreviewTextBlock") ?? throw new InvalidOperationException("WorkspaceFolderPreviewTextBlock was not found.");
        _templateDisplayNameTextBlock = this.FindControl<TextBlock>("TemplateDisplayNameTextBlock") ?? throw new InvalidOperationException("TemplateDisplayNameTextBlock was not found.");
        _templateDescriptionTextBlock = this.FindControl<TextBlock>("TemplateDescriptionTextBlock") ?? throw new InvalidOperationException("TemplateDescriptionTextBlock was not found.");
        _templateFeaturesTextBlock = this.FindControl<TextBlock>("TemplateFeaturesTextBlock") ?? throw new InvalidOperationException("TemplateFeaturesTextBlock was not found.");
        _templateServicesTextBlock = this.FindControl<TextBlock>("TemplateServicesTextBlock") ?? throw new InvalidOperationException("TemplateServicesTextBlock was not found.");
        _templateImageTextBlock = this.FindControl<TextBlock>("TemplateImageTextBlock") ?? throw new InvalidOperationException("TemplateImageTextBlock was not found.");
        _templateDocumentationTextBlock = this.FindControl<TextBlock>("TemplateDocumentationTextBlock") ?? throw new InvalidOperationException("TemplateDocumentationTextBlock was not found.");
        _templateReviewTextBlock = this.FindControl<TextBlock>("TemplateReviewTextBlock") ?? throw new InvalidOperationException("TemplateReviewTextBlock was not found.");
        _validationMessageTextBlock = this.FindControl<TextBlock>("ValidationMessageTextBlock") ?? throw new InvalidOperationException("ValidationMessageTextBlock was not found.");
        _statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock") ?? throw new InvalidOperationException("StatusTextBlock was not found.");
        _createButton = this.FindControl<Button>("CreateButton") ?? throw new InvalidOperationException("CreateButton was not found.");
        _templateComboBox.ItemsSource = templates;
        _templateComboBox.SelectedItem = templates.FirstOrDefault();
        _templateComboBox.SelectionChanged += (_, _) => UpdateDialogState();
        _workspaceNameTextBox.TextChanged += (_, _) => UpdateDialogState();
        _workspacePathTextBox.TextChanged += (_, _) => UpdateDialogState();
        UpdateDialogState();
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

        IStorageFolder? startLocation = null;
        var currentParentPath = _workspacePathTextBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(currentParentPath))
        {
            startLocation = await StorageProvider.TryGetFolderFromPathAsync(currentParentPath);
        }

        var folder = (await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select parent folder",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation,
        })).FirstOrDefault();

        if (folder is not null)
        {
            _workspacePathTextBox.Text = folder.TryGetLocalPath();
            UpdateDialogState();
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
        var parentPath = _workspacePathTextBox.Text?.Trim() ?? string.Empty;

        var validationState = BuildValidationState(template, name, parentPath);
        if (!validationState.CanCreate)
        {
            ApplyValidationState(validationState);
            UpdateCreateButtonState();
            return;
        }

        var workspaceRootPath = BuildWorkspaceRootPath(name, parentPath);
        Result = new CreateWorkspaceDraft { WorkspaceName = name, WorkspaceRootPath = workspaceRootPath, Template = template! };
        Close(Result);
    }

    private void UpdateDialogState()
    {
        if (_templateComboBox.SelectedItem is not TemplateManifest template)
        {
            _templateDisplayNameTextBlock.Text = "No template selected";
            _templateDescriptionTextBlock.Text = "Choose a template to review what will be created.";
            _templateFeaturesTextBlock.Text = "No template selected.";
            _templateServicesTextBlock.Text = "No template selected.";
            _templateImageTextBlock.Text = "Uses the default Ubuntu workspace image.";
            _templateDocumentationTextBlock.Text = "Template documentation will appear here when a template is selected.";
            _templateReviewTextBlock.Text = "Select a template, enter a workspace name, and choose a parent folder.";
            _workspaceFolderPreviewTextBlock.Text = "Choose a parent folder to preview the workspace folder.";
            _statusTextBlock.Text = "Choose a template to start.";
            ApplyValidationState(new ValidationState(ValidationStateKind.Error, "Select a template."));
            UpdateCreateButtonState();
            return;
        }

        var name = _workspaceNameTextBox.Text?.Trim() ?? string.Empty;
        var parentPath = _workspacePathTextBox.Text?.Trim() ?? string.Empty;
        var validationState = BuildValidationState(template, name, parentPath);

        _templateDisplayNameTextBlock.Text = string.IsNullOrWhiteSpace(template.DisplayName) ? template.Id : template.DisplayName;
        _templateDescriptionTextBlock.Text = string.IsNullOrWhiteSpace(template.Description) ? "No template description is available." : template.Description;
        _templateFeaturesTextBlock.Text = BuildChecklistText(template.Features, "core");
        _templateServicesTextBlock.Text = BuildChecklistText(template.Services, "none");
        _templateImageTextBlock.Text = string.IsNullOrWhiteSpace(template.WorkspaceImage) ? "Uses the default Ubuntu workspace image." : template.WorkspaceImage!;
        _templateDocumentationTextBlock.Text = "View template documentation from the Templates page.";
        _templateReviewTextBlock.Text = BuildTemplateReviewText(template, name, parentPath);
        _workspaceFolderPreviewTextBlock.Text = BuildWorkspaceFolderPreview(name, parentPath);
        ApplyValidationState(validationState);
        _statusTextBlock.Text = validationState.CanCreate
            ? "Review what will be created, then create the workspace."
            : "Resolve the validation message before creating the workspace.";
        UpdateCreateButtonState();
    }

    private void UpdateCreateButtonState()
    {
        var template = _templateComboBox.SelectedItem as TemplateManifest;
        var name = _workspaceNameTextBox.Text?.Trim() ?? string.Empty;
        var parentPath = _workspacePathTextBox.Text?.Trim() ?? string.Empty;
        _createButton.IsEnabled = BuildValidationState(template, name, parentPath).CanCreate;
    }

    private static string BuildChecklistText(IReadOnlyList<string> values, string fallback)
        => values.Count == 0
            ? $"- {fallback}"
            : string.Join(Environment.NewLine, values.Select(value => $"✓ {value}"));

    private void ApplyValidationState(ValidationState state)
    {
        _validationMessageTextBlock.Text = state.Message;
        _validationMessageTextBlock.Foreground = state.Kind switch
        {
            ValidationStateKind.Success => GetValidationBrush("SuccessBrush"),
            ValidationStateKind.Warning => GetValidationBrush("WarningBrush"),
            _ => GetValidationBrush("DangerBrush"),
        };
    }

    private IBrush GetValidationBrush(string resourceKey)
        => TryGetResource(resourceKey, ActualThemeVariant, out var brush) && brush is IBrush resolvedBrush
            ? resolvedBrush
            : Brushes.Gray;

    private static string BuildWorkspaceFolderPreview(string workspaceName, string parentFolder)
    {
        if (string.IsNullOrWhiteSpace(parentFolder))
        {
            return "Choose a parent folder to preview the workspace folder.";
        }

        if (string.IsNullOrWhiteSpace(workspaceName))
        {
            return "Enter a workspace name to preview the workspace folder.";
        }

        if (parentFolder.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return "Parent folder path is not valid.";
        }

        var workspaceRootPath = BuildWorkspaceRootPath(workspaceName, parentFolder);
        return string.IsNullOrWhiteSpace(workspaceRootPath)
            ? "Workspace name must include letters or numbers to build the workspace folder."
            : workspaceRootPath;
    }

    private static string BuildTemplateReviewText(TemplateManifest template, string workspaceName, string parentFolder)
    {
        var workspaceFolder = BuildWorkspaceFolderPreview(workspaceName, parentFolder);
        var featureCount = template.Features.Count == 0 ? 1 : template.Features.Count;
        var serviceCount = template.Services.Count;
        return string.Join(Environment.NewLine, new[]
        {
            $"Template id: {template.Id}",
            $"Workspace folder: {workspaceFolder}",
            $"Features: {featureCount}",
            $"Services: {serviceCount}",
            string.IsNullOrWhiteSpace(template.WorkspaceImage) ? "Runtime: default Ubuntu workspace image" : $"Runtime: {template.WorkspaceImage}",
        });
    }

    private static ValidationState BuildValidationState(TemplateManifest? template, string workspaceName, string parentFolder)
    {
        if (template is null)
        {
            return new ValidationState(ValidationStateKind.Error, "Select a template.");
        }

        if (string.IsNullOrWhiteSpace(workspaceName))
        {
            return new ValidationState(ValidationStateKind.Error, "Enter a workspace name.");
        }

        if (workspaceName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return new ValidationState(ValidationStateKind.Error, "Workspace name contains invalid characters.");
        }

        if (string.IsNullOrWhiteSpace(parentFolder))
        {
            return new ValidationState(ValidationStateKind.Error, "Choose a parent folder.");
        }

        if (parentFolder.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return new ValidationState(ValidationStateKind.Error, "Parent folder contains invalid path characters.");
        }

        var workspaceRootPath = BuildWorkspaceRootPath(workspaceName, parentFolder);
        if (string.IsNullOrWhiteSpace(workspaceRootPath))
        {
            return new ValidationState(ValidationStateKind.Error, "Workspace name must include letters or numbers.");
        }

        if (!Directory.Exists(parentFolder))
        {
            return new ValidationState(ValidationStateKind.Error, "❌ Parent folder does not exist.");
        }

        if (!IsDirectoryWritable(parentFolder))
        {
            return new ValidationState(ValidationStateKind.Error, "❌ Parent folder is not writable.");
        }

        if (Directory.Exists(workspaceRootPath))
        {
            return new ValidationState(ValidationStateKind.Warning, "⚠ Workspace folder already exists.");
        }

        return new ValidationState(ValidationStateKind.Success, "✓ Workspace will be created here.");
    }

    private static string BuildWorkspaceRootPath(string workspaceName, string parentFolder)
    {
        if (string.IsNullOrWhiteSpace(workspaceName) || string.IsNullOrWhiteSpace(parentFolder))
        {
            return string.Empty;
        }

        var suggestedFolderName = WorkspacePathBuilder.Slugify(workspaceName);
        if (string.IsNullOrWhiteSpace(suggestedFolderName))
        {
            return string.Empty;
        }

        try
        {
            return Path.Combine(Path.GetFullPath(parentFolder), suggestedFolderName);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    private static bool IsDirectoryWritable(string directoryPath)
    {
        var probePath = Path.Combine(directoryPath, $".opencode-write-test-{Guid.NewGuid():N}.tmp");

        try
        {
            using var stream = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
