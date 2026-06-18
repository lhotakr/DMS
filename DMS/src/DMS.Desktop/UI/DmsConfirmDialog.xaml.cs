using System.Windows;

namespace DMS.Desktop.UI;

public enum DmsDialogButtons
{
    Ok,
    YesNo,
    YesNoCancel
}

public partial class DmsConfirmDialog : Window
{
    public DmsConfirmDialog(
        string title,
        string message,
        DmsDialogButtons buttons = DmsDialogButtons.YesNoCancel)
    {
        InitializeComponent();

        TxtTitle.Text = title;
        TxtMessage.Text = message;

        ConfigureButtons(buttons);
    }

    public MessageBoxResult Result { get; private set; }
        = MessageBoxResult.Cancel;

    private void ConfigureButtons(DmsDialogButtons buttons)
    {
        BtnYes.Visibility = Visibility.Visible;
        BtnNo.Visibility = Visibility.Visible;
        BtnCancel.Visibility = Visibility.Visible;

        switch (buttons)
        {
            case DmsDialogButtons.Ok:
                BtnYes.Content = "OK";
                BtnNo.Visibility = Visibility.Collapsed;
                BtnCancel.Visibility = Visibility.Collapsed;
                Result = MessageBoxResult.OK;
                break;

            case DmsDialogButtons.YesNo:
                BtnYes.Content = "Ano";
                BtnNo.Content = "Ne";
                BtnCancel.Visibility = Visibility.Collapsed;
                Result = MessageBoxResult.No;
                break;

            case DmsDialogButtons.YesNoCancel:
                BtnYes.Content = "Ano";
                BtnNo.Content = "Ne";
                BtnCancel.Content = "Zrušit";
                Result = MessageBoxResult.Cancel;
                break;
        }
    }

    private void BtnYes_Click(object sender, RoutedEventArgs e)
    {
        Result = BtnYes.Content?.ToString() == "OK"
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