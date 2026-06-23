using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Core.Workspaces;

namespace OpenCode.Workspace.Avalonia;

public partial class OpenExistingRepositoryWindow : Window
{
    private readonly Func<string, string, CancellationToken, Task<ExistingGitCheckoutPlan>> _inspectRepositoryAsync;
    private readonly Func<string, string, CancellationToken, Task<GitBranchValidationResult>> _validateBranchAsync;
    private ExistingGitCheckoutPlan? _latestPlan;
    private readonly TextBox _repositoryPathTextBox;
    private readonly TextBox _workspaceNameTextBox;
    private readonly ComboBox _branchModeComboBox;
    private readonly TextBox _namedBranchTextBox;
    private readonly CheckBox _reuseExistingBranchCheckBox;
    private readonly TextBlock _inspectionSummaryTextBlock;
    private readonly TextBlock _validationMessageTextBlock;
    private readonly TextBlock _statusTextBlock;

    public OpenExistingRepositoryWindow()
        : this((_, _, _) => Task.FromResult(new ExistingGitCheckoutPlan
        {
            RepositoryPath = string.Empty,
            WorkspaceName = string.Empty,
            Repository = new GitRepositoryInspection(),
            DiscoveryResult = new WorkspaceDiscoveryResult(),
        }), (_, _, _) => Task.FromResult(new GitBranchValidationResult(true, string.Empty, false)))
    {
    }

    public OpenExistingRepositoryWindow(
        Func<string, string, CancellationToken, Task<ExistingGitCheckoutPlan>> inspectRepositoryAsync,
        Func<string, string, CancellationToken, Task<GitBranchValidationResult>> validateBranchAsync)
    {
        InitializeComponent();
        _inspectRepositoryAsync = inspectRepositoryAsync;
        _validateBranchAsync = validateBranchAsync;
        _repositoryPathTextBox = this.FindControl<TextBox>("RepositoryPathTextBox") ?? throw new InvalidOperationException("RepositoryPathTextBox was not found.");
        _workspaceNameTextBox = this.FindControl<TextBox>("WorkspaceNameTextBox") ?? throw new InvalidOperationException("WorkspaceNameTextBox was not found.");
        _branchModeComboBox = this.FindControl<ComboBox>("BranchModeComboBox") ?? throw new InvalidOperationException("BranchModeComboBox was not found.");
        _namedBranchTextBox = this.FindControl<TextBox>("NamedBranchTextBox") ?? throw new InvalidOperationException("NamedBranchTextBox was not found.");
        _reuseExistingBranchCheckBox = this.FindControl<CheckBox>("ReuseExistingBranchCheckBox") ?? throw new InvalidOperationException("ReuseExistingBranchCheckBox was not found.");
        _inspectionSummaryTextBlock = this.FindControl<TextBlock>("InspectionSummaryTextBlock") ?? throw new InvalidOperationException("InspectionSummaryTextBlock was not found.");
        _validationMessageTextBlock = this.FindControl<TextBlock>("ValidationMessageTextBlock") ?? throw new InvalidOperationException("ValidationMessageTextBlock was not found.");
        _statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock") ?? throw new InvalidOperationException("StatusTextBlock was not found.");
        _branchModeComboBox.ItemsSource = Enum.GetValues<ExistingGitCheckoutBranchMode>();
        _branchModeComboBox.SelectedItem = ExistingGitCheckoutBranchMode.CreateTemporaryWorkspaceBranch;
        _inspectionSummaryTextBlock.Text = "Inspect a repository to review its current branch and workspace configuration.";
    }

    public ExistingRepositoryImportDraft? Result { get; private set; }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void BrowseRepositoryFolder(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null)
        {
            return;
        }

        var folder = (await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select repository folder", AllowMultiple = false })).FirstOrDefault();
        if (folder is not null)
        {
            _repositoryPathTextBox.Text = folder.TryGetLocalPath();
        }
    }

    private async void InspectRepository(object? sender, RoutedEventArgs e)
    {
        _validationMessageTextBlock.Text = string.Empty;
        _statusTextBlock.Text = "Inspecting repository...";
        try
        {
            var repositoryPath = _repositoryPathTextBox.Text?.Trim() ?? string.Empty;
            var workspaceName = _workspaceNameTextBox.Text?.Trim() ?? string.Empty;
            _latestPlan = await _inspectRepositoryAsync(repositoryPath, workspaceName, CancellationToken.None);
            _workspaceNameTextBox.Text = _latestPlan.WorkspaceName;
            _inspectionSummaryTextBlock.Text = $"Branch: {_latestPlan.Repository.CurrentBranch}\nStatus: {_latestPlan.Repository.StatusSummary}\nConfiguration: {_latestPlan.DiscoveryResult.Status}\nPath: {_latestPlan.DiscoveryResult.ConfigurationPath ?? "No workspace.yaml found"}";
            _statusTextBlock.Text = _latestPlan.DiscoveryResult.Status == WorkspaceDiscoveryStatus.Invalid
                ? _latestPlan.DiscoveryResult.ErrorMessage ?? "Configuration could not be loaded."
                : "Repository inspection completed.";
        }
        catch (Exception exception)
        {
            _latestPlan = null;
            _validationMessageTextBlock.Text = exception.Message;
            _statusTextBlock.Text = "Repository inspection failed.";
        }
    }

    private void CancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private async void ImportClicked(object? sender, RoutedEventArgs e)
    {
        _validationMessageTextBlock.Text = string.Empty;
        var repositoryPath = _repositoryPathTextBox.Text?.Trim() ?? string.Empty;
        var workspaceName = _workspaceNameTextBox.Text?.Trim() ?? string.Empty;
        var branchMode = _branchModeComboBox.SelectedItem is ExistingGitCheckoutBranchMode mode ? mode : ExistingGitCheckoutBranchMode.CreateTemporaryWorkspaceBranch;
        var namedBranch = _namedBranchTextBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            _validationMessageTextBlock.Text = "Choose a repository folder.";
            return;
        }

        if (string.IsNullOrWhiteSpace(workspaceName))
        {
            _validationMessageTextBlock.Text = "Enter a workspace name.";
            return;
        }

        if (branchMode == ExistingGitCheckoutBranchMode.CreateNamedFeatureBranch)
        {
            var validation = await _validateBranchAsync(repositoryPath, namedBranch, CancellationToken.None);
            if (!validation.IsValid)
            {
                _validationMessageTextBlock.Text = validation.Message;
                return;
            }
        }

        Result = new ExistingRepositoryImportDraft
        {
            RepositoryPath = repositoryPath,
            WorkspaceName = workspaceName,
            BranchMode = branchMode,
            NamedBranch = namedBranch,
            ReuseExistingNamedBranch = _reuseExistingBranchCheckBox.IsChecked == true,
        };
        Close(Result);
    }
}
