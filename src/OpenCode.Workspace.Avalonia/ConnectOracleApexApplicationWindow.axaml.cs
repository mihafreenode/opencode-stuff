using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using OpenCode.Workspace.Avalonia.Services;
using OpenCode.Workspace.Core.Models;

namespace OpenCode.Workspace.Avalonia;

public partial class ConnectOracleApexApplicationWindow : Window
{
    private readonly Func<ConnectOracleApexApplicationDraft, CancellationToken, Task<OracleApexApplicationDiscoveryResult>> _discoverApplicationsAsync;
    private readonly TextBox _environmentNameTextBox;
    private readonly TextBox _workspaceNameTextBox;
    private readonly TextBox _parsingSchemaTextBox;
    private readonly TextBox _sqlclProfileTextBox;
    private readonly TextBox _sourcePathTextBox;
    private readonly TextBlock _validationMessageTextBlock;
    private readonly TextBlock _statusTextBlock;
    private readonly Button _connectButton;
    private readonly ListBox _applicationsListBox;
    private OracleApexApplicationDiscoveryResult? _lastDiscovery;

    public ConnectOracleApexApplicationWindow()
        : this((draft, _) => Task.FromResult(new OracleApexApplicationDiscoveryResult
        {
            EnvironmentName = draft.EnvironmentName,
            WorkspaceName = draft.WorkspaceName,
            ParsingSchema = draft.ParsingSchema,
            SqlclProfile = draft.SqlclProfile,
            SourcePath = draft.SourcePath,
        }), new ConnectOracleApexApplicationDraft())
    {
    }

    public ConnectOracleApexApplicationWindow(Func<ConnectOracleApexApplicationDraft, CancellationToken, Task<OracleApexApplicationDiscoveryResult>> discoverApplicationsAsync, ConnectOracleApexApplicationDraft initialDraft)
    {
        InitializeComponent();
        AppWindowIcons.Apply(this);
        _discoverApplicationsAsync = discoverApplicationsAsync;
        _environmentNameTextBox = this.FindControl<TextBox>("EnvironmentNameTextBox") ?? throw new InvalidOperationException("EnvironmentNameTextBox was not found.");
        _workspaceNameTextBox = this.FindControl<TextBox>("WorkspaceNameTextBox") ?? throw new InvalidOperationException("WorkspaceNameTextBox was not found.");
        _parsingSchemaTextBox = this.FindControl<TextBox>("ParsingSchemaTextBox") ?? throw new InvalidOperationException("ParsingSchemaTextBox was not found.");
        _sqlclProfileTextBox = this.FindControl<TextBox>("SqlclProfileTextBox") ?? throw new InvalidOperationException("SqlclProfileTextBox was not found.");
        _sourcePathTextBox = this.FindControl<TextBox>("SourcePathTextBox") ?? throw new InvalidOperationException("SourcePathTextBox was not found.");
        _validationMessageTextBlock = this.FindControl<TextBlock>("ValidationMessageTextBlock") ?? throw new InvalidOperationException("ValidationMessageTextBlock was not found.");
        _statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock") ?? throw new InvalidOperationException("StatusTextBlock was not found.");
        _connectButton = this.FindControl<Button>("ConnectButton") ?? throw new InvalidOperationException("ConnectButton was not found.");
        _applicationsListBox = this.FindControl<ListBox>("ApplicationsListBox") ?? throw new InvalidOperationException("ApplicationsListBox was not found.");
        _environmentNameTextBox.Text = initialDraft.EnvironmentName;
        _workspaceNameTextBox.Text = initialDraft.WorkspaceName;
        _parsingSchemaTextBox.Text = initialDraft.ParsingSchema;
        _sqlclProfileTextBox.Text = initialDraft.SqlclProfile;
        _sourcePathTextBox.Text = initialDraft.SourcePath;
        UpdateConnectButtonState();
    }

