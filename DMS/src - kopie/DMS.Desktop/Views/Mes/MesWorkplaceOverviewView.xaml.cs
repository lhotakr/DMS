using DMS.Desktop.Logging;
using DMS.Desktop.Models;
using DMS.Integration.Mes.Models;
using DMS.Integration.Mes.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace DMS.Desktop.Views.Mes;

public partial class MesWorkplaceOverviewView : UserControl
{
    private readonly string _devicesFilePath;
    private readonly string _settingsPath;
    private readonly string _snapshotPath;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserDisplayName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;
    private readonly MesDeviceFileService _deviceFileService = new();
    private readonly MesCommunicationSettingsService _settingsService = new();
    private readonly MesProbeService _probeService = new();
    private readonly DispatcherTimer _timer = new();
    private readonly ObservableCollection<MesDeviceStatusRow> _rows = new();
    private ICollectionView? _view;
    private CancellationTokenSource? _checkCancellation;
    private MesCommunicationSettings _settings = new();
    private bool _isChecking;
    private bool _initialCheckStarted;
    private DateTime? _lastCheckAt;

    public MesWorkplaceOverviewView()
        : this(
            Path.Combine(AppContext.BaseDirectory, "Config", "devices.txt"),
            Path.Combine(AppContext.BaseDirectory, "Config", "mes-communication-settings.json"),
            Path.Combine(AppContext.BaseDirectory, "Data", "MES", "mes05-last-snapshot.json"),
            null,
            Environment.UserName)
    {
    }

    public MesWorkplaceOverviewView(
        string devicesFilePath,
        string settingsPath,
        string snapshotPath,
        DmsLogger? logger,
        string currentUserDisplayName,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _devicesFilePath = devicesFilePath;
        _settingsPath = settingsPath;
        _snapshotPath = snapshotPath;
        _logger = logger;
        _currentUserDisplayName = currentUserDisplayName;
        _translate = translate;
        _translateFormat = translateFormat;

        _timer.Tick += async (_, _) => await RunCheckAsync(allowWhenMonitoringDisabled: false);

        ApplyLocalization();
        ConfigureGrid();
        LoadSettings();
        LoadDeviceFile();

        Loaded += MesWorkplaceOverviewView_Loaded;
        Unloaded += MesWorkplaceOverviewView_Unloaded;
    }


