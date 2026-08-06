using System.Windows;

namespace DMS.Desktop.UI;

/// <summary>
/// Jednotný vstup pro hlášky a potvrzení v celém DMS klientu.
/// Zajišťuje použití stylovaného DMS dialogu místo nativního MessageBoxu.
/// </summary>
public static class DmsMessage
{
    public static MessageBoxResult Show(
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon)
    {
        return Show(GetActiveOwner(), messageBoxText, caption, button, icon);
    }

    public static MessageBoxResult Show(
        Window? owner,
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon)
    {
        var buttons = button switch
        {
            MessageBoxButton.OK => DmsDialogButtons.Ok,
            MessageBoxButton.OKCancel => DmsDialogButtons.YesNo,
            MessageBoxButton.YesNo => DmsDialogButtons.YesNo,
            MessageBoxButton.YesNoCancel => DmsDialogButtons.YesNoCancel,
            _ => DmsDialogButtons.Ok
        };

        var kind = icon switch
        {
            MessageBoxImage.Error => DmsDialogKind.Error,
            MessageBoxImage.Warning => DmsDialogKind.Warning,
            MessageBoxImage.Question => DmsDialogKind.Question,
            _ => DmsDialogKind.Information
        };

        var result = DmsConfirmDialog.Show(
            owner ?? GetActiveOwner(),
            caption,
            messageBoxText,
            buttons,
            kind);

        if (button == MessageBoxButton.OKCancel)
        {
            return result == MessageBoxResult.Yes
                ? MessageBoxResult.OK
                : MessageBoxResult.Cancel;
        }

        return result;
    }

    public static MessageBoxResult Show(string messageBoxText)
    {
        return Show(messageBoxText, "DMS", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static MessageBoxResult Show(string messageBoxText, string caption)
    {
        return Show(messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static void Info(string title, string message, Window? owner = null) =>
        DmsConfirmDialog.Show(owner ?? GetActiveOwner(), title, message, DmsDialogButtons.Ok, DmsDialogKind.Information);

    public static void Warning(string title, string message, Window? owner = null) =>
        DmsConfirmDialog.Show(owner ?? GetActiveOwner(), title, message, DmsDialogButtons.Ok, DmsDialogKind.Warning);

    public static void Error(string title, string message, Window? owner = null) =>
        DmsConfirmDialog.Show(owner ?? GetActiveOwner(), title, message, DmsDialogButtons.Ok, DmsDialogKind.Error);

    public static bool Confirm(string title, string message, Window? owner = null) =>
        DmsConfirmDialog.Show(owner ?? GetActiveOwner(), title, message, DmsDialogButtons.YesNo, DmsDialogKind.Question)
        == MessageBoxResult.Yes;

    private static Window? GetActiveOwner()
    {
        return Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive)
            ?? Application.Current?.MainWindow;
    }
}
