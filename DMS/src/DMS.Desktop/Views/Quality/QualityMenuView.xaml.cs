using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Quality;

public partial class QualityMenuView : UserControl
{
    private readonly Action<string> _executeTransaction;
    private readonly Func<string, (bool Allowed, string Reason)> _authorization;

    private static readonly MenuRow[] Rows =
    {
        new(new MenuItem("QA00", "Import z PowerApps"), new MenuItem("QASET", "Nastavení modulu")),
        new(new MenuItem("QA01", "Založení artiklu"), new MenuItem("QA02", "Úprava artiklu"), new MenuItem("QA03", "Náhled artiklu")),
        new(new MenuItem("QA05", "Přehled artiklů"), new MenuItem("QATASK", "Přehled úkolů")),
        new(new MenuItem("QO01", "Založení zakázky"), new MenuItem("QO02", "Změna zakázky"), new MenuItem("QO03", "Náhled zakázky")),
        new(new MenuItem("QO05", "Přehled zakázek"), new MenuItem("QO06", "Uvolnění / blokace zakázek"))
    };

    public QualityMenuView(
        Action<string> executeTransaction,
        Func<string, (bool Allowed, string Reason)> authorization)
    {
        InitializeComponent();
        _executeTransaction = executeTransaction;
        _authorization = authorization;
        BuildMenu();
    }

    private void BuildMenu()
    {
        MenuRows.Children.Clear();
        foreach (var row in Rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 14), MinHeight = 70 };
            for (var i = 0; i < row.Items.Length; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (var i = 0; i < row.Items.Length; i++)
            {
                var item = row.Items[i];
                var authorization = _authorization(item.Code);
                var button = new Button
                {
                    Content = $"{item.Code} – {item.Title}",
                    Tag = item.Code,
                    MinHeight = 68,
                    Margin = new Thickness(i == 0 ? 0 : 5, 0, i == row.Items.Length - 1 ? 0 : 5, 0),
                    IsEnabled = authorization.Allowed,
                    Opacity = authorization.Allowed ? 1.0 : 0.48,
                    ToolTip = authorization.Allowed ? $"Spustit {item.Code}" : authorization.Reason,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold
                };
                button.SetResourceReference(Button.StyleProperty, "DmsFormButtonStyle");
                button.Click += (_, _) => _executeTransaction(item.Code);
                Grid.SetColumn(button, i);
                grid.Children.Add(button);
            }
            MenuRows.Children.Add(grid);
        }
    }

    private sealed record MenuItem(string Code, string Title);
    private sealed record MenuRow(params MenuItem[] Items);
}