    private async void MesWorkplaceOverviewView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_initialCheckStarted)
        {
            return;
        }

        _initialCheckStarted = true;

        if (!_settings.IsMonitoringEnabled)
        {
            TxtStatusLine.Text = T(
                "MES05.Status.Disabled",
                "Monitoring je v MES00 vypnutý.");
            return;
        }

        // MES05 is a live overview. Run the first real ICMP check automatically
        // so the screen never remains indefinitely in the misleading Unknown state.
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        await RunCheckAsync(allowWhenMonitoringDisabled: false);
    }

    private void MesWorkplaceOverviewView_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        _timer.Stop();
        _checkCancellation?.Cancel();
        _initialCheckStarted = false;
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("MES05.Title", "MES05 - Soupis pracovišť");
        TxtSubtitle.Text = T("MES05.Subtitle", "Náhled dostupnosti MES serverů, terminálů, monitorů a strojů podle devices.txt. Nastavení komunikace se řídí transakcí MES00.");
        TxtCategoryLabel.Text = T("MES05.Filter.Category", "Kategorie");
        TxtStateLabel.Text = T("MES05.Filter.State", "Stav");
        TxtSearchLabel.Text = T("MES05.Filter.Search", "Hledat");
        BtnReloadFile.Content = T("MES05.Button.ReloadFile", "Načíst TXT");
        BtnReloadSettings.Content = T("MES05.Button.ReloadSettings", "Načíst nastavení");
        BtnCheckNow.Content = T("MES05.Button.CheckNow", "Zkontrolovat teď");
        BtnAutoRefresh.Content = T("MES05.Button.AutoRefresh", "Auto kontrola");
        BtnStop.Content = T("MES05.Button.Stop", "Stop");
        BtnCopy.Content = T("MES05.Button.Copy", "Kopírovat");

        if (CmbState.Items.Count > 0 && CmbState.Items[0] is ComboBoxItem allStateItem)
        {
            allStateItem.Content = T("MES05.Filter.All", "Vše");
        }

        ColStatus.Header = T("MES05.Column.Status", "Stav");
        ColCategory.Header = T("MES05.Column.Category", "Typ");
        ColAddress.Header = T("MES05.Column.Address", "Adresa");
        ColName.Header = T("MES05.Column.Name", "Název");
        ColResponseTime.Header = T("MES05.Column.ResponseTime", "Odezva");
        ColCheckedAt.Header = T("MES05.Column.CheckedAt", "Kontrola");
        ColFailure.Header = T("MES05.Column.Failure", "Chyba");
        ColNote.Header = T("MES05.Column.Note", "Poznámka");
    }

    private void ConfigureGrid()
    {
        GridDevices.ItemsSource = _rows;
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterRow;
    }

    private void LoadSettings()
    {
        _settings = _settingsService.Load(_settingsPath);
        _timer.Interval = TimeSpan.FromSeconds(_settings.AutoRefreshSeconds);
        RefreshFileText();
    }

    private void LoadDeviceFile()
    {
        try
        {
            _deviceFileService.EnsureTemplateFile(_devicesFilePath);
            var devices = _deviceFileService.Load(_devicesFilePath);

            _rows.Clear();
            foreach (var device in devices)
            {
                var row = new MesDeviceStatusRow();
                row.ApplyDevice(device);
                _rows.Add(row);
            }

            ReloadCategoryFilter();
            RefreshFileText();
            RefreshSummary();

            _logger?.AdminAction(
                "MES05",
                "LoadMesDeviceFile",
                _currentUserDisplayName,
                $"File={_devicesFilePath}; Devices={_rows.Count}");
        }
        catch (Exception ex)
        {
            TxtStatusLine.Text = TF("MES05.Status.LoadFailed", "Nepodařilo se načíst devices.txt: {0}", ex.Message);
            _logger?.AdminAction(
                "MES05",
                "LoadMesDeviceFileFailed",
                _currentUserDisplayName,
                $"File={_devicesFilePath}; Error={ex.Message}");
        }
    }

    private void ReloadCategoryFilter()
    {
        var categories = _rows
            .Select(row => row.Category)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();

        categories.Insert(0, T("MES05.Filter.All", "Vše"));
        CmbCategory.ItemsSource = categories;
        CmbCategory.SelectedIndex = 0;
    }

    private void RefreshFileText()
    {
        TxtFilePath.Text = TF("MES05.FilePath", "Seznam zařízení: {0}", _devicesFilePath);
        var settingsText = TF(
            "MES05.SettingsPath",
            "Nastavení MES00: {0} | timeout {1} ms | paralelně {2} | auto {3} s | odemknutí stroje {4}",
            _settingsPath,
            _settings.PingTimeoutMs,
            _settings.MaxParallelism,
            _settings.AutoRefreshSeconds,
            _settings.EnableMachineUnlockSignal ? "ON" : "OFF");

        TxtSettingsPath.Text =
            settingsText +
            $" | monitoring {(_settings.IsMonitoringEnabled ? "ON" : "OFF")}";
        TxtSnapshotPath.Text = TF("MES05.SnapshotPath", "Poslední snímek: {0}", _snapshotPath);
    }

    private async Task RunCheckAsync(bool allowWhenMonitoringDisabled)
    {
        if (_isChecking)
        {
            return;
        }

        if (!_settings.IsMonitoringEnabled && !allowWhenMonitoringDisabled)
        {
            TxtStatusLine.Text = T(
                "MES05.Status.Disabled",
                "Automatický monitoring je v MES00 vypnutý. Ruční tlačítko Zkontrolovat teď funguje i bez něj.");
            return;
        }

        _isChecking = true;
        BtnCheckNow.IsEnabled = false;
        BtnReloadFile.IsEnabled = false;
        BtnReloadSettings.IsEnabled = false;
        TxtStatusLine.Text = allowWhenMonitoringDisabled && !_settings.IsMonitoringEnabled
            ? T("MES05.Status.CheckingManual", "Probíhá ruční kontrola dostupnosti (automatický monitoring je vypnutý)...")
            : T("MES05.Status.Checking", "Probíhá kontrola dostupnosti...");

        _checkCancellation?.Cancel();
        _checkCancellation?.Dispose();
        _checkCancellation = new CancellationTokenSource();

        try
        {
            var devices = _rows.Select(row => new MesDevice
            {
                Address = row.Address,
                Category = row.Category,
                Name = row.Name,
                Note = row.Note,
                SourceLineNumber = row.SourceLineNumber
            }).ToList();

            var results = await _probeService.ProbeAsync(
                devices,
                TimeSpan.FromMilliseconds(_settings.PingTimeoutMs),
                _settings.MaxParallelism,
                _checkCancellation.Token);

            var resultByKey = results
                .GroupBy(
                    result => result.Device.Key,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var row in _rows)
            {
                var key = string.IsNullOrWhiteSpace(row.Address) ? row.Name : row.Address;
                if (resultByKey.TryGetValue(key, out var result))
                {
                    row.ApplyResult(result);
                }
            }

            _lastCheckAt = DateTime.Now;
            SaveSnapshot();
            RefreshSummary();
            _view?.Refresh();

            var online = _rows.Count(row => row.IsOnline);
            var offline = _rows.Count - online;

            TxtStatusLine.Text = TF(
                "MES05.Status.Checked",
                "Kontrola dokončena: online {0}, offline/chyba {1}, čas {2}",
                online,
                offline,
                _lastCheckAt.Value.ToString("dd.MM.yyyy HH:mm:ss"));

            _logger?.AdminAction(
                "MES05",
                "RefreshMesWorkplaceOverview",
                _currentUserDisplayName,
                $"Devices={_rows.Count}; Online={online}; OfflineOrError={offline}; ManualOverride={allowWhenMonitoringDisabled}; Snapshot={_snapshotPath}");
        }
        catch (OperationCanceledException)
        {
            TxtStatusLine.Text = T("MES05.Status.Cancelled", "Kontrola byla zastavena.");
        }
        catch (Exception ex)
        {
            TxtStatusLine.Text = TF("MES05.Status.CheckFailed", "Kontrola selhala: {0}", ex.Message);
            _logger?.AdminAction("MES05", "RefreshMesWorkplaceOverviewFailed", _currentUserDisplayName, ex.Message);
        }
        finally
        {
            _isChecking = false;
            BtnCheckNow.IsEnabled = true;
            BtnReloadFile.IsEnabled = true;
            BtnReloadSettings.IsEnabled = true;
        }
    }

    private void SaveSnapshot()
    {
        try
        {
            var snapshot = new MesMonitorSnapshot
            {
                CreatedAt = DateTime.Now,
                TotalDevices = _rows.Count,
                OnlineDevices = _rows.Count(row => row.IsOnline),
                OfflineDevices = _rows.Count(row => string.Equals(row.State, "Offline", StringComparison.OrdinalIgnoreCase)),
                UnknownDevices = _rows.Count(row => !row.IsOnline && !string.Equals(row.State, "Offline", StringComparison.OrdinalIgnoreCase)),
                Rows = _rows.Select(row => row.ToSnapshotRow()).ToList()
            };

            var directory = Path.GetDirectoryName(_snapshotPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                _snapshotPath,
                JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _logger?.AdminAction("MES05", "SaveMesMonitorSnapshotFailed", _currentUserDisplayName, $"Snapshot={_snapshotPath}; Error={ex.Message}");
        }
    }

    private void RefreshSummary()
    {
        var total = _rows.Count;
        var online = _rows.Count(row => row.IsOnline);
        var offline = _rows.Count(row => string.Equals(row.State, "Offline", StringComparison.OrdinalIgnoreCase));
        var error = _rows.Count(row => string.Equals(row.State, "Error", StringComparison.OrdinalIgnoreCase));
        var unknown = total - online - offline - error;

        var time = _lastCheckAt.HasValue
            ? _lastCheckAt.Value.ToString("dd.MM.yyyy HH:mm:ss")
            : T("MES05.Summary.NotChecked", "zatím neproběhla");

        TxtSummary.Text = TF(
            "MES05.Summary",
            "Zařízení: {0} | Online: {1} | Offline: {2} | Chyba: {3} | Neznámé: {4} | Poslední kontrola: {5}",
            total,
            online,
            offline,
            error,
            unknown,
            time);
    }

    private bool FilterRow(object item)
    {
        if (item is not MesDeviceStatusRow row)
        {
            return false;
        }

        var category = CmbCategory.SelectedItem as string;
        var allText = T("MES05.Filter.All", "Vše");

        if (!string.IsNullOrWhiteSpace(category)
            && !string.Equals(category, allText, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(row.Category, category, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var state = GetSelectedState();
        if (!string.IsNullOrWhiteSpace(state)
            && !string.Equals(state, allText, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(row.State, state, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = TxtSearch.Text?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(query) && !row.SearchText.Contains(query))
        {
            return false;
        }

        return true;
    }

    private string GetSelectedState()
    {
        if (CmbState.SelectedIndex <= 0)
        {
            return T("MES05.Filter.All", "Vše");
        }

        if (CmbState.SelectedItem is ComboBoxItem item)
        {
            return item.Content?.ToString() ?? string.Empty;
        }

        return CmbState.SelectedItem?.ToString() ?? string.Empty;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => _view?.Refresh();

    private void BtnReloadFile_Click(object sender, RoutedEventArgs e) => LoadDeviceFile();

    private void BtnReloadSettings_Click(object sender, RoutedEventArgs e)
    {
        LoadSettings();
        TxtStatusLine.Text = T("MES05.Status.SettingsReloaded", "Nastavení MES00 bylo znovu načteno.");
    }

    private async void BtnCheckNow_Click(object sender, RoutedEventArgs e)
    {
        // Manual diagnostics must remain available even when automatic
        // monitoring is disabled in MES00.
        await RunCheckAsync(allowWhenMonitoringDisabled: true);
    }

    private void BtnAutoRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_timer.IsEnabled)
        {
            _timer.Stop();
            BtnAutoRefresh.Content = T("MES05.Button.AutoRefresh", "Auto kontrola");
            TxtStatusLine.Text = T("MES05.Status.AutoRefreshOff", "Automatická kontrola je vypnutá.");
        }
        else
        {
            LoadSettings();
            _timer.Start();
            BtnAutoRefresh.Content = T("MES05.Button.AutoRefreshOn", "Auto kontrola běží");
            TxtStatusLine.Text = TF("MES05.Status.AutoRefreshOn", "Automatická kontrola běží každých {0} sekund.", _settings.AutoRefreshSeconds);
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        BtnAutoRefresh.Content = T("MES05.Button.AutoRefresh", "Auto kontrola");
        _checkCancellation?.Cancel();
        TxtStatusLine.Text = T("MES05.Status.StopRequested", "Požadavek na zastavení kontroly byl odeslán.");
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        var rows = GridDevices.SelectedItems.Count > 0
            ? GridDevices.SelectedItems.Cast<MesDeviceStatusRow>().ToList()
            : _rows.Where(row => FilterRow(row)).ToList();

        var builder = new StringBuilder();
        builder.AppendLine("Status\tType\tAddress\tName\tResponse\tCheckedAt\tFailure\tNote");

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join('\t', new[]
            {
                row.StatusText,
                row.Category,
                row.Address,
                row.Name,
                row.ResponseTimeText,
                row.CheckedAtText,
                row.FailureReason,
                row.Note
            }));
        }

        Clipboard.SetText(builder.ToString());
        TxtStatusLine.Text = TF("MES05.Status.Copied", "Zkopírováno {0} řádků do schránky.", rows.Count);
    }

    private string T(string key, string fallback)
    {
        var value = _translate?.Invoke(key);
        return IsMissing(value, key) ? fallback : value!;
    }

    private string TF(string key, string fallback, params object[] args)
    {
        var value = _translateFormat?.Invoke(key, args);

        if (!string.IsNullOrWhiteSpace(value) && !IsMissing(value, key))
        {
            return value;
        }

        try
        {
            return string.Format(fallback, args);
        }
        catch
        {
            return fallback;
        }
    }

    private static bool IsMissing(string? value, string key)
    {
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase);
    }
}
