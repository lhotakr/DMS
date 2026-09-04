using DMS.Desktop.Logging;
using DMS.Desktop.Models;
using DMS.Integration.Mes.Models;
using DMS.Integration.Mes.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace DMS.Desktop.Views.Mes;

public partial class MesStationDataView : UserControl
{
    private readonly string _stationsFilePath;
    private readonly string _settingsPath;
    private readonly string _snapshotFolder;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserDisplayName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;
    private readonly MesCommunicationSettingsService _settingsService = new();
    private readonly MesStationDefinitionService _definitionService = new();
    private readonly MesStationPollingService _pollingService = new();
    private readonly DispatcherTimer _timer = new();
    private readonly ObservableCollection<MesStationDataRow> _rows = new();
    private ICollectionView? _view;
    private CancellationTokenSource? _pollCancellation;
    private MesCommunicationSettings _settings = new();
    private IReadOnlyList<MesStationDefinition> _stations = Array.Empty<MesStationDefinition>();
    private bool _isPolling;
    private DateTime? _lastPollAt;

    public MesStationDataView()
        : this(
            Path.Combine(AppContext.BaseDirectory, "Config", "mes-stations.json"),
            Path.Combine(AppContext.BaseDirectory, "Config", "mes-communication-settings.json"),
            Path.Combine(AppContext.BaseDirectory, "Data", "MES", "StationSnapshots"),
            null,
            Environment.UserName)
    {
    }

    public MesStationDataView(
        string stationsFilePath,
        string settingsPath,
        string snapshotFolder,
        DmsLogger? logger,
        string currentUserDisplayName,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _stationsFilePath = stationsFilePath;
        _settingsPath = settingsPath;
        _snapshotFolder = snapshotFolder;
        _logger = logger;
        _currentUserDisplayName = currentUserDisplayName;
        _translate = translate;
        _translateFormat = translateFormat;

        _timer.Tick += async (_, _) => await PollAsync();

        ApplyLocalization();
        ConfigureGrid();
        LoadSettings();
        LoadDefinitions();
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("MES03.Title", "MES03 - Data stanic");
        TxtSubtitle.Text = T("MES03.Subtitle", "Čtení datových bodů Counter1/2, Input1-6 a Output1-6 z jednotlivých MES stanic. Komunikace a cesty se řídí transakcí MES00.");
        TxtStationLabel.Text = T("MES03.Filter.Station", "Stanice");
        TxtProtocolLabel.Text = T("MES03.Filter.Protocol", "Protokol");
        TxtStateLabel.Text = T("MES03.Filter.State", "Stav");
        TxtSearchLabel.Text = T("MES03.Filter.Search", "Hledat");
        BtnReloadDefinitions.Content = T("MES03.Button.ReloadDefinitions", "Načíst stanice");
        BtnReloadSettings.Content = T("MES03.Button.ReloadSettings", "Načíst nastavení");
        BtnPollNow.Content = T("MES03.Button.PollNow", "Číst teď");
        BtnAutoRefresh.Content = T("MES03.Button.AutoRefresh", "Auto čtení");
        BtnStop.Content = T("MES03.Button.Stop", "Stop");
        BtnCopy.Content = T("MES03.Button.Copy", "Kopírovat");

        if (CmbState.Items.Count > 0 && CmbState.Items[0] is ComboBoxItem allStateItem)
        {
            allStateItem.Content = T("MES03.Filter.All", "Vše");
        }

        ColStatus.Header = T("MES03.Column.Status", "Stav");
        ColStation.Header = T("MES03.Column.Station", "Stanice");
        ColWorkCenter.Header = T("MES03.Column.WorkCenter", "Pracoviště");
        ColProtocol.Header = T("MES03.Column.Protocol", "Protokol");
        ColHost.Header = T("MES03.Column.Host", "Host");
        ColPoint.Header = T("MES03.Column.Point", "Datový bod");
        ColRole.Header = T("MES03.Column.Role", "Role");
        ColAddress.Header = T("MES03.Column.Address", "Adresa");
        ColValue.Header = T("MES03.Column.Value", "Hodnota");
        ColQuality.Header = T("MES03.Column.Quality", "Kvalita");
        ColResponseTime.Header = T("MES03.Column.ResponseTime", "Odezva");
        ColCheckedAt.Header = T("MES03.Column.CheckedAt", "Čteno");
        ColError.Header = T("MES03.Column.Error", "Chyba");
    }

    private void ConfigureGrid()
    {
        GridDataPoints.ItemsSource = _rows;
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterRow;
    }

    private void LoadSettings()
    {
        _settings = _settingsService.Load(_settingsPath);
        _timer.Interval = TimeSpan.FromSeconds(_settings.StationAutoRefreshSeconds);
        RefreshHeader();
    }

