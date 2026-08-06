using DMS.Core.Quality;
using DMS.Desktop.Logging;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DMS.Desktop.Views.QualityOrders;

public partial class QualityOrderListView : UserControl
{
    private const int MaxDisplayedRows = 500;

    private readonly QualityOrderMaintenanceService _service;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;
    private DispatcherTimer? _filterTimer;
    private bool _filterReady;
    private bool _isApplyingFilter;
    private List<QualityOrderListRow> _allRows = new();
    private List<QO05FilterRow> _filterRows = new();

    public event Action<string>? TransactionRequested;

    public QualityOrderListView(
        string dmsRootPath,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

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
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer?.Stop();
            ApplyFilterSafely();
        };

        ApplyLocalization();
        ReloadData();
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("QO05.Title");
        TxtHint.Text = T("QO05.Hint");
        TxtFilterHelp.Text = T("QO05.FilterHelp");

        LblFilterOrder.Text = T("QO05.Filter.Order");
        LblFilterPrintVersion.Text = T("QO05.Filter.PrintVersion");
        LblFilterSap.Text = T("QO05.Filter.Sap");
        LblFilterText.Text = T("QO05.Filter.Text");
        LblCreatedFrom.Text = T("QO05.Filter.CreatedFrom");
        LblCreatedTo.Text = T("QO05.Filter.CreatedTo");

        TxtFilterOrder.ToolTip = T("QO05.Filter.Order.ToolTip");
        TxtFilterPrintVersion.ToolTip = T("QO05.Filter.PrintVersion.ToolTip");
        TxtFilterSap.ToolTip = T("QO05.Filter.Sap.ToolTip");
        TxtFilterText.ToolTip = T("QO05.Filter.Text.ToolTip");
        TxtCreatedFrom.ToolTip = T("QO05.Filter.Date.ToolTip");
        TxtCreatedTo.ToolTip = T("QO05.Filter.Date.ToolTip");

        LblStatusFilter.Text = T("QO05.Filter.ScheduleStatus");
        RbStatusAll.Content = T("QO05.Filter.StatusAll");
        RbStatusUnplanned.Content = T("QO05.Filter.StatusUnplanned");
        RbStatusScheduled.Content = T("QO05.Filter.StatusScheduled");
        RbStatusFinished.Content = T("QO05.Filter.StatusFinished");

        LblReleaseFilter.Text = T("QO05.Filter.ReleaseState");
        RbReleaseAll.Content = T("QO05.Filter.ReleaseAll");
        RbReleaseBlocked.Content = T("QO05.Filter.ReleaseBlocked");
        RbReleaseReleased.Content = T("QO05.Filter.ReleaseReleased");

        BtnRefresh.Content = T("QO.Action.Reload");

        ColScheduleLight.Header = "●";
        ColScheduleLight.HeaderStyle = (Style)FindResource("QO05ColumnHeaderStyle");
        ColReleaseIcon.Header = "✓";
        ColReleaseIcon.HeaderStyle = (Style)FindResource("QO05ColumnHeaderStyle");
        ColOrder.Header = T("QO.Col.Order");
        ColPrintVersion.Header = T("QO.Col.PrintVersion");
        ColSap.Header = T("QO.Col.SapId");
        ColCustomer.Header = T("QO.Col.Customer");
        ColArticleTitle.Header = T("QO.Col.ArticleTitle");
        ColMachine.Header = T("QO.Col.Machine");
        ColStart.Header = T("QO.Col.Start");
        ColEnd.Header = T("QO.Col.End");
        ColCreatedAt.Header = T("QO.Col.CreatedAt");
        ColOrdered.Header = T("QO.Col.Ordered");
        ColProduced.Header = T("QO.Col.Produced");
        ColColor.Header = T("QO.Col.Color");
        ColQualityClass.Header = T("QO.Col.QualityClass");
        ColLoreal.Header = T("QO.Col.Loreal");
        ColRelease.Header = T("QO.Col.Released");
        ColStatus.Header = T("QO.Col.Status");
        ColOpenTasks.Header = T("QO.Col.OpenTasks");
        ColNotes.Header = T("QO.Col.Notes");

