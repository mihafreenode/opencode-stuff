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
        YesLabel = localization.Get("actions.yes");
        NoLabel = localization.Get("actions.no");
    }

    public string Title { get; }
    public string Message { get; }
    public AppDialogButtons Buttons { get; }
    public string OkLabel { get; }
    public string YesLabel { get; }
    public string NoLabel { get; }
    public bool ShowOk => Buttons == AppDialogButtons.Ok;
    public bool ShowYesNo => Buttons == AppDialogButtons.YesNo;
}
