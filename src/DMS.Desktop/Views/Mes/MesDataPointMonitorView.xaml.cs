using DMS.Core.Mes;
using DMS.Desktop.Configuration.Mes;
using DMS.Desktop.Logging;
using DMS.Desktop.Services.Mes;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace DMS.Desktop.Views.Mes;

public partial class MesDataPointMonitorView : UserControl
{
    private readonly string _initialQuery;
    private readonly string _configurationRootPath;
    private readonly string _settingsFilePath;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserName;
    private readonly Func<string, string>? _translate;

    private readonly MesDeviceInventoryParser _inventoryParser = new();
    private readonly MesDataPointReadService _readService = new();
    private readonly ObservableCollection<MesDataPointDisplayRow> _rows = new();
    private readonly Dictionary<string, MesDataPointDisplayRow> _rowsByCode =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    private ICollectionView? _rowsView;
    private MesIntegrationSettings _settings = new();
    private MesPlcBindingSet _bindingSet = new();
    private IReadOnlyList<MesDeviceEntry> _machines = Array.Empty<MesDeviceEntry>();
    private IReadOnlyList<string> _inventoryErrors = Array.Empty<string>();
    private string _settingsError = string.Empty;
    private string _bindingsError = string.Empty;

    private string _devicesFilePath = string.Empty;
    private string _bindingsFilePath = string.Empty;
    private DateTime _settingsWriteTimeUtc;
    private DateTime _devicesWriteTimeUtc;
    private DateTime _bindingsWriteTimeUtc;
    private DateTimeOffset _nextConfigurationCheckAt;

    private bool _isLoadingDevices;
    private bool _isPaused;
    private bool _refreshInProgress;
    private bool? _lastOnlineState;
    private string _lastOnlineStation = string.Empty;

    private readonly MesMachineRuntimeEvaluator _runtimeEvaluator = new();

    public MesDataPointMonitorView(
        string? query,
        string configurationRootPath,
        DmsLogger? logger = null,
        string? currentUserName = null,
        Func<string, string>? translate = null)
    {
        InitializeComponent();

        _initialQuery = query?.Trim() ?? string.Empty;
        _configurationRootPath = configurationRootPath;
        _settingsFilePath = Path.Combine(
            _configurationRootPath,
            "mes-integration.json");
        _logger = logger;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName)
            ? "UNKNOWN"
            : currentUserName;
        _translate = translate;

        GridDataPoints.ItemsSource = _rows;
        _rowsView = CollectionViewSource.GetDefaultView(_rows);
        _rowsView.Filter = FilterRow;

        _refreshTimer.Tick += RefreshTimer_Tick;

        Loaded += View_Loaded;
        Unloaded += View_Unloaded;

