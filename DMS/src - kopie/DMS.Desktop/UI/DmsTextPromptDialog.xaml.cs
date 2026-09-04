using System.Windows;

namespace DMS.Desktop.UI;

public partial class DmsTextPromptDialog : Window
{
    private readonly bool _isRequired;

    public DmsTextPromptDialog(
        string title,
        string message,
        string initialValue = "",
        bool isRequired = true)
    {
        InitializeComponent();
        Title = title;
        TxtTitle.Text = title;
        TxtMessage.Text = message;
        TxtValue.Text = initialValue;
        _isRequired = isRequired;

        Loaded += (_, _) =>
        {
            TxtValue.Focus();
            TxtValue.SelectAll();
        };
    }

    public string Value => TxtValue.Text.Trim();

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_isRequired && string.IsNullOrWhiteSpace(Value))
        {
            DmsMessage.Warning("DMS", "Hodnota musí být vyplněna.", this);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public static string? Show(
        Window? owner,
        string title,
        string message,
        string initialValue = "",
        bool isRequired = true)
    {
        var dialog = new DmsTextPromptDialog(title, message, initialValue, isRequired);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true ? dialog.Value : null;
    }
}
