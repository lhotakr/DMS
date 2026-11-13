using DMS.Core.Quality;
using DMS.Core.Sap;
using DMS.Desktop.UI;
using DMS.Desktop.Views.QualityOrders;
using DMS.Integration.Mes.Orders;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DMS.Desktop.Views.Orders;

public partial class OrderOverviewView
{
    private MesQaRegistrationStore? _qaRegistrationStore;
    private Action<string>? _openQualityOrder;
    private string _dataRootPath = string.Empty;

    private void InitializeQaIntegration(string dataRootPath)
    {
        _dataRootPath = dataRootPath;
        _qaRegistrationStore = new MesQaRegistrationStore(dataRootPath);
        // Add QA and plant 9200 columns at the beginning without changing MESORD navigation.
        var qaColumn = new DataGridTemplateColumn { Header = T("ORD10.QA.Action", "QA"), Width = new DataGridLength(125) };
        var template = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(Button));
        factory.SetBinding(Button.ContentProperty, new Binding(nameof(MesProductionOrderRecord.QaActionLabel)));
        factory.SetValue(Button.PaddingProperty, new Thickness(6, 2, 6, 2));
        factory.SetValue(Button.MarginProperty, new Thickness(2));
        factory.AddHandler(Button.ClickEvent, new RoutedEventHandler(QaRegister_Click));
        template.VisualTree = factory;
        qaColumn.CellTemplate = template;
        GridOrders.Columns.Insert(2, qaColumn);
        GridOrders.Columns.Insert(3, new DataGridTextColumn
        {
            Header = T("ORD10.QA.Order9200", "Zakázka 9200"),
            Binding = new Binding(nameof(MesProductionOrderRecord.QaOrder9200)), Width = new DataGridLength(125),
            IsReadOnly = true
        });
        GridOrders.Columns.Insert(4, new DataGridTextColumn
        {
            Header = T("ORD10.SapDescription", "Popis SAP"),
            Binding = new Binding(nameof(MesProductionOrderRecord.SapCacheDescription)), Width = new DataGridLength(245),
            IsReadOnly = true
        });
        ColOrder.Header = T("ORD10.QA.Order2000", "Zakázka 2000");
        ColPlannedStart.Visibility = Visibility.Collapsed;
        ColPlannedEnd.Visibility = Visibility.Collapsed;
    }

    private void EnrichOrdersForQaAndSap(IReadOnlyList<MesProductionOrderRecord> rows)
    {
        Dictionary<string, string> sap = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var storage = new SapStoragePaths(_dataRootPath);
            sap = new JsonSapMaterialRepository(storage.SapMaterialsFilePath)
                .LoadAll().Where(x => !string.IsNullOrWhiteSpace(x.MaterialNumber))
                .GroupBy(x => x.MaterialNumber.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First().Description, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) { _logger.Error("ORD10 SAP cache enrichment failed.", e); }

        Dictionary<string, QualityOrder> quality = new(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<MesQaRegistration> registrations = Array.Empty<MesQaRegistration>();
        try
        {
            quality = new JsonQualityRepository(new QualityStoragePaths(_dataRootPath)).LoadOrders()
                .Where(x => !string.IsNullOrWhiteSpace(x.MesOrderNumber2000))
                .GroupBy(x => x.MesOrderNumber2000.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            registrations = _qaRegistrationStore?.Load() ?? Array.Empty<MesQaRegistration>();
        }
        catch (Exception e) { _logger.Error("ORD10 QA linkage read failed.", e); }
        var links = registrations.GroupBy(x => x.Order2000, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (sap.TryGetValue((row.SapArticleNumber ?? "").Trim(), out var description))
                row.SapCacheDescription = description ?? "";
            if (quality.TryGetValue(row.OrderCode, out var q))
            {
                row.QaOrder9200 = q.OrderNumber;
                row.QaActionLabel = T("ORD10.QA.Open", "Otevřít QA");
            }
            else if (links.TryGetValue(row.OrderCode, out var link))
            {
                row.QaOrder9200 = link.Order9200;
                row.QaActionLabel = link.Status == "Accepted"
                    ? T("ORD10.QA.Open", "Otevřít QA") : T("ORD10.QA.Pending", "Čeká na QA");
            }
            else
            {
                row.QaOrder9200 = "";
                row.QaActionLabel = T("ORD10.QA.Register", "Registrovat");
            }
        }
    }

    private void QaRegister_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MesProductionOrderRecord row } || _qaRegistrationStore is null) return;
        try
        {
            EnrichOrdersForQaAndSap(new[] { row });
            if (!string.IsNullOrWhiteSpace(row.QaOrder9200))
            {
                if (_openQualityOrder is not null) _openQualityOrder(row.QaOrder9200);
                else DmsMessage.Show(T("ORD10.QA.Linked", "Zakázka je již spojena s QA: ") + row.QaOrder9200,
                    "ORD10", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var existing = _qaRegistrationStore.Load().FirstOrDefault(item =>
                string.Equals(item.Order2000, row.OrderCode, StringComparison.OrdinalIgnoreCase));
            var created = _qaRegistrationStore.Register(
                row.OrderCode, row.SapArticleNumber, row.TargetQuantity, _user, row.ProductCode);
            if (existing is null)
                _logger.AuditCreated("ORD10", "MesQaRegistration", created.Order2000, _user,
                    $"Action=Register; Sap={created.SapMaterialNumber}; MesProduct={created.MesProductCode}; TargetQuantity={created.TargetQuantity}; Status=Pending");
            row.QaActionLabel = T("ORD10.QA.Pending", "Čeká na QA");
            GridOrders.Items.Refresh();
            var dialog = new MesQaRegistrationWindow(_dataRootPath, _logger, _user, _translate, row.OrderCode)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
            EnrichOrdersForQaAndSap(new[] { row });
            GridOrders.Items.Refresh();
            if (!string.IsNullOrWhiteSpace(dialog.AcceptedOrder9200) && _openQualityOrder is not null)
                _openQualityOrder(dialog.AcceptedOrder9200);
        }
        catch (Exception ex)
        {
            _logger.Error($"ORD10 QA registration failed. Order2000={row.OrderCode}", ex);
            DmsMessage.Show(ex.Message, "ORD10", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