    private void LoadDefinitions()
    {
        try
        {
            _stations = _definitionService.Load(_stationsFilePath);
            ReloadStationFilter();
            ReloadProtocolFilter();
            _rows.Clear();
            RefreshHeader();
            TxtStatusLine.Text = TF("MES03.Status.DefinitionsLoaded", "Načteno {0} stanic. Spusť čtení dat.", _stations.Count);
            _logger?.AdminAction("MES03", "LoadMesStationDefinitions", _currentUserDisplayName, $"File={_stationsFilePath}; Stations={_stations.Count}");
        }
        catch (Exception ex)
        {
            TxtStatusLine.Text = TF("MES03.Status.DefinitionsLoadFailed", "Načtení definic stanic selhalo: {0}", ex.Message);
            _logger?.AdminAction("MES03", "LoadMesStationDefinitionsFailed", _currentUserDisplayName, $"File={_stationsFilePath}; Error={ex.Message}");
        }
    }

    private async Task PollAsync()
    {
        if (_isPolling)
        {
            return;
        }

        if (!_settings.EnableStationDataPolling)
        {
            TxtStatusLine.Text = T("MES03.Status.Disabled", "Čtení dat stanic je v MES00 vypnuté.");
            return;
        }

        _isPolling = true;
        BtnPollNow.IsEnabled = false;
        BtnReloadDefinitions.IsEnabled = false;
        BtnReloadSettings.IsEnabled = false;
        TxtStatusLine.Text = T("MES03.Status.Polling", "Probíhá čtení datových bodů ze stanic...");

        _pollCancellation?.Cancel();
        _pollCancellation?.Dispose();
        _pollCancellation = new CancellationTokenSource();

        try
        {
            var snapshots = await _pollingService.PollAsync(
                _stations,
                _snapshotFolder,
                _settings.MaxParallelism,
                _pollCancellation.Token);

            _pollingService.SaveSnapshots(_snapshotFolder, snapshots);
            _rows.Clear();
            foreach (var snapshot in snapshots)
            {
                foreach (var point in snapshot.DataPoints)
                {
                    var row = new MesStationDataRow();
                    row.ApplySnapshot(snapshot, point);
                    _rows.Add(row);
                }
            }

            _lastPollAt = DateTime.Now;
            ReloadStationFilter(keepSelection: true);
            ReloadProtocolFilter(keepSelection: true);
            RefreshHeader();
            _view?.Refresh();

            var ok = _rows.Count(row => row.IsOk);
            var nok = _rows.Count - ok;
            TxtStatusLine.Text = TF("MES03.Status.Polled", "Čtení dokončeno: OK {0}, chyba/nepřipraveno {1}, čas {2}", ok, nok, _lastPollAt.Value.ToString("HH:mm:ss"));
            _logger?.AdminAction("MES03", "PollMesStationData", _currentUserDisplayName, $"Stations={snapshots.Count}; Rows={_rows.Count}; Ok={ok}; Nok={nok}");
        }
        catch (OperationCanceledException)
        {
            TxtStatusLine.Text = T("MES03.Status.Cancelled", "Čtení bylo zastaveno.");
        }
        catch (Exception ex)
        {
            TxtStatusLine.Text = TF("MES03.Status.PollFailed", "Čtení selhalo: {0}", ex.Message);
            _logger?.AdminAction("MES03", "PollMesStationDataFailed", _currentUserDisplayName, ex.Message);
        }
        finally
        {
            _isPolling = false;
            BtnPollNow.IsEnabled = true;
            BtnReloadDefinitions.IsEnabled = true;
            BtnReloadSettings.IsEnabled = true;
        }
    }

    private void RefreshHeader()
    {
        var activeStations = _stations.Count(station => station.IsActive);
        var ok = _rows.Count(row => row.IsOk);
        var nok = _rows.Count - ok;
        var last = _lastPollAt.HasValue
            ? _lastPollAt.Value.ToString("dd.MM.yyyy HH:mm:ss")
            : T("MES03.Summary.NotChecked", "zatím neproběhlo");

        TxtSummary.Text = TF("MES03.Summary", "Stanice: {0} aktivní z {1} | Datové body: {2} | OK: {3} | Chyba/nepřipraveno: {4} | Poslední čtení: {5}", activeStations, _stations.Count, _rows.Count, ok, nok, last);
        TxtStationsPath.Text = TF("MES03.StationsPath", "Definice stanic: {0}", _stationsFilePath);
        TxtSettingsPath.Text = TF("MES03.SettingsPath", "Nastavení MES00: {0} | timeout stanice {1} ms | paralelně {2} | auto {3} s", _settingsPath, _settings.StationPollTimeoutMs, _settings.MaxParallelism, _settings.StationAutoRefreshSeconds);
        TxtSnapshotFolder.Text = TF("MES03.SnapshotFolder", "Snímky stanic: {0}", _snapshotFolder);
    }

