using DMS.Desktop.Logging;
using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Orders;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DMS.Desktop.Views.Orders;

/// <summary>
/// MESORD phase 1: read-only order header and operation history.
/// Daily production, personnel and process samples require verified historical SQL sources.
/// </summary>
public sealed class MesOrderHistoryView : UserControl
{
    private readonly string _orderNumber;
    private readonly string _configRoot;
    private readonly DmsLogger _logger;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, FontSize = 15, Margin = new Thickness(0, 8, 0, 12) };
    private readonly DataGrid _operations = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, MinHeight = 180 };
    private bool _loaded;

    public MesOrderHistoryView(string orderNumber, string configurationRootPath, DmsLogger logger)
    {
        _orderNumber = (orderNumber ?? string.Empty).Trim();
        _configRoot = configurationRootPath;
        _logger = logger;
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = "MESORD – historie výrobní zakázky", FontSize = 23, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock { Text = "FASTEC / závod 2000 · Zakázka " + _orderNumber, FontSize = 15, Margin = new Thickness(0, 6, 0, 4) });
        root.Children.Add(_status);
        root.Children.Add(_summary);
        root.Children.Add(new TextBlock { Text = "Operace zakázky", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 8) });
        AddColumn("Operace", nameof(MesProductionOrderOperationRecord.OperationCode));
        AddColumn("Pracoviště", nameof(MesProductionOrderOperationRecord.WorkcenterCode));
        AddColumn("Popis", nameof(MesProductionOrderOperationRecord.Description));
        AddColumn("Začátek", nameof(MesProductionOrderOperationRecord.ActualStart));
        AddColumn("Konec", nameof(MesProductionOrderOperationRecord.ActualEnd));
        AddColumn("Dobré kusy", nameof(MesProductionOrderOperationRecord.FinishedQuantity));
        AddColumn("Odpad", nameof(MesProductionOrderOperationRecord.ScrapQuantity));
        root.Children.Add(_operations);
        root.Children.Add(new TextBlock {
            Text = "Denní výroba, historie přihlášení operátorů a procesní hodnoty: čeká se na ověření historických zdrojů dat FASTEC. Zde se nezobrazují odhadované hodnoty.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 8), Foreground = Brushes.DarkOrange
        });
        root.Children.Add(new TextBlock {
            Text = "Intercompany: vazba na zakázku závodu 9200 a evidence kvality jsou připraveny jako samostatné rozšíření, nikoli jako změna dat FASTEC.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8)
        });
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        Loaded += async (_, _) => { if (_loaded) return; _loaded = true; await LoadAsync(); };
    }

    private void AddColumn(string header, string property) => _operations.Columns.Add(new DataGridTextColumn
    {
        Header = header, Binding = new Binding(property), Width = new DataGridLength(1, DataGridLengthUnitType.Star)
    });

    private async Task LoadAsync()
    {
        try
        {
            _status.Text = "Načítám zakázku…";
            var names = new[] { "mes-database-settings.json", "mes-reporting-settings.json", "mes-sql-settings.json", "mes-database.json", "mes-reporting.json" };
            var path = names.Select(n => Path.Combine(_configRoot, n)).FirstOrDefault(File.Exists);
            if (path == null)
            {
                _status.Text = "Nastavení MES SQL nebylo nalezeno. Zkontroluj MESSET.";
                return;
            }
            var settings = new MesDatabaseSettingsService().Load(path);
            if (!settings.IsEnabled) { _status.Text = "Připojení MES SQL je vypnuté v MESSET."; return; }
            var service = new MesOrderOverviewDataService(settings);
            var orders = await service.GetOrdersAsync(new MesOrderOverviewFilter { SearchText = _orderNumber, MaxRows = 500 });
            var order = orders.FirstOrDefault(o => string.Equals(o.OrderCode, _orderNumber, StringComparison.OrdinalIgnoreCase));
            if (order == null) { _status.Text = "Zakázka nebyla nalezena mezi dostupnými nearchivovanými zakázkami. Archivované zakázky vyžadují samostatný dotaz."; return; }
            var operations = await service.GetOperationsAsync(order.Id, order.OrderCode);
            _operations.ItemsSource = operations;
            _status.Text = "Načteno z FASTEC · stav " + order.StatusCode;
            _summary.Text = $"Artikl: {order.ProductCode}  |  SAP: {order.SapArticleNumber}\n" +
                $"Popis: {order.ProductDescription}\n" +
                $"Plán: {order.TargetQuantity:N0}  |  Hotovo: {order.FinishedQuantity:N0}  |  NOK: {order.ScrapQuantity:N0}\n" +
                $"Skutečný začátek: {order.ActualStart:dd.MM.yyyy HH:mm}  |  Konec: {order.ActualEnd:dd.MM.yyyy HH:mm}\n" +
                $"Operací: {order.OperationCount}  |  Původ dat: FASTEC dbo.d_pda_po / dbo.d_pda_po_routing";
        }
        catch (Exception ex)
        {
            _logger.Error("MESORD order history loading failed.", ex);
            _status.Text = "Načtení historie zakázky selhalo: " + ex.Message;
        }
    }
}
