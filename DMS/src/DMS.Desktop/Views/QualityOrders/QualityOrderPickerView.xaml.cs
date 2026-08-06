using DMS.Core.Quality;
using DMS.Desktop.Logging;
using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DMS.Desktop.Views.QualityOrders;

public partial class QualityOrderPickerView : UserControl
{
    private const int MaxDisplayedRows = 300;

    private readonly string _targetTransaction;
    private readonly bool _blockedOnly;
    private readonly QualityOrderMaintenanceService _service;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;
    private DispatcherTimer? _filterTimer;
    private List<QualityOrderListRow> _allRows = new();

    public event Action<string>? TransactionRequested;

    public QualityOrderPickerView(
        string targetTransaction,
        bool blockedOnly,
        string dmsRootPath,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _targetTransaction = string.IsNullOrWhiteSpace(targetTransaction)
            ? "QO03"
            : targetTransaction.Trim().ToUpperInvariant();
        _blockedOnly = blockedOnly;
        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName)
            ? "UNKNOWN"
            : currentUserName;
        _translate = translate;
        _translateFormat = translateFormat;

        var rootPath = string.IsNullOrWhiteSpace(dmsRootPath)
            ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."))
            : dmsRootPath;

        var paths = new QualityStoragePaths(rootPath);
        paths.EnsureDirectories();
        _service = new QualityOrderMaintenanceService(new JsonQualityRepository(paths));

        _filterTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer?.Stop();
            ApplyFilter();
        };

        ApplyLocalization();
        ReloadData();
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T($"QO.Picker.Title.{_targetTransaction}");
        TxtHint.Text = T($"QO.Picker.Hint.{_targetTransaction}");
        LblFilterOrder.Text = T("QO.Picker.Filter.Order");
        LblFilterPrintVersion.Text = T("QO.Picker.Filter.PrintVersion");
        LblFilterText.Text = T("QO.Picker.Filter.Text");

        TxtFilterOrder.ToolTip = T("QO.Picker.Filter.Order.ToolTip");
        TxtFilterPrintVersion.ToolTip = T("QO.Picker.Filter.PrintVersion.ToolTip");
        TxtFilterText.ToolTip = T("QO.Picker.Filter.Text.ToolTip");
        BtnRefresh.Content = T("QO.Action.Reload");

        ColScheduleLight.Header = "●";
        ColReleaseIcon.Header = "✓";
        ColOrder.Header = T("QO.Col.Order");
        ColPrintVersion.Header = T("QO.Col.PrintVersion");
        ColSap.Header = T("QO.Col.SapId");
        ColCustomer.Header = T("QO.Col.Customer");
        ColMachine.Header = T("QO.Col.Machine");
        ColStatus.Header = T("QO.Col.Status");
        ColNotes.Header = T("QO.Col.Notes");

        ApplyColumnLayout();
    }

    private void ApplyColumnLayout()
    {
        if (GridOrders is null)
        {
            return;
        }

        GridOrders.MinColumnWidth = 48;
        ScrollViewer.SetHorizontalScrollBarVisibility(GridOrders, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(GridOrders, ScrollBarVisibility.Auto);

        SetFixedColumn(ColScheduleLight, 44);
        SetFixedColumn(ColReleaseIcon, 48);
        SetColumn(ColOrder, 95, 90);
        SetColumn(ColPrintVersion, 175, 160);
        SetColumn(ColSap, 115, 105);
        SetColumn(ColCustomer, 150, 130);
        SetColumn(ColMachine, 130, 110);
        SetColumn(ColStatus, 125, 110);
        SetColumn(ColNotes, 360, 260);
    }

    private static void SetFixedColumn(DataGridColumn column, double width)
    {
        column.Width = new DataGridLength(width);
        column.MinWidth = width;
        column.MaxWidth = width;
    }

    private static void SetColumn(DataGridColumn column, double width, double minWidth)
    {
        column.Width = new DataGridLength(width);
        column.MinWidth = minWidth;
        column.MaxWidth = double.PositiveInfinity;
    }

    private void ReloadData()
    {
        _allRows = _service.BuildOrderListRows()
            .Where(row => !_blockedOnly || row.IsBlocked)
            .ToList();
        LocalizeRows(_allRows);

        _logger?.AdminAction(
            _targetTransaction,
            "OpenQualityOrderPicker",
            _currentUserName,
            $"Target={_targetTransaction}; BlockedOnly={_blockedOnly}; Count={_allRows.Count}");

        ApplyFilter();
    }

    private void LocalizeRows(IEnumerable<QualityOrderListRow> rows)
    {
        foreach (var row in rows)
        {
            row.ReleasedText = T($"QO.Release.{row.ReleaseStatusCode}");
            row.ScheduleStatusText = T($"QO.Status.{row.ScheduleStatusCode}");
        }
    }

    private void ApplyFilter()
    {
        var orderFilter = Normalize(TxtFilterOrder.Text);
        var printVersionFilter = Normalize(TxtFilterPrintVersion.Text);
        var textFilter = Normalize(TxtFilterText.Text);

        IEnumerable<QualityOrderListRow> query = _allRows;

        if (!string.IsNullOrWhiteSpace(orderFilter))
        {
            query = query.Where(row => Contains(row.OrderNumber, orderFilter));
        }

        if (!string.IsNullOrWhiteSpace(printVersionFilter))
        {
            query = query.Where(row => Contains(row.PrintVersionNumber, printVersionFilter));
        }

        if (!string.IsNullOrWhiteSpace(textFilter))
        {
            query = query.Where(row =>
                Contains(row.Customer, textFilter) ||
                Contains(row.Title, textFilter) ||
                Contains(row.Machine, textFilter) ||
                Contains(row.Notes, textFilter));
        }

        var rows = query.Take(MaxDisplayedRows).ToList();
        GridOrders.ItemsSource = rows;
        TxtCount.Text = TF("QO.Picker.Count", rows.Count, _allRows.Count);
    }

    private void FilterChanged(object sender, EventArgs e)
    {
        if (_filterTimer is null)
        {
            return;
        }

        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private void BtnRefresh_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ReloadData();
    }

    private void GridOrders_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (GridOrders.SelectedItem is not QualityOrderListRow row)
        {
            return;
        }

        _logger?.AdminAction(
            _targetTransaction,
            "SelectQualityOrderFromPicker",
            _currentUserName,
            $"Target={_targetTransaction}; Order={row.OrderNumber}");

        TransactionRequested?.Invoke($"{_targetTransaction} {row.OrderNumber}");
    }

    private static string Normalize(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length < 2 ? string.Empty : text;
    }

    private static bool Contains(string? value, string filter)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private string T(string key)
    {
        var value = _translate?.Invoke(key) ?? key;
        return IsMissing(value, key) ? key : value;
    }

    private string TF(string key, params object[] args)
    {
        if (_translateFormat is not null)
        {
            var value = _translateFormat(key, args);
            return IsMissing(value, key) ? key : value;
        }

        var pattern = T(key);

        try
        {
            return string.Format(pattern, args);
        }
        catch
        {
            return pattern;
        }
    }

    private static bool IsMissing(string value, string key)
    {
        return string.IsNullOrWhiteSpace(value) ||
               string.Equals(value, key, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }
}
