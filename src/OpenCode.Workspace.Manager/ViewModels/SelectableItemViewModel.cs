namespace OpenCode.Workspace.Manager.ViewModels;

public sealed class SelectableItemViewModel : ObservableObject
{
    private bool _isSelected;

    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public bool IsLocked { get; init; }
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
