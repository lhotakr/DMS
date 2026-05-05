using System.Windows;
using DMS.Core.Articles;

namespace DMS.Desktop.Views.Dialogs;

public partial class ArticleNumberPromptWindow : Window
{
    public string? ArticleNumber { get; private set; }

    public ArticleNumberPromptWindow()
    {
        InitializeComponent();
        TxtArticleNumber.Focus();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        var value = TxtArticleNumber.Text.Trim();

        if (!ArticleNumberValidator.IsValid(value))
        {
            MessageBox.Show(
                "Zadej platné desetimístné SAP číslo artiklu.",
                "Neplatné číslo artiklu",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            TxtArticleNumber.Focus();
            TxtArticleNumber.SelectAll();
            return;
        }

        ArticleNumber = value;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}