    private void ReloadStationFilter(bool keepSelection = false)
    {
        var selected = keepSelection ? CmbStation.SelectedItem?.ToString() : null;
        var values = _stations.Select(station => station.StationCode)
            .Concat(_rows.Select(row => row.StationCode))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();
        values.Insert(0, T("MES03.Filter.All", "Vše"));
        CmbStation.ItemsSource = values;
        CmbStation.SelectedItem = values.FirstOrDefault(value => string.Equals(value, selected, StringComparison.OrdinalIgnoreCase)) ?? values[0];
    }

    private void ReloadProtocolFilter(bool keepSelection = false)
    {
        var selected = keepSelection ? CmbProtocol.SelectedItem?.ToString() : null;
        var values = _stations.Select(station => station.Protocol)
            .Concat(_rows.Select(row => row.Protocol))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();
        values.Insert(0, T("MES03.Filter.All", "Vše"));
        CmbProtocol.ItemsSource = values;
        CmbProtocol.SelectedItem = values.FirstOrDefault(value => string.Equals(value, selected, StringComparison.OrdinalIgnoreCase)) ?? values[0];
    }

    private bool FilterRow(object item)
    {
        if (item is not MesStationDataRow row)
        {
            return false;
        }

        var allText = T("MES03.Filter.All", "Vše");
        var stationFilter = CmbStation.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(stationFilter) && !string.Equals(stationFilter, allText, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(row.StationCode, stationFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var protocolFilter = CmbProtocol.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(protocolFilter) && !string.Equals(protocolFilter, allText, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(row.Protocol, protocolFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var stateFilter = GetSelectedState();
        if (!string.Equals(stateFilter, allText, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(stateFilter, "OK", StringComparison.OrdinalIgnoreCase))
            {
                if (!row.IsOk)
                {
                    return false;
                }
            }
            else if (!string.Equals(row.StatusText, stateFilter, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(row.Quality, stateFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var query = TxtSearch.Text?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(query) || row.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private string GetSelectedState()
    {
        if (CmbState.SelectedItem is ComboBoxItem item)
        {
            return item.Content?.ToString() ?? T("MES03.Filter.All", "Vše");
        }

        return CmbState.SelectedItem?.ToString() ?? T("MES03.Filter.All", "Vše");
    }

    private void BtnReloadDefinitions_Click(object sender, RoutedEventArgs e) => LoadDefinitions();

    private void BtnReloadSettings_Click(object sender, RoutedEventArgs e)
    {
        LoadSettings();
        TxtStatusLine.Text = T("MES03.Status.SettingsReloaded", "Nastavení MES00 bylo znovu načteno.");
    }

    private async void BtnPollNow_Click(object sender, RoutedEventArgs e) => await PollAsync();

    private void BtnAutoRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_timer.IsEnabled)
        {
            _timer.Stop();
            BtnAutoRefresh.Content = T("MES03.Button.AutoRefresh", "Auto čtení");
            TxtStatusLine.Text = T("MES03.Status.AutoRefreshOff", "Automatické čtení je vypnuté.");
        }
        else
        {
            _timer.Interval = TimeSpan.FromSeconds(_settings.StationAutoRefreshSeconds);
            _timer.Start();
            BtnAutoRefresh.Content = T("MES03.Button.AutoRefreshOn", "Auto čtení běží");
            TxtStatusLine.Text = TF("MES03.Status.AutoRefreshOn", "Automatické čtení běží každých {0} sekund.", _settings.StationAutoRefreshSeconds);
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        BtnAutoRefresh.Content = T("MES03.Button.AutoRefresh", "Auto čtení");
        _pollCancellation?.Cancel();
        TxtStatusLine.Text = T("MES03.Status.StopRequested", "Požadavek na zastavení čtení byl odeslán.");
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        var rows = GridDataPoints.SelectedItems.Cast<MesStationDataRow>().ToList();
        if (rows.Count == 0)
        {
            rows = _view?.Cast<MesStationDataRow>().ToList() ?? _rows.ToList();
        }

        var builder = new StringBuilder();
        builder.AppendLine("Station\tWorkCenter\tProtocol\tHost\tPoint\tRole\tAddress\tValue\tQuality\tCheckedAt\tError");
        foreach (var row in rows)
        {
            builder.Append(row.StationCode).Append('\t')
                .Append(row.WorkCenter).Append('\t')
                .Append(row.Protocol).Append('\t')
                .Append(row.Host).Append('\t')
                .Append(row.PointName).Append('\t')
                .Append(row.Role).Append('\t')
                .Append(row.Address).Append('\t')
                .Append(row.Value).Append('\t')
                .Append(row.Quality).Append('\t')
                .Append(row.CheckedAtText).Append('\t')
                .Append(row.ErrorMessage)
                .AppendLine();
        }

        Clipboard.SetText(builder.ToString());
        TxtStatusLine.Text = TF("MES03.Status.Copied", "Zkopírováno {0} řádků do schránky.", rows.Count);
    }

    private void Filter_Changed(object sender, EventArgs e) => _view?.Refresh();

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
