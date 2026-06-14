using System.Threading;
using System.Windows;
using OpenCode.Workspace.Manager.ViewModels;

namespace OpenCode.Workspace.Manager.Services;

public static class AppDialogService
{
    public static AppDialogResult ShowOk(Window? owner, PoLocalizationService localization, string title, string message)
    {
        if (!CanShowDialog(owner))
        {
            return AppDialogResult.Ok;
        }

        var dialog = CreateDialog(owner, title, message, AppDialogButtons.Ok, localization);
        dialog.ShowDialog();
        return dialog.Result;
    }

    public static AppDialogResult ShowYesNo(Window? owner, PoLocalizationService localization, string title, string message)
    {
        if (!CanShowDialog(owner))
        {
            return AppDialogResult.No;
        }

        var dialog = CreateDialog(owner, title, message, AppDialogButtons.YesNo, localization);
        dialog.ShowDialog();
        return dialog.Result;
    }

    private static bool CanShowDialog(Window? owner)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            return false;
        }

        if (owner is not null)
        {
            return owner.Dispatcher.CheckAccess() && !owner.Dispatcher.HasShutdownStarted && !owner.Dispatcher.HasShutdownFinished;
        }

        var application = Application.Current;
        if (application is null)
        {
            return true;
        }

        return application.Dispatcher.CheckAccess() && !application.Dispatcher.HasShutdownStarted && !application.Dispatcher.HasShutdownFinished;
    }

    private static AppDialogWindow CreateDialog(Window? owner, string title, string message, AppDialogButtons buttons, PoLocalizationService localization)
        => new()
        {
            Owner = owner,
            ShowInTaskbar = false,
            DataContext = new AppDialogViewModel(title, message, buttons, localization),
        };
}

public enum AppDialogButtons
{
    Ok,
    YesNo,
}

public enum AppDialogResult
{
    None,
    Ok,
    Yes,
    No,
}
