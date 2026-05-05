using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DMS.Core.Transactions;

namespace DMS.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly TransactionDispatcher _transactionDispatcher = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private void BtnRunTransaction_Click(object sender, RoutedEventArgs e)
    {
        var command = TransactionParser.Parse(TxtTransaction.Text);
        var result = _transactionDispatcher.Dispatch(command);

        RenderTransactionResult(result);
    }

    private void RenderTransactionResult(TransactionResult result)
    {
        WorkspacePanel.Children.Clear();

        if (!result.Success)
        {
            WorkspacePanel.Children.Add(new TextBlock
            {
                Text = "Chyba transakce",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DarkRed,
                Margin = new Thickness(0, 0, 0, 16)
            });

            WorkspacePanel.Children.Add(new TextBlock
            {
                Text = result.Message,
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap
            });

            return;
        }

        switch (result.TransactionCode)
        {
            case "ART03":
                RenderArticleCard(result.Parameter!);
                break;

            case "DOC03":
                RenderSimplePage("Dokumentace artiklu", result.Message);
                break;

            case "SCR03":
                RenderSimplePage("Síta artiklu", result.Message);
                break;

            case "SCR10":
                RenderSimplePage("Fronta přípravy sít", result.Message);
                break;

            case "ORD10":
                RenderSimplePage("Přehled zakázek", result.Message);
                break;

            default:
                RenderSimplePage(result.TransactionCode, result.Message);
                break;
        }
    }

    private void RenderArticleCard(string articleNumber)
    {
        WorkspacePanel.Children.Add(new TextBlock
        {
            Text = "Karta artiklu",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(11, 42, 74)),
            Margin = new Thickness(0, 0, 0, 16)
        });

        WorkspacePanel.Children.Add(CreateLine($"SAP číslo: {articleNumber}"));
        WorkspacePanel.Children.Add(CreateLine("Název: Flakon 50 ml"));
        WorkspacePanel.Children.Add(CreateLine("Zákazník: Example Cosmetics"));
        WorkspacePanel.Children.Add(CreateLine("Stav: Připraveno"));

        WorkspacePanel.Children.Add(new Separator
        {
            Margin = new Thickness(0, 16, 0, 16)
        });

        WorkspacePanel.Children.Add(new TextBlock
        {
            Text = "Dokumenty",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        WorkspacePanel.Children.Add(CreateLine("✅ Výkres"));
        WorkspacePanel.Children.Add(CreateLine("✅ Tisková oblast"));
        WorkspacePanel.Children.Add(CreateLine("✅ Massblatt"));
        WorkspacePanel.Children.Add(CreateLine("✅ Balicí předpis"));
        WorkspacePanel.Children.Add(CreateLine("✅ Receptura"));
    }

    private void RenderSimplePage(string title, string message)
    {
        WorkspacePanel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(11, 42, 74)),
            Margin = new Thickness(0, 0, 0, 16)
        });

        WorkspacePanel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap
        });
    }

    private static TextBlock CreateLine(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 16,
            Margin = new Thickness(0, 4, 0, 4)
        };
    }
}