        ApplyColumnLayout();
    }

    private void ApplyColumnLayout()
    {
        if (GridOrders is null)
        {
            return;
        }

        // QO05 contains many business columns. Keep real widths and let the grid scroll
        // horizontally instead of squeezing headers/cells into unreadable two-letter columns.
        GridOrders.MinColumnWidth = 48;
        ScrollViewer.SetHorizontalScrollBarVisibility(GridOrders, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(GridOrders, ScrollBarVisibility.Auto);

        SetFixedColumn(ColScheduleLight, 44);
        SetFixedColumn(ColReleaseIcon, 48);
        SetColumn(ColOrder, 90, 90);
        SetColumn(ColPrintVersion, 170, 160);
        SetColumn(ColSap, 110, 105);
        SetColumn(ColCustomer, 145, 130);
        SetColumn(ColArticleTitle, 220, 180);
        SetColumn(ColMachine, 130, 110);
        SetColumn(ColStart, 95, 90);
        SetColumn(ColEnd, 95, 90);
        SetColumn(ColCreatedAt, 135, 125);
        SetColumn(ColOrdered, 95, 90);
        SetColumn(ColProduced, 95, 90);
        SetColumn(ColColor, 135, 115);
        SetColumn(ColQualityClass, 85, 80);
        SetColumn(ColLoreal, 80, 75);
        SetColumn(ColRelease, 100, 95);
        SetColumn(ColStatus, 120, 110);
        SetColumn(ColOpenTasks, 300, 220);
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
        // Build rows once, localize them once, then keep a pre-normalized filter cache.
        // Filtering itself no longer sorts/materializes the complete result list on every keystroke.
        _allRows = _service.BuildOrderListRows()
            .OrderByDescending(row => row.CreatedAtDate ?? DateTime.MinValue)
            .ThenByDescending(row => row.OrderNumber)
            .ToList();

        LocalizeRows(_allRows);
        _filterRows = _allRows
            .Select(QO05FilterRow.Create)
            .ToList();

        _filterReady = true;

        _logger?.AdminAction(
            "QO05",
            "LoadQualityOrderOverview",
            _currentUserName,
            $"Count={_allRows.Count}; DisplayLimit={MaxDisplayedRows}; Sort=CreatedAtDescending; FilterCache=True");

        ApplyFilterSafely();
    }

    private void LocalizeRows(IEnumerable<QualityOrderListRow> rows)
    {
        foreach (var row in rows)
        {
            row.ReleasedText = T($"QO.Release.{row.ReleaseStatusCode}");
            row.ScheduleStatusText = T($"QO.Status.{row.ScheduleStatusCode}");
            row.LorealText = row.Source.Loreal ? T("Common.Yes") : T("Common.No");
        }
    }

    private void ApplyFilterSafely()
    {
        if (_isApplyingFilter)
        {
            return;
        }

        try
        {
            _isApplyingFilter = true;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _logger?.Error($"QO05 filter failed: {ex.Message}");

            if (TxtDateWarning is not null)
            {
                TxtDateWarning.Text = TF("QO05.Filter.Error", ex.Message);
                TxtDateWarning.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            _isApplyingFilter = false;
        }
    }

    private void ApplyFilter()
    {
        if (GridOrders is null || TxtCount is null)
        {
            return;
        }

        var orderFilter = NormalizeFilter(TxtFilterOrder.Text);
        var printVersionFilter = NormalizeFilter(TxtFilterPrintVersion.Text);
        var sapFilter = NormalizeFilter(TxtFilterSap.Text);
        var textFilter = NormalizeFilter(TxtFilterText.Text);
        var statusFilter = GetSelectedStatusFilter();
        var releaseFilter = GetSelectedReleaseFilter();

        var fromValid = TryReadDateFilter(TxtCreatedFrom.Text, endOfDay: false, out var createdFrom);
        var toValid = TryReadDateFilter(TxtCreatedTo.Text, endOfDay: true, out var createdTo);
        UpdateDateWarning(fromValid, toValid);

        var displayedRows = new List<QualityOrderListRow>(MaxDisplayedRows);
        var filteredCount = 0;

        foreach (var cache in _filterRows)
        {
            if (!cache.Matches(
                    statusFilter,
                    releaseFilter,
                    createdFrom,
                    createdTo,
                    orderFilter,
                    printVersionFilter,
                    sapFilter,
                    textFilter))
            {
                continue;
            }

            filteredCount++;

            if (displayedRows.Count < MaxDisplayedRows)
            {
                displayedRows.Add(cache.Row);
            }
        }

        GridOrders.ItemsSource = displayedRows;
        TxtCount.Text = TF("QO05.CountLimited", displayedRows.Count, filteredCount, _allRows.Count, MaxDisplayedRows);
    }

    private string GetSelectedStatusFilter()
    {
        if (RbStatusUnplanned.IsChecked == true)
        {
            return "Unplanned";
        }

        if (RbStatusScheduled.IsChecked == true)
        {
            return "Scheduled";
        }

        if (RbStatusFinished.IsChecked == true)
        {
            return "Finished";
        }

        return string.Empty;
    }


    private string GetSelectedReleaseFilter()
    {
        if (RbReleaseBlocked.IsChecked == true)
        {
            return "Blocked";
        }

        if (RbReleaseReleased.IsChecked == true)
        {
            return "Released";
        }

        return string.Empty;
    }

    private void FilterChanged(object sender, EventArgs e)
    {
        if (!_filterReady)
        {
            return;
        }

        if (_filterTimer is null)
        {
            ApplyFilterSafely();
            return;
        }

        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
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
            "QO05",
            "OpenQualityOrderEditFromOverview",
            _currentUserName,
            $"Order={row.OrderNumber}; PrintVersion={row.PrintVersionNumber}");

        TransactionRequested?.Invoke($"QO02 {row.OrderNumber}");
    }

    private static string NormalizeFilter(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length < 2
            ? string.Empty
            : text.ToUpperInvariant();
    }

    private static string SearchValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
    }

    private bool TryReadDateFilter(string? rawValue, bool endOfDay, out DateTime? result)
    {
        result = null;
        var value = rawValue?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var formats = new[]
        {
            "d.M.yyyy",
            "dd.MM.yyyy",
            "d. M. yyyy",
            "dd. MM. yyyy",
            "yyyy-MM-dd"
        };

        if (!DateTime.TryParseExact(
                value,
                formats,
                CultureInfo.GetCultureInfo("cs-CZ"),
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        result = endOfDay
            ? parsed.Date.AddDays(1).AddTicks(-1)
            : parsed.Date;

        return true;
    }

    private void UpdateDateWarning(bool fromValid, bool toValid)
    {
        if (fromValid && toValid)
        {
            TxtDateWarning.Visibility = Visibility.Collapsed;
            TxtDateWarning.Text = string.Empty;
            return;
        }

        TxtDateWarning.Text = T("QO05.Filter.InvalidDate");
        TxtDateWarning.Visibility = Visibility.Visible;
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
            return IsMissing(value, key) ? FallbackFormat(key, args) : value;
        }

        var pattern = T(key);

        try
        {
            return string.Format(pattern, args);
        }
        catch
        {
            return FallbackFormat(key, args);
        }
    }

    private static string FallbackFormat(string key, object[] args)
    {
        if (string.Equals(key, "QO05.CountLimited", StringComparison.OrdinalIgnoreCase) && args.Length >= 4)
        {
            return $"Displayed {args[0]} of {args[1]} filtered orders. Total: {args[2]}. Limit: {args[3]}.";
        }

        return key;
    }

    private static bool IsMissing(string value, string key)
    {
        return string.IsNullOrWhiteSpace(value) ||
               string.Equals(value, key, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class QO05FilterRow
    {
        private QO05FilterRow(
            QualityOrderListRow row,
            string orderNumber,
            string printVersionNumber,
            string sapMaterialNumber,
            string text)
        {
            Row = row;
            OrderNumber = orderNumber;
            PrintVersionNumber = printVersionNumber;
            SapMaterialNumber = sapMaterialNumber;
            Text = text;
        }

        public QualityOrderListRow Row { get; }

        private string OrderNumber { get; }

        private string PrintVersionNumber { get; }

        private string SapMaterialNumber { get; }

        private string Text { get; }

        public static QO05FilterRow Create(QualityOrderListRow row)
        {
            var text = string.Join(
                " ",
                SearchValue(row.Customer),
                SearchValue(row.Title),
                SearchValue(row.ArticleTitle),
                SearchValue(row.OpenTasksText),
                SearchValue(row.Machine),
                SearchValue(row.ColorType),
                SearchValue(row.Notes));

            return new QO05FilterRow(
                row,
                SearchValue(row.OrderNumber),
                SearchValue(row.PrintVersionNumber),
                SearchValue(row.SapMaterialNumber),
                text);
        }

        public bool Matches(
            string statusFilter,
            string releaseFilter,
            DateTime? createdFrom,
            DateTime? createdTo,
            string orderFilter,
            string printVersionFilter,
            string sapFilter,
            string textFilter)
        {
            if (!string.IsNullOrWhiteSpace(statusFilter) &&
                !string.Equals(Row.ScheduleStatusCode, statusFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(releaseFilter) &&
                !string.Equals(Row.ReleaseStatusCode, releaseFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (createdFrom.HasValue &&
                (!Row.CreatedAtDate.HasValue || Row.CreatedAtDate.Value < createdFrom.Value))
            {
                return false;
            }

            if (createdTo.HasValue &&
                (!Row.CreatedAtDate.HasValue || Row.CreatedAtDate.Value > createdTo.Value))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(orderFilter) &&
                !OrderNumber.Contains(orderFilter, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(printVersionFilter) &&
                !PrintVersionNumber.Contains(printVersionFilter, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(sapFilter) &&
                !SapMaterialNumber.Contains(sapFilter, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(textFilter) &&
                !Text.Contains(textFilter, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }
    }
}