    public ConnectOracleApexApplicationDraft? Result { get; private set; }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void LoadApplicationsClicked(object? sender, RoutedEventArgs e)
    {
        _validationMessageTextBlock.Text = string.Empty;
        _statusTextBlock.Text = "Loading Oracle APEX applications...";

        try
        {
            var draft = BuildDraft();
            _lastDiscovery = await _discoverApplicationsAsync(draft, CancellationToken.None);
            _applicationsListBox.ItemsSource = _lastDiscovery.Applications.Select(item => new OracleApexApplicationListItem(item)).ToList();
            _statusTextBlock.Text = _lastDiscovery.Summary;
            UpdateConnectButtonState();
        }
        catch (Exception exception)
        {
            _lastDiscovery = null;
            _applicationsListBox.ItemsSource = null;
            _validationMessageTextBlock.Text = exception.Message;
            _validationMessageTextBlock.Foreground = GetValidationBrush("DangerBrush");
            _statusTextBlock.Text = "Application discovery failed.";
            UpdateConnectButtonState();
        }
    }

    private void ApplicationsSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => UpdateConnectButtonState();

    private void CancelClicked(object? sender, RoutedEventArgs e)
        => Close(null);

    private void ConnectClicked(object? sender, RoutedEventArgs e)
    {
        if (_applicationsListBox.SelectedItem is not OracleApexApplicationListItem selected)
        {
            _validationMessageTextBlock.Text = "Choose an application to connect.";
            _validationMessageTextBlock.Foreground = GetValidationBrush("DangerBrush");
            return;
        }

        var draft = BuildDraft();
        Result = new ConnectOracleApexApplicationDraft
        {
            EnvironmentName = draft.EnvironmentName,
            WorkspaceName = draft.WorkspaceName,
            ParsingSchema = draft.ParsingSchema,
            SqlclProfile = draft.SqlclProfile,
            SourcePath = draft.SourcePath,
            ApplicationId = selected.Application.ApplicationId,
            ApplicationName = selected.Application.ApplicationName,
            Alias = selected.Application.Alias,
        };
        Close(Result);
    }

    private ConnectOracleApexApplicationDraft BuildDraft()
        => new()
        {
            EnvironmentName = string.IsNullOrWhiteSpace(_environmentNameTextBox.Text) ? "dev" : _environmentNameTextBox.Text.Trim(),
            WorkspaceName = string.IsNullOrWhiteSpace(_workspaceNameTextBox.Text) ? "TEST" : _workspaceNameTextBox.Text.Trim(),
            ParsingSchema = string.IsNullOrWhiteSpace(_parsingSchemaTextBox.Text) ? "TESTSCHEMA" : _parsingSchemaTextBox.Text.Trim(),
            SqlclProfile = string.IsNullOrWhiteSpace(_sqlclProfileTextBox.Text) ? "local-apex-dev" : _sqlclProfileTextBox.Text.Trim(),
            SourcePath = string.IsNullOrWhiteSpace(_sourcePathTextBox.Text) ? "src/apex" : _sourcePathTextBox.Text.Trim(),
        };

    private void UpdateConnectButtonState()
        => _connectButton.IsEnabled = _lastDiscovery is not null && _applicationsListBox.SelectedItem is OracleApexApplicationListItem;

    private IBrush GetValidationBrush(string resourceKey)
        => TryGetResource(resourceKey, ActualThemeVariant, out var brush) && brush is IBrush resolvedBrush
            ? resolvedBrush
            : Brushes.Gray;

    private sealed class OracleApexApplicationListItem
    {
        public OracleApexApplicationListItem(OracleApexApplicationInfo application)
        {
            Application = application;
        }

        public OracleApexApplicationInfo Application { get; }
        public string DisplayText => $"{Application.ApplicationId} - {Application.ApplicationName}";
        public string DetailText => string.IsNullOrWhiteSpace(Application.Alias) ? "No alias" : $"Alias: {Application.Alias}";
    }
}
