using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Live;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DMS.Desktop.Views.Mes;

public partial class MesLiveOverviewView : UserControl
{
    private readonly string _configurationRootPath;
    private readonly DmsLogger _logger;
    private readonly string _user;
    private readonly Func<string, string> _translate;
    private readonly Action<string>? _openTransaction;
    private readonly MesDatabaseSettingsService _settingsService = new();
    private readonly DispatcherTimer _timer;

    private MesDatabaseConnectionSettings _settings = new();
    private MesLiveOverviewDataService? _service;
    private IReadOnlyList<MesLiveWorkcenterRecord> _workcenters =
        Array.Empty<MesLiveWorkcenterRecord>();
    private bool _loaded;
    private bool _refreshing;
    private bool _suppressFilterRefresh;
    private bool _workcenterSelectionDirty;

    public MesLiveOverviewView(
        string configurationRootPath,
        DmsLogger logger,
        string user,
        Func<string, string>? translate = null,
        Action<string>? openTransaction = null)
    {
        InitializeComponent();

        _configurationRootPath = configurationRootPath
            ?? throw new ArgumentNullException(nameof(configurationRootPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _user = user ?? string.Empty;
        _translate = translate ?? (key => key);
        _openTransaction = openTransaction;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += Timer_Tick;

        ApplyLocalization();

        Loaded += MesLiveOverviewView_Loaded;
        Unloaded += MesLiveOverviewView_Unloaded;
    }

    private string T(string key, string fallback)
    {
        var translated = _translate(key);
        return string.IsNullOrWhiteSpace(translated)
               || string.Equals(translated, key, StringComparison.Ordinal)
               || string.Equals(translated, $"[[{key}]]", StringComparison.Ordinal)
            ? fallback
            : translated;
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("MES.Title", "MES - Přehled výroby");
        TxtSubtitle.Text = T(
            "MES.Subtitle",
            "Živý read-only přehled pracovišť přímo z analytické vrstvy FASTEC.");

        LblWorkcenter.Text = T("MES.Filter.Workcenter", "Pracoviště");
        LblGroup.Text = T("MES.Filter.Group", "Skupina");
        BtnSelectAllWorkcenters.Content = T("MES.Filter.SelectAllWorkcenters", "Vybrat vše");
        BtnClearWorkcenters.Content = T("MES.Filter.ClearWorkcenters", "Zrušit výběr");

        ColWorkcenter.Header = T("MES.Column.Workcenter", "Pracoviště");
        ColShift.Header = T("MES.Column.Shift", "Směna");
        ColOrder.Header = T("MES.Column.Order", "Zakázka");
        ColItem.Header = T("MES.Column.Item", "Artikl");
        ColState.Header = T("MES.Column.State", "Aktuální stav");
        ColStateDuration.Header = T("MES.Column.StateDuration", "Doba stavu");
        ColUserText.Header = T("MES.Column.UserText", "Uživatelský text");
        ColPlannedPerformance.Header = T("MES.Column.PlannedPerformance", "Plánovaný výkon");
        ColCurrentPerformance.Header = T("MES.Column.CurrentPerformance", "Ø aktuální výkon");
        ColPoTarget.Header = T("MES.Column.PoTarget", "Cíl zakázky");
        ColPoGood.Header = T("MES.Column.PoGood", "Dobré ks zakázka");
        ColShiftGood.Header = T("MES.Column.ShiftGood", "Dobré ks směna");
    }

    private async void MesLiveOverviewView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        try
        {
            var settingsPath = ResolveMesDatabaseSettingsPath(_configurationRootPath);
            _settings = _settingsService.Load(settingsPath);

            if (!_settings.IsEnabled)
            {
                TxtStatus.Text = T("MES.Status.Disabled", "MES SQL připojení je v MESSET vypnuté.");
                return;
            }

            _service = new MesLiveOverviewDataService(_settings);
            await LoadFiltersAsync();
            await RefreshAsync(showDialogOnError: true, logAction: true, showLoadingState: true);
            _timer.Start();
        }
        catch (Exception ex)
        {
            _logger.Error("MES live overview initialization failed.", ex);
            TxtStatus.Text = ex.Message;
            DmsMessage.Show(
                $"{T("MES.Status.LoadFailed", "Načtení živých MES dat selhalo.")}\n\n{ex.Message}",
                "MES",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void MesLiveOverviewView_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
    }

    private async Task LoadFiltersAsync()
    {
        if (_service is null)
        {
            return;
        }

        _suppressFilterRefresh = true;
        try
        {
            var groups = await _service.GetWorkcenterGroupsAsync();
            var allGroups = T("MES.Filter.AllGroups", "Všechny skupiny");

            CmbGroup.ItemsSource = new[] { allGroups }
                .Concat(groups)
                .ToList();
            CmbGroup.SelectedIndex = 0;

            await LoadWorkcentersForSelectedGroupAsync(selectAll: true);
        }
        finally
        {
            _suppressFilterRefresh = false;
        }
    }

    private string? GetSelectedGroup()
    {
        if (CmbGroup.SelectedIndex <= 0)
        {
            return null;
        }

        var value = Convert.ToString(CmbGroup.SelectedItem, CultureInfo.CurrentCulture);
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private async Task LoadWorkcentersForSelectedGroupAsync(bool selectAll)
    {
        if (_service is null)
        {
            return;
        }

        var workcenters = await _service.GetWorkcentersAsync(GetSelectedGroup());
        _workcenters = workcenters.ToList();
        LstWorkcenters.ItemsSource = _workcenters;

        LstWorkcenters.SelectedItems.Clear();
        if (selectAll)
        {
            foreach (var workcenter in _workcenters)
            {
                LstWorkcenters.SelectedItems.Add(workcenter);
            }
        }

        UpdateWorkcenterSelectionText();
        _workcenterSelectionDirty = false;
    }

    private async void CmbGroup_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterRefresh || !_loaded || _service is null)
        {
            return;
        }

        _suppressFilterRefresh = true;
        try
        {
            await LoadWorkcentersForSelectedGroupAsync(selectAll: true);
        }
        catch (Exception ex)
        {
            _logger.Error("MES workcenter group filter load failed.", ex);
            TxtStatus.Text = ex.Message;
            return;
        }
        finally
        {
            _suppressFilterRefresh = false;
        }

        await RefreshAsync(showDialogOnError: false, logAction: true, showLoadingState: false);
    }

    private void BtnWorkcenterPicker_Click(object sender, RoutedEventArgs e)
    {
        PopupWorkcenters.IsOpen = BtnWorkcenterPicker.IsChecked == true;
    }

    private async void PopupWorkcenters_Closed(object? sender, EventArgs e)
    {
        BtnWorkcenterPicker.IsChecked = false;

        if (!_workcenterSelectionDirty
            || _suppressFilterRefresh
            || !_loaded
            || _service is null)
        {
            return;
        }

        _workcenterSelectionDirty = false;
        await RefreshAsync(showDialogOnError: false, logAction: true, showLoadingState: false);
    }

    private void LstWorkcenters_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateWorkcenterSelectionText();

        if (!_suppressFilterRefresh)
        {
            _workcenterSelectionDirty = true;
        }
    }

    private void BtnSelectAllWorkcenters_Click(object sender, RoutedEventArgs e)
    {
        _suppressFilterRefresh = true;
        try
        {
            LstWorkcenters.SelectedItems.Clear();
            foreach (var workcenter in _workcenters)
            {
                LstWorkcenters.SelectedItems.Add(workcenter);
            }

            UpdateWorkcenterSelectionText();
            _workcenterSelectionDirty = true;
        }
        finally
        {
            _suppressFilterRefresh = false;
        }
    }

    private void BtnClearWorkcenters_Click(object sender, RoutedEventArgs e)
    {
        _suppressFilterRefresh = true;
        try
        {
            LstWorkcenters.SelectedItems.Clear();
            UpdateWorkcenterSelectionText();
            _workcenterSelectionDirty = true;
        }
        finally
        {
            _suppressFilterRefresh = false;
        }
    }

    private IReadOnlyList<string> GetSelectedWorkcenterCodes()
    {
        return LstWorkcenters.SelectedItems
            .OfType<MesLiveWorkcenterRecord>()
            .Select(item => item.Code?.Trim() ?? string.Empty)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void UpdateWorkcenterSelectionText()
    {
        var selected = LstWorkcenters.SelectedItems
            .OfType<MesLiveWorkcenterRecord>()
            .ToList();

        if (_workcenters.Count == 0)
        {
            TxtWorkcenterSelection.Text =
                T("MES.Filter.NoWorkcentersAvailable", "Nejsou aktivní pracoviště");
            return;
        }

        if (selected.Count == 0)
        {
            TxtWorkcenterSelection.Text =
                T("MES.Filter.NoWorkcentersSelected", "Žádné pracoviště");
            return;
        }

        if (selected.Count == _workcenters.Count)
        {
            TxtWorkcenterSelection.Text = string.Format(
                CultureInfo.CurrentCulture,
                T("MES.Filter.AllWorkcentersCount", "Všechna aktivní pracoviště ({0})"),
                _workcenters.Count);
            return;
        }

        if (selected.Count == 1)
        {
            TxtWorkcenterSelection.Text = selected[0].DisplayText;
            return;
        }

        var preview = string.Join(
            ", ",
            selected
                .Select(item => item.DisplayDesignation)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(4));

        if (selected.Count > 4)
        {
            preview += ", …";
        }

        TxtWorkcenterSelection.Text = string.Format(
            CultureInfo.CurrentCulture,
            T("MES.Filter.SelectedWorkcenters", "{0} pracovišť: {1}"),
            selected.Count,
            preview);
    }

    private void WorkcenterLink_Click(object sender, RoutedEventArgs e)
    {
        if (_openTransaction is null)
        {
            return;
        }

        if (sender is not Button button
            || button.DataContext is not DisplayRow row
            || string.IsNullOrWhiteSpace(row.WorkcenterCode))
        {
            return;
        }

        _openTransaction($"MESWC {row.WorkcenterCode}");
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        // Do not write one ADMIN log record per second. _refreshing also prevents overlap
        // when the SQL roundtrip itself takes longer than the timer interval.
        await RefreshAsync(
            showDialogOnError: false,
            logAction: false,
            showLoadingState: false);
    }

    private async Task RefreshAsync(
        bool showDialogOnError,
        bool logAction,
        bool showLoadingState = true)
    {
        if (_refreshing || _service is null)
        {
            return;
        }

        _refreshing = true;

        if (showLoadingState)
        {
            TxtStatus.Text = T("MES.Status.Loading", "Načítám aktuální MES data...");
        }

        try
        {
            var selectedWorkcenterCodes = GetSelectedWorkcenterCodes();

            if (_workcenters.Count > 0 && selectedWorkcenterCodes.Count == 0)
            {
                GridOverview.ItemsSource = Array.Empty<DisplayRow>();
                UpdateRefreshInfo(0, DateTime.Now);
                TxtStatus.Text = T(
                    "MES.Status.NoWorkcentersSelected",
                    "Není vybrané žádné pracoviště.");
                return;
            }

            var filter = new MesMachineOverviewFilter
            {
                WorkcenterCodes = selectedWorkcenterCodes,
                WorkcenterGroup = GetSelectedGroup() ?? string.Empty,
                MaxRows = 1000
            };

            var rows = await _service.GetOverviewAsync(filter);
            var now = DateTime.Now;
            var perMinuteUnit = T("MES.Unit.PerMinute", "ks/min");

            var displayRows = rows
                .Select(row => new DisplayRow(row, now, perMinuteUnit))
                .ToList();

            GridOverview.ItemsSource = displayRows;
            UpdateRefreshInfo(displayRows.Count, now);
            TxtStatus.Text = string.Format(
                CultureInfo.CurrentCulture,
                T("MES.Status.Loaded", "Načteno {0} aktivních pracovišť."),
                displayRows.Count);

            if (logAction)
            {
                _logger.AdminAction(
                    "MES",
                    "LoadLiveMachineOverview",
                    _user,
                    $"Workcenters={string.Join("|", filter.WorkcenterCodes)}; Group={filter.WorkcenterGroup}; Rows={displayRows.Count}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("MES live overview refresh failed.", ex);
            TxtStatus.Text = ex.Message;

            if (showDialogOnError)
            {
                DmsMessage.Show(
                    $"{T("MES.Status.LoadFailed", "Načtení živých MES dat selhalo.")}\n\n{ex.Message}",
                    "MES",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateRefreshInfo(int rowCount, DateTime now)
    {
        TxtRows.Text = string.Format(
            CultureInfo.CurrentCulture,
            T("MES.Rows", "Řádků: {0}"),
            rowCount);

        TxtRefresh.Text = string.Format(
            CultureInfo.CurrentCulture,
            T("MES.LastRefresh", "Obnoveno: {0}"),
            now.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture));
    }

    private static string ResolveMesDatabaseSettingsPath(string configurationRootPath)
    {
        var knownNames = new[]
        {
            "mes-database-settings.json",
            "mes-reporting-settings.json",
            "mes-sql-settings.json",
            "mes-database.json",
            "mes-reporting.json"
        };

        foreach (var name in knownNames)
        {
            var candidate = Path.Combine(configurationRootPath, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        if (Directory.Exists(configurationRootPath))
        {
            foreach (var file in Directory.EnumerateFiles(
                         configurationRootPath,
                         "*.json",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file));
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var names = document.RootElement
                        .EnumerateObject()
                        .Select(property => property.Name)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var hasServer = names.Overlaps(new[]
                    {
                        "Server", "SqlServer", "ServerName", "DataSource",
                        "Host", "Address", "ServerAddress"
                    });
                    var hasDatabase = names.Overlaps(new[]
                    {
                        "Database", "DatabaseName", "InitialCatalog", "Catalog"
                    });
                    var hasConnectionString = names.Overlaps(new[]
                    {
                        "ConnectionString", "SqlConnectionString"
                    });
                    var looksMes = Path.GetFileName(file)
                        .Contains("mes", StringComparison.OrdinalIgnoreCase);

                    if (looksMes && ((hasServer && hasDatabase) || hasConnectionString))
                    {
                        return file;
                    }
                }
                catch
                {
                    // Other configuration files are irrelevant here.
                }
            }
        }

        throw new FileNotFoundException(
            "MES SQL settings file was not found under the active DMS configuration root. Open MESSET and save the connection first.");
    }

    private sealed class DisplayRow
    {
        public DisplayRow(MesMachineOverviewRecord source, DateTime now, string perMinuteUnit)
        {
            WorkcenterCode = source.WorkcenterCode;
            WorkcenterName = !string.IsNullOrWhiteSpace(source.WorkcenterCode)
                ? source.WorkcenterCode
                : source.WorkcenterDescription;

            // Keep Designation available internally; the service already sorts by it.
            WorkcenterDesignation = string.IsNullOrWhiteSpace(source.WorkcenterDesignation)
                ? source.WorkcenterCode
                : source.WorkcenterDesignation;
            ShiftName = source.ShiftName;
            OrderCode = source.OrderCode;
            ProductCode = source.ProductCode;
            StateName = source.StateName;
            StateUserText = source.StateUserText;
            StateDurationText = FormatDuration(source.CurrentStateDuration(now));
            PlannedPerformanceText = FormatRate(source.PlannedPerformancePerMinute, perMinuteUnit);
            CurrentPerformanceText = FormatRate(source.CurrentPerformancePerMinute, perMinuteUnit);
            OrderTargetText = FormatAmount(source.OrderTargetAmount);
            OrderGoodText = FormatAmount(source.OrderGoodAmount);
            ShiftGoodText = FormatAmount(source.ShiftGoodAmount);

            var color = ParseFastecColor(source.StateColor)
                        ?? ParseFastecColor(source.StateCategoryColor);

            if (color.HasValue)
            {
                StateBrush = new SolidColorBrush(color.Value);
                StateForegroundBrush = GetReadableForeground(color.Value);
            }
            else
            {
                StateBrush = Brushes.Transparent;
                StateForegroundBrush =
                    Application.Current?.TryFindResource("DmsForegroundBrush") as Brush
                    ?? Brushes.Black;
            }
        }

        public string WorkcenterCode { get; }
        public string WorkcenterName { get; }
        public string WorkcenterDesignation { get; }
        public string ShiftName { get; }
        public string OrderCode { get; }
        public string ProductCode { get; }
        public string StateName { get; }
        public string StateDurationText { get; }
        public string StateUserText { get; }
        public string PlannedPerformanceText { get; }
        public string CurrentPerformanceText { get; }
        public string OrderTargetText { get; }
        public string OrderGoodText { get; }
        public string ShiftGoodText { get; }
        public Brush StateBrush { get; }
        public Brush StateForegroundBrush { get; }

        private static string FormatRate(decimal? value, string unit) => value.HasValue
            ? $"{value.Value:N0} {unit}"
            : string.Empty;

        private static string FormatAmount(decimal? value) => value.HasValue
            ? value.Value.ToString("N0", CultureInfo.CurrentCulture)
            : string.Empty;

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalDays >= 1)
            {
                return $"{(int)duration.TotalDays}d {duration:hh\\:mm\\:ss}";
            }

            return duration.ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture);
        }

        private static Color? ParseFastecColor(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var value = raw.Trim();

            try
            {
                var converted = ColorConverter.ConvertFromString(value);
                if (converted is Color color)
                {
                    return color;
                }
            }
            catch
            {
                // Try numeric formats below.
            }

            var parts = value.Split(
                new[] { ',', ';', ' ' },
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length is 3 or 4
                && parts.All(part => byte.TryParse(
                    part,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out _)))
            {
                var bytes = parts
                    .Select(part => byte.Parse(part, CultureInfo.InvariantCulture))
                    .ToArray();

                return parts.Length == 3
                    ? Color.FromRgb(bytes[0], bytes[1], bytes[2])
                    : Color.FromArgb(bytes[0], bytes[1], bytes[2], bytes[3]);
            }

            return null;
        }

        private static Brush GetReadableForeground(Color background)
        {
            var luminance =
                (0.2126 * background.R)
                + (0.7152 * background.G)
                + (0.0722 * background.B);

            return luminance >= 145
                ? Brushes.Black
                : Brushes.White;
        }
    }
}