        ApplyLocalization();
        ClearHeader();
    }

    private async void View_Loaded(object sender, RoutedEventArgs e)
    {
        ReloadAllConfiguration(force: true);
        SelectInitialDevice();
        UpdateRefreshInterval();

        _refreshTimer.Start();

        _logger?.AdminAction(
            "MESDPM",
            "Open",
            _currentUserName,
            $"Query={_initialQuery}; DevicesFile={_devicesFilePath}; BindingsFile={_bindingsFilePath}");

        await RefreshSelectedDeviceAsync(forceConfigurationCheck: true);
    }

    private void View_Unloaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Stop();
        _lifetimeCancellation.Cancel();
        _ = _readService.DisposeAsync();

        _logger?.AdminAction(
            "MESDPM",
            "Close",
            _currentUserName,
            $"Station={GetSelectedDevice()?.StationCode}");
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (_isPaused)
        {
            return;
        }

        await RefreshSelectedDeviceAsync(forceConfigurationCheck: false);
    }

    private async void CmbDevices_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isLoadingDevices)
        {
            return;
        }

        ResetRuntimeEvaluation();
        UpdateRefreshInterval();

        _logger?.AdminAction(
            "MESDPM",
            "SelectDevice",
            _currentUserName,
            $"Station={GetSelectedDevice()?.StationCode}; IP={GetSelectedDevice()?.IpAddress}");

        await RefreshSelectedDeviceAsync(forceConfigurationCheck: true);
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        _logger?.AdminAction(
            "MESDPM",
            "ManualRefresh",
            _currentUserName,
            $"Station={GetSelectedDevice()?.StationCode}; IP={GetSelectedDevice()?.IpAddress}");

        await RefreshSelectedDeviceAsync(forceConfigurationCheck: true);
    }

    private void BtnPause_Click(object sender, RoutedEventArgs e)
    {
        _isPaused = !_isPaused;
        BtnPause.Content = _isPaused
            ? T("MESDPM.Action.Resume", "Resume")
            : T("MESDPM.Action.Pause", "Pause");

        _logger?.AdminAction(
            "MESDPM",
            _isPaused ? "Pause" : "Resume",
            _currentUserName,
            $"Station={GetSelectedDevice()?.StationCode}");

        TxtStatus.Text = _isPaused
            ? T(
                "MESDPM.Status.Paused",
                "Monitor refresh is paused. Phase 1 pauses polling in this view; a separate background collector will be added later.")
            : T("MESDPM.Status.Resumed", "Monitor refresh resumed.");
    }

    private void BtnCopyIp_Click(object sender, RoutedEventArgs e)
    {
        var device = GetSelectedDevice();

        if (device is null || string.IsNullOrWhiteSpace(device.IpAddress))
        {
            return;
        }

        try
        {
            Clipboard.SetText(device.IpAddress);

            _logger?.AdminAction(
                "MESDPM",
                "CopyIp",
                _currentUserName,
                $"Station={device.StationCode}; IP={device.IpAddress}");

            TxtStatus.Text = TF(
                "MESDPM.Status.IpCopied",
                "IP address copied: {0}",
                device.IpAddress);
        }
        catch (Exception ex)
        {
            TxtStatus.Text = ex.Message;
        }
    }

    private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        _rowsView?.Refresh();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        _rowsView?.Refresh();
    }

    private void GridDataPoints_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateDetail();
    }

    private async Task RefreshSelectedDeviceAsync(bool forceConfigurationCheck)
    {
        if (_refreshInProgress || _lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        _refreshInProgress = true;

        try
        {
            if (forceConfigurationCheck ||
                DateTimeOffset.Now >= _nextConfigurationCheckAt)
            {
                ReloadAllConfiguration(force: false);
                _nextConfigurationCheckAt = DateTimeOffset.Now.AddSeconds(
                    Math.Max(1, _settings.DeviceInventoryReloadSeconds));
            }

            var device = GetSelectedDevice();

            if (device is null)
            {
                ClearHeader();
                _rows.Clear();
                _rowsByCode.Clear();
                TxtStatus.Text = T(
                    "MESDPM.Status.NoMachine",
                    "No STROJ entry is available in devices.txt.");
                return;
            }

            var binding = FindBinding(device);

            var snapshot = await _readService.ReadSnapshotAsync(
                device,
                binding,
                _settings,
                _lifetimeCancellation.Token);

            UpdateHeader(snapshot);
            UpdateRows(snapshot);
            UpdateMachineRuntime(snapshot);
            LogConnectionTransition(snapshot);
            UpdateStatus(snapshot);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // View is closing.
        }
        catch (Exception ex)
        {
            TxtConnection.Text = T("MESDPM.Connection.Offline", "Offline");
            TxtStatus.Text = ex.Message;
            _logger?.Error(
                $"MESDPM refresh failed; Station={GetSelectedDevice()?.StationCode}; User={_currentUserName}",
                ex);
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private void ReloadAllConfiguration(bool force)
    {
        var settingsWriteTime = GetWriteTimeUtc(_settingsFilePath);

        if (force || settingsWriteTime != _settingsWriteTimeUtc)
        {
            var previousDevicesPath = _devicesFilePath;
            var previousBindingsPath = _bindingsFilePath;

            var settingsService =
                new MesIntegrationSettingsService(_settingsFilePath);
            var loadedSettings = settingsService.Load();
            _settingsError = settingsService.LastError;

            if (force || string.IsNullOrWhiteSpace(_settingsError))
            {
                _settings = loadedSettings;
            }

            _settingsWriteTimeUtc = settingsWriteTime;

            _devicesFilePath = MesConfigurationPathResolver.Resolve(
                _configurationRootPath,
                _settings.DevicesFilePath);

            _bindingsFilePath = MesConfigurationPathResolver.Resolve(
                _configurationRootPath,
                _settings.PlcBindingsFilePath);

            if (!string.Equals(
                    previousDevicesPath,
                    _devicesFilePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                _devicesWriteTimeUtc = DateTime.MinValue;
            }

            if (!string.Equals(
                    previousBindingsPath,
                    _bindingsFilePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                _bindingsWriteTimeUtc = DateTime.MinValue;
            }

            if (!string.IsNullOrWhiteSpace(_settingsError))
            {
                _logger?.Warning(
                    $"MESDPM settings reload failed; Error={_settingsError}; User={_currentUserName}");
            }
        }

        var devicesWriteTime = GetWriteTimeUtc(_devicesFilePath);
        var bindingsWriteTime = GetWriteTimeUtc(_bindingsFilePath);

        if (force || devicesWriteTime != _devicesWriteTimeUtc)
        {
            var loadResult = _inventoryParser.Load(_devicesFilePath);
            _inventoryErrors = loadResult.Errors;
            _devicesWriteTimeUtc = devicesWriteTime;

            if (loadResult.Success)
            {
                _machines = loadResult.Devices
                    .Where(device => device.IsMachine)
                    .OrderBy(device => device.StationCode)
                    .ThenBy(device => device.IpAddress)
                    .ToList();

                ReloadDeviceSelector();

                _logger?.Info(
                    $"MESDPM device inventory reloaded; Machines={_machines.Count}; " +
                    $"Errors={_inventoryErrors.Count}; File={_devicesFilePath}; User={_currentUserName}");
            }
            else
            {
                _logger?.Warning(
                    $"MESDPM device inventory reload failed; File={_devicesFilePath}; " +
                    $"Error={string.Join(" | ", _inventoryErrors)}; User={_currentUserName}");
            }
        }

        if (force || bindingsWriteTime != _bindingsWriteTimeUtc)
        {
            var bindingService = new MesPlcBindingService(_bindingsFilePath);
            var loadedBindings = bindingService.Load();
            _bindingsError = bindingService.LastError;
            _bindingsWriteTimeUtc = bindingsWriteTime;

            if (force || string.IsNullOrWhiteSpace(_bindingsError))
            {
                _bindingSet = loadedBindings;
            }

            if (string.IsNullOrWhiteSpace(_bindingsError))
            {
                _logger?.Info(
                    $"MESDPM PLC bindings reloaded; Bindings={_bindingSet.Devices.Count}; " +
                    $"File={_bindingsFilePath}; User={_currentUserName}");
            }
            else
            {
                _logger?.Warning(
                    $"MESDPM PLC bindings reload failed; Error={_bindingsError}; User={_currentUserName}");
            }
        }
    }

    private void ReloadDeviceSelector()
    {
        var selectedIp = GetSelectedDevice()?.IpAddress;

        _isLoadingDevices = true;

        try
        {
            CmbDevices.ItemsSource = _machines;

            if (!string.IsNullOrWhiteSpace(selectedIp))
            {
                CmbDevices.SelectedItem = _machines.FirstOrDefault(device =>
                    string.Equals(
                        device.IpAddress,
                        selectedIp,
                        StringComparison.OrdinalIgnoreCase));
            }

            if (CmbDevices.SelectedItem is null && _machines.Count > 0)
            {
                CmbDevices.SelectedIndex = 0;
            }
        }
        finally
        {
            _isLoadingDevices = false;
        }
    }

    private void SelectInitialDevice()
    {
        if (string.IsNullOrWhiteSpace(_initialQuery) || _machines.Count == 0)
        {
            return;
        }

        var selected = _machines.FirstOrDefault(device =>
            string.Equals(device.IpAddress, _initialQuery, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(device.StationCode, _initialQuery, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(device.Name, _initialQuery, StringComparison.OrdinalIgnoreCase));

        if (selected is not null)
        {
            CmbDevices.SelectedItem = selected;
        }
    }

    private MesPlcBinding? FindBinding(MesDeviceEntry device)
    {
        return _bindingSet.Devices.FirstOrDefault(binding =>
                   string.Equals(
                       binding.StationCode,
                       device.StationCode,
                       StringComparison.OrdinalIgnoreCase))
               ?? _bindingSet.Devices.FirstOrDefault(binding =>
                   !string.IsNullOrWhiteSpace(binding.IpAddressOverride) &&
                   string.Equals(
                       binding.IpAddressOverride,
                       device.IpAddress,
                       StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateHeader(MesDeviceSnapshot snapshot)
    {
        TxtStation.Text = snapshot.Device.StationCode;
        TxtIpAddress.Text = snapshot.Device.IpAddress;
        TxtDriver.Text = snapshot.Binding?.Driver ?? snapshot.Device.SuggestedDriver;
        TxtController.Text = string.IsNullOrWhiteSpace(snapshot.Binding?.Controller)
            ? "—"
            : snapshot.Binding.Controller;
        TxtConnection.Text = snapshot.IsOnline
            ? T("MESDPM.Connection.Online", "Online")
            : snapshot.DataPoints.Any(point =>
                point.Quality == MesDataPointQuality.ConfigurationError)
                ? T("MESDPM.Connection.NotConfigured", "Not configured")
                : snapshot.DataPoints.Any(point =>
                    point.Quality == MesDataPointQuality.Unsupported)
                    ? T("MESDPM.Connection.Unsupported", "Deferred / unsupported")
                    : T("MESDPM.Connection.Offline", "Offline");
        TxtLastRead.Text = snapshot.ReadAt.ToString("dd.MM.yyyy HH:mm:ss.fff");
    }

    private void ClearHeader()
    {
        TxtStation.Text = "—";
        TxtIpAddress.Text = "—";
        TxtDriver.Text = "—";
        TxtController.Text = "—";
        TxtConnection.Text = T("MESDPM.Connection.Unknown", "Unknown");
        TxtMachineState.Text = T("MESDPM.MachineState.Unknown", "Unknown");
        TxtCycleTime.Text = "—";
        TxtLastRead.Text = "—";
    }

    private void UpdateRows(MesDeviceSnapshot snapshot)
    {
        var incomingCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in snapshot.DataPoints)
        {
            incomingCodes.Add(value.Code);

            if (!_rowsByCode.TryGetValue(value.Code, out var row))
            {
                row = new MesDataPointDisplayRow
                {
                    Code = value.Code
                };

                _rowsByCode.Add(value.Code, row);
                _rows.Add(row);
            }

            row.UpdateFrom(
                value,
                LocalizeSignal(value.LogicalSignal, value.DisplayName),
                LocalizeQuality(value.Quality),
                LocalizeSource(value.SourceText),
                LocalizeValue(value.RawValue, value.DisplayValue));
        }

        var removed = _rows
            .Where(row => !incomingCodes.Contains(row.Code))
            .ToList();

        foreach (var row in removed)
        {
            _rows.Remove(row);
            _rowsByCode.Remove(row.Code);
        }

        var ordered = _rows
            .OrderBy(row => row.Slot)
            .ThenBy(row => row.Channel)
            .ThenBy(row => row.Code)
            .ToList();

        for (var targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
        {
            var currentIndex = _rows.IndexOf(ordered[targetIndex]);

            if (currentIndex != targetIndex)
            {
                _rows.Move(currentIndex, targetIndex);
            }
        }

        _rowsView?.Refresh();
        UpdateDetail();
    }

    private void UpdateMachineRuntime(MesDeviceSnapshot snapshot)
    {
        var goodCounter = snapshot.DataPoints.FirstOrDefault(point =>
            string.Equals(
                point.LogicalSignal,
                "GOOD_PIECES",
                StringComparison.OrdinalIgnoreCase) &&
            point.Quality == MesDataPointQuality.Valid);

        var stopTimeoutSeconds = snapshot.Binding?.StopTimeoutSeconds > 0
            ? snapshot.Binding.StopTimeoutSeconds
            : 30;

        var result = _runtimeEvaluator.Update(
            snapshot.Device.StationCode,
            snapshot.IsOnline,
            goodCounter?.RawValue,
            snapshot.ReadAt,
            TimeSpan.FromSeconds(stopTimeoutSeconds));

        TxtMachineState.Text = result.State switch
        {
            MesMachineRuntimeState.Running =>
                T("MESDPM.MachineState.Running", "Running"),
            MesMachineRuntimeState.Stopped =>
                T("MESDPM.MachineState.Stopped", "Stopped"),
            MesMachineRuntimeState.Offline =>
                T("MESDPM.MachineState.Offline", "Offline"),
            MesMachineRuntimeState.WaitingForPulse =>
                T("MESDPM.MachineState.WaitingPulse", "Waiting for pulse"),
            _ => T("MESDPM.MachineState.Unknown", "Unknown")
        };

        TxtCycleTime.Text = result.ObservedCycleTime.HasValue
            ? TF(
                "MESDPM.CycleTime.Value",
                "{0:0.000} s (+{1})",
                result.ObservedCycleTime.Value.TotalSeconds,
                result.CounterDelta)
            : "—";

        if (result.CounterWrapDetected)
        {
            _logger?.Info(
                $"MESDPM counter wrap detected; Station={snapshot.Device.StationCode}; " +
                $"Signal=GOOD_PIECES; Delta={result.CounterDelta}; User={_currentUserName}");
        }
        else if (result.CounterResetDetected)
        {
            _logger?.Warning(
                $"MESDPM counter reset detected; Station={snapshot.Device.StationCode}; " +
                $"Signal=GOOD_PIECES; User={_currentUserName}");
        }
    }

    private void ResetRuntimeEvaluation()
    {
        _runtimeEvaluator.Reset(GetSelectedDevice()?.StationCode);
        _lastOnlineState = null;
        _lastOnlineStation = string.Empty;
    }

    private void LogConnectionTransition(MesDeviceSnapshot snapshot)
    {
        var stationChanged = !string.Equals(
            _lastOnlineStation,
            snapshot.Device.StationCode,
            StringComparison.OrdinalIgnoreCase);

        if (stationChanged || !_lastOnlineState.HasValue)
        {
            _lastOnlineState = snapshot.IsOnline;
            _lastOnlineStation = snapshot.Device.StationCode;
            return;
        }

        if (_lastOnlineState.Value == snapshot.IsOnline)
        {
            return;
        }

        _lastOnlineState = snapshot.IsOnline;

        if (snapshot.IsOnline)
        {
            _logger?.Info(
                $"MESDPM device online; Station={snapshot.Device.StationCode}; IP={snapshot.Device.IpAddress}; User={_currentUserName}");
        }
        else
        {
            _logger?.Warning(
                $"MESDPM device offline; Station={snapshot.Device.StationCode}; IP={snapshot.Device.IpAddress}; " +
                $"Reason={snapshot.StatusMessage}; User={_currentUserName}");
        }
    }

    private void UpdateStatus(MesDeviceSnapshot snapshot)
    {
        var configurationErrors = new List<string>();

        if (!string.IsNullOrWhiteSpace(_settingsError))
        {
            configurationErrors.Add(_settingsError);
        }

        if (!string.IsNullOrWhiteSpace(_bindingsError))
        {
            configurationErrors.Add(_bindingsError);
        }

        configurationErrors.AddRange(_inventoryErrors.Take(3));

        var errorText = configurationErrors.Count == 0
            ? string.Empty
            : " | " + string.Join(" | ", configurationErrors);

        TxtStatus.Text =
            $"{LocalizeSnapshotStatus(snapshot)} | " +
            $"devices.txt: {_devicesFilePath} | " +
            $"bindings: {_bindingsFilePath}" +
            errorText;
    }

    private string LocalizeSnapshotStatus(MesDeviceSnapshot snapshot)
    {
        if (snapshot.IsOnline)
        {
            return T("MESDPM.Status.Online", "Online");
        }

        if (snapshot.Binding is null)
        {
            return T(
                "MESDPM.Status.BindingMissing",
                "No PLC binding is configured for this station.");
        }

        if (!snapshot.Binding.Enabled)
        {
            return T(
                "MESDPM.Status.BindingDisabled",
                "The PLC binding is disabled.");
        }

        if (string.Equals(
                snapshot.Binding.Driver,
                MesDriverKeys.SiemensDeferred,
                StringComparison.OrdinalIgnoreCase))
        {
            return T(
                "MESDPM.Status.SiemensDeferred",
                "Siemens PLC reading is intentionally deferred to a later phase.");
        }

        if (snapshot.DataPoints.Any(point =>
                point.Quality == MesDataPointQuality.ConfigurationError))
        {
            return T(
                "MESDPM.Status.MappingMissing",
                "The station is known, but Modbus addresses have not been entered yet.");
        }

        return snapshot.StatusMessage;
    }

    private void UpdateDetail()
    {
        if (GridDataPoints.SelectedItem is not MesDataPointDisplayRow row)
        {
            TxtDetail.Text = T(
                "MESDPM.Detail.Empty",
                "Select a data point to show its technical detail.");
            return;
        }

        TxtDetail.Text = BuildDetailText(row);
    }

    private bool FilterRow(object item)
    {
        if (item is not MesDataPointDisplayRow row)
        {
            return false;
        }

        if (ChkOnlyActive.IsChecked == true && !row.IsActive)
        {
            return false;
        }

        if (ChkOnlyChanged.IsChecked == true && !row.WasChangedInLastRead)
        {
            return false;
        }

        var filter = TxtFilter.Text?.Trim();

        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return Contains(row.Code, filter) ||
               Contains(row.LogicalSignal, filter) ||
               Contains(row.DisplayName, filter) ||
               Contains(row.ModuleType, filter) ||
               Contains(row.SourceText, filter) ||
               Contains(row.ValueText, filter) ||
               Contains(row.QualityText, filter) ||
               Contains(row.Error, filter);
    }

    private void UpdateRefreshInterval()
    {
        var binding = GetSelectedDevice() is { } device
            ? FindBinding(device)
            : null;

        var intervalMs = binding?.PollIntervalMs > 0
            ? binding.PollIntervalMs
            : _settings.DefaultRefreshIntervalMs;

        _refreshTimer.Interval = TimeSpan.FromMilliseconds(
            Math.Clamp(intervalMs, 250, 60_000));
    }

    private MesDeviceEntry? GetSelectedDevice()
    {
        return CmbDevices.SelectedItem as MesDeviceEntry;
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("MESDPM.Title", "MESDPM - Data Point Monitor");
        TxtSubtitle.Text = T(
            "MESDPM.Subtitle",
            "Read-only diagnostic view of counters, digital inputs and MES status outputs. IP addresses come from the shared live devices.txt inventory.");

        LblStation.Text = T("MESDPM.Field.Station", "Station");
        LblIpAddress.Text = T("MESDPM.Field.IpAddress", "IP address");
        LblDriver.Text = T("MESDPM.Field.Driver", "Driver");
        LblConnection.Text = T("MESDPM.Field.Connection", "Connection");
        LblController.Text = T("MESDPM.Field.Controller", "Controller");
        LblMachineState.Text = T("MESDPM.Field.MachineState", "Machine state");
        LblCycleTime.Text = T("MESDPM.Field.CycleTime", "Observed cycle time");
        LblLastRead.Text = T("MESDPM.Field.LastRead", "Last read");

        LblDevice.Text = T("MESDPM.Filter.Device", "Device:");
        LblFilter.Text = T("MESDPM.Filter.Text", "Filter:");
        ChkOnlyActive.Content = T("MESDPM.Filter.OnlyActive", "Only active");
        ChkOnlyChanged.Content = T("MESDPM.Filter.OnlyChanged", "Only changed");

        BtnRefresh.Content = T("MESDPM.Action.Refresh", "Refresh");
        BtnPause.Content = T("MESDPM.Action.Pause", "Pause");
        BtnCopyIp.Content = T("MESDPM.Action.CopyIp", "Copy IP");

        ColPoint.Header = T("MESDPM.Column.Point", "Data point");
        ColMeaning.Header = T("MESDPM.Column.Meaning", "Meaning");
        ColLogicalSignal.Header = T("MESDPM.Column.LogicalSignal", "Logical signal");
        ColValue.Header = T("MESDPM.Column.Value", "Value");
        ColModule.Header = T("MESDPM.Column.Module", "Module");
        ColPhysicalPoint.Header = T("MESDPM.Column.PhysicalPoint", "Channel");
        ColSource.Header = T("MESDPM.Column.Source", "Modbus source");
        ColQuality.Header = T("MESDPM.Column.Quality", "Quality");
        ColLastChange.Header = T("MESDPM.Column.LastChange", "Last change");
        ColLastRead.Header = T("MESDPM.Column.LastRead", "Last read");

        TxtDetailTitle.Text = T("MESDPM.Detail.Title", "Data point detail");
        TxtDetail.Text = T(
            "MESDPM.Detail.Empty",
            "Select a data point to show its technical detail.");
    }

    private string BuildDetailText(MesDataPointDisplayRow row)
    {
        return string.Join(
            Environment.NewLine,
            $"{T("MESDPM.Detail.Code", "Code")}: {row.Code}",
            $"{T("MESDPM.Detail.LogicalSignal", "Logical signal")}: {row.LogicalSignal}",
            $"{T("MESDPM.Detail.Meaning", "Meaning")}: {row.DisplayName}",
            $"{T("MESDPM.Detail.Module", "Module")}: {row.ModuleType}",
            $"{T("MESDPM.Detail.PhysicalPoint", "Physical point")}: {row.PhysicalPointText}",
            $"{T("MESDPM.Detail.Source", "Modbus source")}: {row.SourceText}",
            $"{T("MESDPM.Detail.Value", "Value")}: {row.ValueText}",
            $"{T("MESDPM.Detail.Quality", "Quality")}: {row.QualityText}",
            $"{T("MESDPM.Detail.LastRead", "Last read")}: {row.ReadAtText}",
            $"{T("MESDPM.Detail.LastChange", "Last change")}: {row.ChangedAtText}",
            $"{T("MESDPM.Detail.Error", "Error")}: {row.Error}");
    }

    private string LocalizeSource(string sourceText)
    {
        return string.Equals(
                sourceText,
                "Not mapped",
                StringComparison.OrdinalIgnoreCase)
            ? T("MESDPM.Source.NotMapped", "Not mapped")
            : sourceText;
    }

    private string LocalizeValue(object? rawValue, string fallback)
    {
        return rawValue switch
        {
            bool true => T("MESDPM.Value.On", "ON"),
            bool false => T("MESDPM.Value.Off", "OFF"),
            _ => fallback
        };
    }

    private string LocalizeSignal(string logicalSignal, string fallback)
    {
        if (string.IsNullOrWhiteSpace(logicalSignal))
        {
            return fallback;
        }

        return T($"MESDPM.Signal.{logicalSignal}", fallback);
    }

    private string LocalizeQuality(MesDataPointQuality quality)
    {
        return T($"MESDPM.Quality.{quality}", quality.ToString());
    }

    private string T(string key, string fallback)
    {
        if (_translate is null)
        {
            return fallback;
        }

        var translated = _translate(key);

        return string.IsNullOrWhiteSpace(translated) ||
               string.Equals(translated, $"[[{key}]]", StringComparison.Ordinal)
            ? fallback
            : translated;
    }

    private string TF(
        string key,
        string fallback,
        params object[] args)
    {
        var template = T(key, fallback);

        try
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                template,
                args);
        }
        catch (FormatException)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                fallback,
                args);
        }
    }

    private static bool Contains(string? value, string filter)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime GetWriteTimeUtc(string? filePath)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath)
                ? File.GetLastWriteTimeUtc(filePath)
                : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

}
