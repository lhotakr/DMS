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
        Func<string, string>? translate = null)
    {
        InitializeComponent();

        _configurationRootPath = configurationRootPath ?? throw new ArgumentNullException(nameof(configurationRootPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _user = user ?? string.Empty;
        _translate = translate ?? (key => key);

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _timer.Tick += Timer_Tick;

        ApplyLocalization();

        // Set the default only after the timer has been created.
        // Setting IsChecked=True directly in XAML raises Checked during InitializeComponent(),
        // which previously called UpdateTimerState() before _timer existed.
        ChkAutoRefresh.IsChecked = true;

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
        TxtSubtitle.Text = T("MES.Subtitle", "Živý read-only přehled pracovišť přímo z analytické vrstvy FASTEC.");
        LblWorkcenter.Text = T("MES.Filter.Workcenter", "Pracoviště");
        LblShift.Text = T("MES.Filter.Shift", "Směna");
        BtnSelectAllWorkcenters.Content = T("MES.Filter.SelectAllWorkcenters", "Vybrat vše");
        BtnClearWorkcenters.Content = T("MES.Filter.ClearWorkcenters", "Zrušit výběr");
        BtnRefresh.Content = T("MES.Button.Refresh", "Obnovit");
        ChkAutoRefresh.Content = T("MES.AutoRefresh", "Automaticky každých 30 s");

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
                BtnRefresh.IsEnabled = false;
                ChkAutoRefresh.IsChecked = false;
                return;
            }

            _service = new MesLiveOverviewDataService(_settings);
            await LoadFiltersAsync();
            await RefreshAsync(showDialogOnError: true);
            UpdateTimerState();
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
            var now = DateTime.Now;
            var workcenters = await _service.GetWorkcentersAsync();
            var shifts = await _service.GetShiftsAsync(now.AddDays(-1), now.AddDays(1));

            _workcenters = workcenters.ToList();
            LstWorkcenters.ItemsSource = _workcenters;

            // Výchozí stav odpovídá původnímu filtru "Všechna pracoviště".
            // SelectedItems lze plnit přímo i před vygenerováním ListBoxItem kontejnerů.
            LstWorkcenters.SelectedItems.Clear();
            foreach (var workcenter in _workcenters)
            {
                LstWorkcenters.SelectedItems.Add(workcenter);
            }

            UpdateWorkcenterSelectionText();
            _workcenterSelectionDirty = false;

            var allShifts = new MesShiftRecord
            {
                Id = Guid.Empty,
                Name = T("MES.Filter.AllShifts", "Všechny směny"),
                StartTime = DateTime.MinValue,
                EndTime = DateTime.MaxValue
            };

            var shiftItems = new[] { allShifts }.Concat(shifts).ToList();
            CmbShift.ItemsSource = shiftItems;

            var currentShift = shifts.FirstOrDefault(item => item.IsCurrent(now));
            CmbShift.SelectedItem = currentShift ?? allShifts;
        }
        finally
        {
            _suppressFilterRefresh = false;
        }
    }

    private void BtnWorkcenterPicker_Click(object sender, RoutedEventArgs e)
    {
        PopupWorkcenters.IsOpen = BtnWorkcenterPicker.IsChecked == true;
    }

    private async void PopupWorkcenters_Closed(object? sender, EventArgs e)
    {
        BtnWorkcenterPicker.IsChecked = false;

        if (!_workcenterSelectionDirty ||
            _suppressFilterRefresh ||
            !_loaded ||
            _service is null)
        {
            return;
        }

        _workcenterSelectionDirty = false;
        await RefreshAsync(showDialogOnError: false);
    }

    private void LstWorkcenters_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
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
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
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
                T("MES.Filter.NoWorkcentersAvailable", "Nejsou dostupná pracoviště");
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
                T("MES.Filter.AllWorkcentersCount", "Všechna pracoviště ({0})"),
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
                .Select(item => item.Code)
                .Where(code => !string.IsNullOrWhiteSpace(code))
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

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync(showDialogOnError: true);
    }

    private async void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterRefresh || !_loaded || _service is null)
        {
            return;
        }

        await RefreshAsync(showDialogOnError: false);
    }

    private void ChkAutoRefresh_Changed(object sender, RoutedEventArgs e)
    {
        UpdateTimerState();
    }

    private void UpdateTimerState()
    {
        if (ChkAutoRefresh.IsChecked == true && _service is not null && _settings.IsEnabled)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        await RefreshAsync(showDialogOnError: false);
    }

    private async Task RefreshAsync(bool showDialogOnError)
    {
        if (_refreshing || _service is null)
        {
            return;
        }

        _refreshing = true;
        BtnRefresh.IsEnabled = false;
        TxtStatus.Text = T("MES.Status.Loading", "Načítám aktuální MES data...");

        try
        {
            var selectedWorkcenterCodes = GetSelectedWorkcenterCodes();
            var shift = CmbShift.SelectedItem as MesShiftRecord;

            if (_workcenters.Count > 0 && selectedWorkcenterCodes.Count == 0)
            {
                GridOverview.ItemsSource = Array.Empty<DisplayRow>();
                var emptyNow = DateTime.Now;

                TxtRows.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    T("MES.Rows", "Řádků: {0}"),
                    0);
                TxtRefresh.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    T("MES.LastRefresh", "Obnoveno: {0}"),
                    emptyNow.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture));
                TxtStatus.Text = T(
                    "MES.Status.NoWorkcentersSelected",
                    "Není vybrané žádné pracoviště.");

                return;
            }

            var filter = new MesMachineOverviewFilter
            {
                WorkcenterCodes = selectedWorkcenterCodes,
                ShiftId = shift is null || shift.Id == Guid.Empty ? null : shift.Id,
                MaxRows = 1000
            };

            var rows = await _service.GetOverviewAsync(filter);
            var now = DateTime.Now;
            var perMinuteUnit = T("MES.Unit.PerMinute", "ks/min");
            var displayRows = rows.Select(row => new DisplayRow(row, now, perMinuteUnit)).ToList();

            GridOverview.ItemsSource = displayRows;
            TxtRows.Text = string.Format(
                CultureInfo.CurrentCulture,
                T("MES.Rows", "Řádků: {0}"),
                displayRows.Count);
            TxtRefresh.Text = string.Format(
                CultureInfo.CurrentCulture,
                T("MES.LastRefresh", "Obnoveno: {0}"),
                now.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture));
            TxtStatus.Text = string.Format(
                CultureInfo.CurrentCulture,
                T("MES.Status.Loaded", "Načteno {0} pracovišť."),
                displayRows.Count);

            _logger.AdminAction(
                "MES",
                "LoadLiveMachineOverview",
                _user,
                $"Workcenters={string.Join("|", filter.WorkcenterCodes)}; Shift={filter.ShiftId}; Rows={displayRows.Count}");
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
            BtnRefresh.IsEnabled = _settings.IsEnabled;
            _refreshing = false;
        }
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
            foreach (var file in Directory.EnumerateFiles(configurationRootPath, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file));
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var names = document.RootElement.EnumerateObject()
                        .Select(property => property.Name)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var hasServer = names.Overlaps(new[] { "Server", "SqlServer", "ServerName", "DataSource", "Host", "Address", "ServerAddress" });
                    var hasDatabase = names.Overlaps(new[] { "Database", "DatabaseName", "InitialCatalog", "Catalog" });
                    var hasConnectionString = names.Overlaps(new[] { "ConnectionString", "SqlConnectionString" });
                    var looksMes = Path.GetFileName(file).Contains("mes", StringComparison.OrdinalIgnoreCase);

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
                StateForegroundBrush = Application.Current?.TryFindResource("DmsForegroundBrush") as Brush ?? Brushes.Black;
            }
        }

        public string WorkcenterCode { get; }
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

            var parts = value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is 3 or 4 && parts.All(part => byte.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            {
                var bytes = parts.Select(part => byte.Parse(part, CultureInfo.InvariantCulture)).ToArray();
                return parts.Length == 3
                    ? Color.FromRgb(bytes[0], bytes[1], bytes[2])
                    : Color.FromArgb(bytes[0], bytes[1], bytes[2], bytes[3]);
            }

            return null;
        }

        private static Brush GetReadableForeground(Color background)
        {
            var luminance = (0.2126 * background.R) + (0.7152 * background.G) + (0.0722 * background.B);
            return luminance >= 145 ? Brushes.Black : Brushes.White;
        }
    }
}
