using System.Windows;

namespace DMS.Desktop.UI;

public enum DmsDialogButtons
{
    Ok,
    YesNo,
    YesNoCancel
}

public enum DmsDialogKind
{
    Information,
    Warning,
    Error,
    Question
}

public partial class DmsConfirmDialog : Window
{
    private readonly DmsDialogButtons _buttons;

    public DmsConfirmDialog(
        string title,
        string message,
        DmsDialogButtons buttons = DmsDialogButtons.YesNoCancel,
        string okText = "OK",
        string yesText = "Ano",
        string noText = "Ne",
        string cancelText = "Zrušit",
        DmsDialogKind kind = DmsDialogKind.Question)
    {
        InitializeComponent();

        _buttons = buttons;

        TxtTitle.Text = title;
        TxtMessage.Text = message;
        ApplyKind(kind);

        ConfigureButtons(
            buttons,
            okText,
            yesText,
            noText,
            cancelText);
    }

    public MessageBoxResult Result { get; private set; }
        = MessageBoxResult.Cancel;


    private void ApplyKind(DmsDialogKind kind)
    {
        TxtIcon.Text = kind switch
        {
            DmsDialogKind.Error => "✕",
            DmsDialogKind.Warning => "!",
            DmsDialogKind.Information => "i",
            _ => "?"
        };

        TxtIcon.SetResourceReference(
            System.Windows.Controls.TextBlock.ForegroundProperty,
            kind switch
            {
                DmsDialogKind.Error => "DmsErrorBrush",
                DmsDialogKind.Warning => "DmsWarningBrush",
                DmsDialogKind.Information => "DmsAccentBrush",
                _ => "DmsAccentBrush"
            });
    }

    private void ConfigureButtons(
        DmsDialogButtons buttons,
        string okText,
        string yesText,
        string noText,
        string cancelText)
    {
        BtnYes.Visibility = Visibility.Visible;
        BtnNo.Visibility = Visibility.Visible;
        BtnCancel.Visibility = Visibility.Visible;

        switch (buttons)
        {
            case DmsDialogButtons.Ok:
                BtnYes.Content = okText;
                BtnNo.Visibility = Visibility.Collapsed;
                BtnCancel.Visibility = Visibility.Collapsed;
                Result = MessageBoxResult.OK;
                break;

            case DmsDialogButtons.YesNo:
                BtnYes.Content = yesText;
                BtnNo.Content = noText;
                BtnCancel.Visibility = Visibility.Collapsed;
                Result = MessageBoxResult.No;
                break;

            case DmsDialogButtons.YesNoCancel:
                BtnYes.Content = yesText;
                BtnNo.Content = noText;
                BtnCancel.Content = cancelText;
                Result = MessageBoxResult.Cancel;
                break;
        }
    }

    private void BtnYes_Click(object sender, RoutedEventArgs e)
    {
        Result = _buttons == DmsDialogButtons.Ok
            ? MessageBoxResult.OK
            : MessageBoxResult.Yes;

        DialogResult = true;
        Close();
    }

    private void BtnNo_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.No;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Cancel;
        DialogResult = false;
        Close();
    }

    public static MessageBoxResult Show(
        Window? owner,
        string title,
        string message,
        DmsDialogButtons buttons = DmsDialogButtons.YesNoCancel)
    {
        var dialog = new DmsConfirmDialog(
            title,
            message,
            buttons);

        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();

        return dialog.Result;
    }

    public static MessageBoxResult Show(
        Window? owner,
        string title,
        string message,
        DmsDialogButtons buttons,
        string okText,
        string yesText,
        string noText,
        string cancelText)
    {
        var dialog = new DmsConfirmDialog(
            title,
            message,
            buttons,
            okText,
            yesText,
            noText,
            cancelText);

        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();

        return dialog.Result;
    }

    public static MessageBoxResult Show(
        Window? owner,
        string title,
        string message,
        DmsDialogButtons buttons,
        DmsDialogKind kind)
    {
        var dialog = new DmsConfirmDialog(
            title,
            message,
            buttons,
            kind: kind);

        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
        return dialog.Result;
    }

    // Zpětná kompatibilita pro starší volání.
    public static MessageBoxResult Show(
        Window? owner,
        string title,
        string message,
        bool showCancel = true)
    {
        return Show(
            owner,
            title,
            message,
            showCancel
                ? DmsDialogButtons.YesNoCancel
                : DmsDialogButtons.YesNo);
    }

    public static void ShowInfo(
        Window? owner,
        string title,
        string message)
    {
        Show(
            owner,
            title,
            message,
            DmsDialogButtons.Ok);
    }

    public static void Info(
        Window? owner,
        string title,
        string message)
    {
        ShowInfo(owner, title, message);
    }

    public static void Warning(
        Window? owner,
        string title,
        string message)
    {
        Show(
            owner,
            title,
            message,
            DmsDialogButtons.Ok);
    }

    public static bool ShowQuestion(
        Window? owner,
        string title,
        string message)
    {
        var result = Show(
            owner,
            title,
            message,
            DmsDialogButtons.YesNo);

        return result == MessageBoxResult.Yes;
    }
}
