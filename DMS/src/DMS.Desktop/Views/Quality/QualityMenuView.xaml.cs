using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Quality;

public partial class QualityMenuView : UserControl
{
    private readonly Action<string> _executeTransaction;
    private readonly Func<string, (bool Allowed, string Reason)> _authorization;
    private readonly Func<string, string> _translate;

    private static readonly MenuRow[] Rows =
    {
        new(new MenuItem("QA01", "Create article"), new MenuItem("QA02", "Edit article"), new MenuItem("QA03", "Article preview")),
        new(new MenuItem("QA05", "Article overview"), new MenuItem("QATASK", "Task overview")),
        new(new MenuItem("QO01", "Create order"), new MenuItem("QO02", "Edit order"), new MenuItem("QO03", "Order preview")),
        new(new MenuItem("QO05", "Order overview"), new MenuItem("QO06", "Release / block orders")),
        new(new MenuItem("QA00", "Import from PowerApps"), new MenuItem("QASET", "Module settings"))
    };

    public QualityMenuView(
        Action<string> executeTransaction,
        Func<string, (bool Allowed, string Reason)> authorization,
        Func<string, string> translate)
    {
        InitializeComponent();
        _executeTransaction = executeTransaction;
        _authorization = authorization;
        _translate = translate ?? throw new ArgumentNullException(nameof(translate));
        ApplyLocalization();
        BuildMenu();
    }

    private string TOr(
        string key,
        string fallback)
    {
        var value = _translate(key);

        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.Ordinal)
               || value.StartsWith("[[", StringComparison.Ordinal)
            ? fallback
            : value;
    }

    private void ApplyLocalization()
    {
        TxtMenuTitle.Text =
            TOr(
                "QAMENU.Title",
                "Quality module – main menu");

        TxtDisabledHint.Text =
            TOr(
                "QAMENU.DisabledHint",
                "A disabled button means the transaction is unavailable or the user is not authorized to run it.");
    }

    private string GetTransactionTitle(MenuItem item)
    {
        return TOr(
            $"Transaction.{item.Code}.Name",
            item.FallbackTitle);
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
                    Content = $"{item.Code} – {GetTransactionTitle(item)}",
                    Tag = item.Code,
                    MinHeight = 68,
                    Margin = new Thickness(i == 0 ? 0 : 5, 0, i == row.Items.Length - 1 ? 0 : 5, 0),
                    IsEnabled = authorization.Allowed,
                    Opacity = authorization.Allowed ? 1.0 : 0.48,
                    ToolTip = authorization.Allowed
                        ? string.Format(TOr("QAMENU.RunTransaction", "Run {0}"), item.Code)
                        : authorization.Reason,
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

    private sealed record MenuItem(string Code, string FallbackTitle);
    private sealed record MenuRow(params MenuItem[] Items);
}
