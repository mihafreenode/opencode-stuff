using OpenCode.Workspace.Manager.Services;

namespace OpenCode.Workspace.Manager.ViewModels;

public sealed class AppDialogViewModel
{
    public AppDialogViewModel(string title, string message, AppDialogButtons buttons, PoLocalizationService localization)
    {
        Title = title;
        Message = message;
        Buttons = buttons;
        OkLabel = localization.Get("actions.ok");
        PrimaryLabel = buttons == AppDialogButtons.OpenFileCancel ? "Open File" : localization.Get("actions.yes");
        SecondaryLabel = buttons == AppDialogButtons.OpenFileCancel ? localization.Get("actions.cancel") : localization.Get("actions.no");
    }

    public string Title { get; }
    public string Message { get; }
    public AppDialogButtons Buttons { get; }
    public string OkLabel { get; }
    public string PrimaryLabel { get; }
    public string SecondaryLabel { get; }
    public bool ShowOk => Buttons == AppDialogButtons.Ok;
    public bool ShowPrimarySecondary => Buttons == AppDialogButtons.YesNo || Buttons == AppDialogButtons.OpenFileCancel;
}
