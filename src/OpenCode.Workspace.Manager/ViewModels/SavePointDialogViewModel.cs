using OpenCode.Workspace.Manager.Services;

namespace OpenCode.Workspace.Manager.ViewModels;

public sealed class SavePointDialogViewModel : ObservableObject
{
    private string _savePointMessage;

    public SavePointDialogViewModel(PoLocalizationService localization, string initialMessage)
    {
        _savePointMessage = initialMessage;
        DialogTitle = localization.Get("savePoint.dialog.title");
        DialogDescription = localization.Get("savePoint.dialog.description");
        ConfirmLabel = localization.Get("actions.createSavePoint");
        CancelLabel = localization.Get("actions.cancel");
    }

    public string DialogTitle { get; }
    public string DialogDescription { get; }
    public string ConfirmLabel { get; }
    public string CancelLabel { get; }

    public string SavePointMessage
    {
        get => _savePointMessage;
        set => SetProperty(ref _savePointMessage, value);
    }
}
