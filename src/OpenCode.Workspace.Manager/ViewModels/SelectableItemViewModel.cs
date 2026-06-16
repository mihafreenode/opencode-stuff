namespace OpenCode.Workspace.Manager.ViewModels;

public sealed class SelectableItemViewModel : ObservableObject
{
    private bool _isSelected;
    private string _displayName = string.Empty;
    private string _description = string.Empty;
    private bool _isLocked;

    public required string Id { get; init; }
    public required string BaseDisplayName { get; init; }
    public required string BaseDescription { get; init; }
    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (SetProperty(ref _isLocked, value))
            {
                RaisePropertyChanged(nameof(CanChangeSelection));
                if (_isLocked)
                {
                    IsSelected = true;
                }
            }
        }
    }

    public bool CanChangeSelection => !IsLocked;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (IsLocked)
            {
                SetProperty(ref _isSelected, true);
                return;
            }

            SetProperty(ref _isSelected, value);
        }
    }
}
