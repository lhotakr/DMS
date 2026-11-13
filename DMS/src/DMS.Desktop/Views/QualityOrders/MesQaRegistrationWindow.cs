using DMS.Core.Quality;
using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DMS.Desktop.Views.QualityOrders;

/// <summary>Explicit QA acceptance of a pending MES order; does not create a 9200 number.</summary>
public sealed class MesQaRegistrationWindow : Window
{
    private readonly MesQaRegistrationStore _queue;
    private readonly QualityOrderMaintenanceService _quality;
    private readonly JsonQualityRepository _repository;
    private readonly string _selectedOrder2000;
    public string? AcceptedOrder9200 { get; private set; }
    private readonly DmsLogger? _logger;
    private readonly string _user;
    private readonly Func<string, string>? _translate;
    private readonly DataGrid _grid = new() { IsReadOnly = true, AutoGenerateColumns = false,
        SelectionMode = DataGridSelectionMode.Single, Margin = new Thickness(0, 10, 0, 10) };
    private readonly TextBox _order9200 = new() { Width = 165, Height = 30, Margin = new Thickness(0, 0, 10, 0) };
    private readonly TextBlock _printVersion = new()
    {
        Width = 240, TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0)
    };

    public MesQaRegistrationWindow(string dmsDataRoot, DmsLogger? logger, string user, Func<string, string>? translate = null, string? selectedOrder2000 = null)
    {
        _queue = new MesQaRegistrationStore(dmsDataRoot);
        _repository = new JsonQualityRepository(new QualityStoragePaths(dmsDataRoot));
        _quality = new QualityOrderMaintenanceService(_repository);
        _selectedOrder2000 = selectedOrder2000?.Trim() ?? string.Empty;
        _logger = logger;
        _user = user;
        _translate = translate;
        Title = L("QO.MES.WindowTitle", "QA – převzetí výrobních zakázek z MES");
        Width = 980; Height = 580; MinWidth = 750; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)Application.Current.FindResource("DmsPanelBrush");
        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("DmsForegroundBrush");
        var root = new DockPanel { Margin = new Thickness(18) };
        Content = root;
        var title = new TextBlock { Text = L("QO.MES.PendingTitle", "Čekající registrace z ORD10"), FontSize = 20,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) };
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var hint = new TextBlock { Text = L("QO.MES.PendingHelp", "Vyber zakázku 2000 a zadej pouze číslo zakázky 9200. Tisková verze se dohledá podle MES a cílové množství se převezme automaticky."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
        var fields = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        fields.Children.Add(new TextBlock { Text = L("ORD10.QA.Order9200", "Zakázka 9200") + ":", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,6,0) });
        fields.Children.Add(_order9200);
        fields.Children.Add(new TextBlock { Text = L("QO.MES.PrintVersion", "Tisková verze") + ":", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,6,0) });
        fields.Children.Add(_printVersion);
        _grid.SelectionChanged += (_, _) => UpdateDetectedPrintVersion();
        var btn = new Button { Content = L("QO.MES.Accept", "Převzít do QA"), Padding = new Thickness(12, 5, 12, 5) };
        btn.Click += (_, _) => AcceptSelected(); fields.Children.Add(btn);
        DockPanel.SetDock(fields, Dock.Bottom); root.Children.Add(fields);
        root.Children.Add(_grid);
        AddColumn("ORD10.QA.Order2000", "Zakázka 2000", "Order2000", 110);
        AddColumn("ORD10.QA.Order9200", "Zakázka 9200", "Order9200", 110);
        AddColumn("QO.MES.Material", "SAP", "SapMaterialNumber", 125);
        AddColumn("QO.MES.MesProduct", "Artikl MES", "MesProductCode", 165);
        AddColumn("QO.MES.Target", "Cíl MES", "TargetQuantity", 100);
        AddColumn("QO.MES.Status", "Stav", "Status", 100);
        AddColumn("QO.MES.RegisteredAt", "Registrováno", "RegisteredAt", 150);
        AddColumn("QO.MES.RegisteredBy", "Registroval", "RegisteredBy", 145);
        Reload();
    }

    private void AddColumn(string key, string fallback, string property, double width)
    {
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = L(key, fallback), Binding = new Binding(property), Width = new DataGridLength(width)
        });
    }

    private string L(string key, string fallback)
    {
        var value = _translate?.Invoke(key);
        return string.IsNullOrWhiteSpace(value) || value == key || value == "[[" + key + "]]"
            ? fallback : value;
    }

    private void Reload()
    {
        try
        {
            var rows = _queue.Load().OrderBy(x => x.Status == "Accepted")
                .ThenByDescending(x => x.RegisteredAt).ToList();
            _grid.ItemsSource = rows;
            _grid.SelectedItem = rows.FirstOrDefault(x =>
                string.Equals(x.Order2000, _selectedOrder2000, StringComparison.OrdinalIgnoreCase))
                ?? rows.FirstOrDefault(x => x.Status == "Pending");
            UpdateDetectedPrintVersion();
        }
        catch (Exception e)
        {
            _logger?.Error("QO05 MES registration queue load failed.", e);
            DmsConfirmDialog.ShowInfo(this, "QA", L("QO.MES.QueueError", "Nelze načíst frontu: ") + e.Message);
        }
    }

    private void AcceptSelected()
    {
        if (_grid.SelectedItem is not MesQaRegistration row || row.Status != "Pending")
        {
            DmsConfirmDialog.ShowInfo(this, "QA", L("QO.MES.SelectPending", "Vyber čekající požadavek."));
            return;
        }
        var number = _order9200.Text.Trim();
        if (string.IsNullOrWhiteSpace(number))
        {
            DmsConfirmDialog.ShowInfo(this, "QA", L("QO.MES.Required", "Vyplň číslo zakázky 9200."));
            return;
        }
        try
        {
            var printVersion = ResolvePrintVersion(row);
            var old = _quality.FindOrder(number);
            if (old is not null && !string.IsNullOrWhiteSpace(old.MesOrderNumber2000) &&
                !string.Equals(old.MesOrderNumber2000, row.Order2000, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(L("QO.MES.DifferentLink", "Tato zakázka 9200 už patří jiné zakázce 2000."));
            if (old is not null && !string.Equals(old.PrintVersionNumber, printVersion.FullPrintVersionNumber, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(L("QO.MES.DifferentVersion", "Existující zakázka 9200 má jinou tiskovou verzi."));
            if (row.TargetQuantity <= 0 || decimal.Truncate(row.TargetQuantity) != row.TargetQuantity || row.TargetQuantity > int.MaxValue)
                throw new InvalidOperationException(L("QO.MES.InvalidTarget", "Cílové množství MES nelze bezpečně převést na celé kusy QA."));
            var target = checked((int)row.TargetQuantity);
            if (old is not null && old.OrderedQuantity.HasValue && old.OrderedQuantity != target)
            {
                var confirmation = DmsConfirmDialog.Show(this, "QA", string.Format(L("QO.MES.QuantityChangePrompt", "QA 9200 {0} má množství {1}. Převzít cíl MES {2}?"), number, old.OrderedQuantity, target),
                    DmsDialogButtons.YesNo);
                if (confirmation != MessageBoxResult.Yes) return;
            }
            else
            {
                var confirmation = DmsConfirmDialog.Show(this, "QA", string.Format(L("QO.MES.LinkPrompt", "Přiřadit zakázku 2000 {0} k zakázce 9200 {1} a množství {2}?"), row.Order2000, number, target),
                    DmsDialogButtons.YesNo);
                if (confirmation != MessageBoxResult.Yes) return;
            }
            var model = old is null ? _quality.PrepareCreate(printVersion.FullPrintVersionNumber) : _quality.PrepareEdit(number)!;
            model.IsCreateMode = old is null;
            model.OrderNumber = number;
            model.PrintVersionNumber = printVersion.FullPrintVersionNumber;
            model.MesOrderNumber2000 = row.Order2000;
            model.OrderedQuantity = target;
            if (old is null && string.IsNullOrWhiteSpace(model.SapMaterialNumber)) model.SapMaterialNumber = row.SapMaterialNumber;
            var saved = _quality.Save(model, _user);
            if (!saved.Success || saved.SavedOrder is null) throw new InvalidOperationException(saved.Message);
            _queue.MarkAccepted(row.Order2000, number, _user);
            if (old is null)
                _logger?.AuditCreated("QUALITY", "QualityOrder", number, _user,
                    $"Source=MES; Order2000={row.Order2000}; TargetQuantity={target}; PrintVersion={printVersion.FullPrintVersionNumber}");
            else
            {
                _logger?.AuditChange("QUALITY", "QualityOrder", number, "MesOrderNumber2000", old.MesOrderNumber2000, row.Order2000, _user);
                _logger?.AuditChange("QUALITY", "QualityOrder", number, "OrderedQuantity",
                    old.OrderedQuantity?.ToString(), target.ToString(), _user);
            }
            _logger?.AuditChange("QUALITY", "MesQaRegistration", row.Order2000, "Status", "Pending", "Accepted", _user);
            AcceptedOrder9200 = number;
            DmsConfirmDialog.ShowInfo(this, "QA", L("QO.MES.Accepted", "Zakázka byla převzata. QO02 může pokračovat v úpravách."));
            if (!string.IsNullOrWhiteSpace(_selectedOrder2000))
                DialogResult = true;
            else
                Reload();
        }
        catch (Exception ex)
        {
            _logger?.Error($"QA MES registration acceptance failed. Order2000={row.Order2000}; Order9200={number}", ex);
            DmsConfirmDialog.ShowInfo(this, "QA", L("QO.MES.AcceptFailed", "Převzetí selhalo: ") + ex.Message);
        }
    }
    // MES 0112345.100.99001 and QA 0112345100.99001 represent the same print version.
    // Preserve leading zeros and reject ambiguous matches; do not guess a print version.
    private static string CanonicalVersion(string? value) =>
        new string((value ?? string.Empty).Where(c => c != '.' && c != ' ' && c != '-').ToArray())
            .ToUpperInvariant();

    private QualityPrintVersion ResolvePrintVersion(MesQaRegistration row)
    {
        var all = _repository.LoadPrintVersions().ToList();
        var code = CanonicalVersion(row.MesProductCode);
        var matches = string.IsNullOrWhiteSpace(code)
            ? new List<QualityPrintVersion>()
            : all.Where(item =>
                CanonicalVersion(item.FullPrintVersionNumber) == code ||
                CanonicalVersion(item.LegacyArticleNumber) == code).ToList();
        // Old v1.2 requests contain no MES product code: only use an unambiguous SAP match.
        if (matches.Count == 0 && string.IsNullOrWhiteSpace(code))
        {
            var sap = (row.SapMaterialNumber ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(sap))
                matches = all.Where(item =>
                    string.Equals(item.SapMaterialNumber?.Trim(), sap, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        matches = matches.GroupBy(item => item.FullPrintVersionNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToList();
        if (matches.Count == 1) return matches[0];
        if (matches.Count == 0)
            throw new InvalidOperationException(L("QO.MES.AutoVersionMissing",
                "Tisková verze z MES nebyla v QA jednoznačně nalezena. Zkontroluj evidenci tiskových verzí."));
        throw new InvalidOperationException(L("QO.MES.AutoVersionAmbiguous",
            "Pro artikl MES existuje více tiskových verzí. Převzetí bylo zastaveno."));
    }

    private void UpdateDetectedPrintVersion()
    {
        if (_grid.SelectedItem is not MesQaRegistration row)
        {
            _printVersion.Text = "—";
            return;
        }
        try
        {
            _printVersion.Text = ResolvePrintVersion(row).FullPrintVersionNumber;
        }
        catch
        {
            _printVersion.Text = L("QO.MES.AutoVersionUnknown", "Nenalezeno – zkontroluj tiskovou verzi QA");
        }
    }

}
