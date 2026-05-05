using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DMS.Core.Transactions;
using DMS.Desktop.Views.Dialogs;

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
        ExecuteTransaction(TxtTransaction.Text);
    }

    private void ExecuteTransaction(string input)
    {
        var command = TransactionParser.Parse(input);

        if (!TryCompleteMissingParameter(command, out var completedCommand))
        {
            return;
        }

        if (completedCommand.Mode == "NewWindow")
        {
            OpenTransactionInNewWindow(completedCommand);
            return;
        }

        var result = _transactionDispatcher.Dispatch(completedCommand);
        RenderTransactionResult(result);
    }

    private bool TryCompleteMissingParameter(
        TransactionCommand command,
        out TransactionCommand completedCommand)
    {
        completedCommand = command;

        if (!TransactionNeedsArticleNumber(command.Code))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(command.Parameter))
        {
            return true;
        }

        var dialog = new ArticleNumberPromptWindow
        {
            Owner = this
        };

        var dialogResult = dialog.ShowDialog();

        if (dialogResult != true || string.IsNullOrWhiteSpace(dialog.ArticleNumber))
        {
            return false;
        }

        completedCommand = new TransactionCommand
        {
            RawInput = command.RawInput,
            Mode = command.Mode,
            Code = command.Code,
            Parameter = dialog.ArticleNumber
        };

        TxtTransaction.Text = BuildTransactionText(completedCommand);

        return true;
    }

    private static bool TransactionNeedsArticleNumber(string transactionCode)
    {
        return transactionCode.ToUpperInvariant() switch
        {
            "ART03" => true,
            "DOC03" => true,
            "SCR03" => true,
            "MB03" => true,
            "REC03" => true,
            _ => false
        };
    }

    private static string BuildTransactionText(TransactionCommand command)
    {
        var prefix = command.Mode switch
        {
            "Replace" => "/n",
            "NewWindow" => "/o",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(command.Parameter))
        {
            return $"{prefix}{command.Code}";
        }

        return $"{prefix}{command.Code} {command.Parameter}";
    }

    private void OpenTransactionInNewWindow(TransactionCommand command)
    {
        var newWindow = new MainWindow();

        newWindow.Show();

        var commandForNewWindow = new TransactionCommand
        {
            RawInput = command.RawInput,
            Mode = "Current",
            Code = command.Code,
            Parameter = command.Parameter
        };

        newWindow.TxtTransaction.Text = BuildTransactionText(commandForNewWindow);

        var result = newWindow._transactionDispatcher.Dispatch(commandForNewWindow);
        newWindow.RenderTransactionResult(result);
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