using DMS.Core.Mes;
using DMS.Desktop.Configuration.Mes;
using DMS.Desktop.Logging;
using DMS.Desktop.Services.Mes;
using DMS.Integration.Mes.Models;
using DMS.Integration.Mes.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Mes;

public partial class MesCommunicationSettingsView : UserControl
{
    public IReadOnlyList<MesModbusArea> ModbusAreas { get; } =
        Enum.GetValues<MesModbusArea>();

    public IReadOnlyList<MesDataType> ModbusDataTypes { get; } =
        Enum.GetValues<MesDataType>();

    public IReadOnlyList<MesWordOrder> ModbusWordOrders { get; } =
        Enum.GetValues<MesWordOrder>();

    public IReadOnlyList<string> ExplorerBitChoices { get; } =
        new[] { "Auto" }
            .Concat(Enumerable.Range(0, 16).Select(value => value.ToString()))
            .ToList();

    private readonly string _settingsPath;
    private readonly string _configurationRootPath;
    private readonly string _plcBindingsPath;
    private readonly DmsLogger? _logger;
    private readonly string _currentUserDisplayName;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;
    private readonly MesCommunicationSettingsService _settingsService = new();
    private readonly MesDeviceInventoryParser _deviceInventoryParser = new();
    private readonly MesPlcBindingService _plcBindingService;
    private readonly MesIntegrationSettings _integrationSettings;
    private readonly ObservableCollection<MesSignalMappingEditRow> _mappingRows = new();
    private readonly ObservableCollection<MesModbusExplorerRow> _modbusExplorerRows = new();
    private readonly List<MesModbusExplorerRow> _allModbusExplorerRows = new();
    private readonly Dictionary<string, ulong> _modbusExplorerBaseline =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly MesModbusExplorerService _modbusExplorerService = new();
    private CancellationTokenSource? _modbusExplorerCancellation;
    private MesCommunicationSettings _settings = new();
    private MesPlcBindingSet _bindingSet = new();
    private MesPlcBinding? _editingBinding;
    private MesPlcBinding? _originalBinding;
    private bool _isLoadingWorkplace;
    private bool _isViewInitialized;

    public MesCommunicationSettingsView()
        : this(
            Path.Combine(AppContext.BaseDirectory, "Config", "mes-communication-settings.json"),
            null,
            Environment.UserName)
    {
    }

    public MesCommunicationSettingsView(
        string settingsPath,
        DmsLogger? logger,
        string currentUserDisplayName,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _settingsPath = settingsPath;
        _configurationRootPath = Path.GetDirectoryName(settingsPath)
                                 ?? Path.Combine(AppContext.BaseDirectory, "Config");
        _logger = logger;
        _currentUserDisplayName = currentUserDisplayName;
        _translate = translate;
        _translateFormat = translateFormat;

        var integrationSettingsPath = Path.Combine(
            _configurationRootPath,
            "mes-integration.json");
        _integrationSettings = new MesIntegrationSettingsService(
            integrationSettingsPath).Load();

        _plcBindingsPath = MesConfigurationPathResolver.Resolve(
            _configurationRootPath,
            string.IsNullOrWhiteSpace(_integrationSettings.PlcBindingsFilePath)
                ? "mes-plc-bindings.json"
                : _integrationSettings.PlcBindingsFilePath);
        _plcBindingService = new MesPlcBindingService(_plcBindingsPath);

        GridSignalMappings.ItemsSource = _mappingRows;
        GridModbusExplorer.ItemsSource = _modbusExplorerRows;
        CmbExplorerArea.ItemsSource = ModbusAreas;
        CmbExplorerArea.SelectedItem = MesModbusArea.InputRegister;
        CmbExplorerAssignBit.ItemsSource = ExplorerBitChoices;
        CmbExplorerAssignBit.SelectedIndex = 0;
        ApplySignalMappingColumnWidths();
        Loaded += (_, _) => ApplySignalMappingColumnWidths();
        Unloaded += (_, _) => _modbusExplorerCancellation?.Cancel();

        CmbWorkplaceDriver.ItemsSource = new[]
        {
            MesDriverKeys.BrX20ModbusTcp,
            MesDriverKeys.SiemensDeferred,
            MesDriverKeys.Unconfigured
        };

        // XAML can raise Checked/TextChanged while InitializeComponent is still creating
        // the remaining controls. Filter handlers must stay dormant until the view is complete.
        _isViewInitialized = true;

        ApplyLocalization();
        LoadSettings();
        LoadWorkplaces();
    }

    private void ApplySignalMappingColumnWidths()
    {
        SetColumnWidth(ColMappingPoint, 110, 95);
        SetColumnWidth(ColMappingModule, 140, 125);
        SetColumnWidth(ColMappingSlot, 60, 55);
        SetColumnWidth(ColMappingChannel, 70, 65);
        SetColumnWidth(ColMappingLogicalSignal, 200, 170);
        SetColumnWidth(ColMappingDisplayName, 220, 180);
        SetColumnWidth(ColMappingEnabled, 80, 75);
        SetColumnWidth(ColMappingMes03, 80, 75);
        SetColumnWidth(ColMappingInverted, 90, 85);
        SetColumnWidth(ColMappingArea, 145, 130);
        SetColumnWidth(ColMappingAddress, 130, 115);
        SetColumnWidth(ColMappingDataType, 125, 110);
        SetColumnWidth(ColMappingBitIndex, 95, 85);
        SetColumnWidth(ColMappingWordOrder, 165, 145);
    }

    private static void SetColumnWidth(
        DataGridColumn column,
        double width,
        double minimumWidth)
    {
        column.MinWidth = minimumWidth;
        column.Width = new DataGridLength(width);
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("MES00.Title", "MES00 - Nastavení MES komunikace");
        TxtSubtitle.Text = T("MES00.Subtitle", "Centrální nastavení monitoringu, pracovišť, významu signálů a budoucí komunikace mezi DMS a strojem.");
        TxtMonitoringSection.Text = T("MES00.Section.Monitoring", "Monitoring");
        ChkMonitoringEnabled.Content = T("MES00.Field.MonitoringEnabled", "Povolit monitoring");
        TxtPingTimeoutLabel.Text = T("MES00.Field.PingTimeout", "Timeout pingu [ms]");
        TxtMaxParallelismLabel.Text = T("MES00.Field.MaxParallelism", "Paralelní kontroly");
        TxtAutoRefreshLabel.Text = T("MES00.Field.AutoRefresh", "Automatická kontrola [s]");
        TxtDevicesFilePathLabel.Text = T("MES00.Field.DevicesFilePath", "Soubor devices.txt");
        TxtDevicesFilePathHint.Text = T("MES00.DevicesFilePath.Hint", "Může být lokální cesta nebo UNC cesta na serveru, např. \\\\10.131.10.5\\FISData\\devices.txt.");
        TxtStationsFilePathLabel.Text = T("MES00.Field.StationsFilePath", "Soubor stanic");
        TxtStationsFilePathHint.Text = T("MES00.StationsFilePath.Hint", "Definice stanic a datových bodů pro MES03, např. mes-stations.json.");
        TxtStationSnapshotsFolderLabel.Text = T("MES00.Field.StationSnapshotsFolder", "Složka snímků stanic");
        TxtStationPollTimeoutLabel.Text = T("MES00.Field.StationPollTimeout", "Timeout čtení stanice [ms]");
        TxtStationAutoRefreshLabel.Text = T("MES00.Field.StationAutoRefresh", "Auto čtení stanic [s]");

        TxtWorkplacesSection.Text = T("MES00.Workplaces.Title", "Pracoviště a význam signálů");
        TxtWorkplacesHint.Text = T(
            "MES00.Workplaces.Hint",
            "Pracoviště se načítají ze živého devices.txt. Standardní B&R šablona přiřadí význam signálů, ale skutečné Modbus adresy je nutné doplnit z procesního obrazu X20BC0087. Adresy se zadávají jako nula-založené PDU adresy, nikoli 30001/40001.");
        TxtWorkplaceLabel.Text = T("MES00.Workplaces.Workplace", "Pracoviště / zařízení");
        TxtWorkplaceNameLabel.Text = T("MES00.Workplaces.DeviceName", "Název zařízení");
        TxtWorkplaceIpLabel.Text = T("MES00.Workplaces.IpAddress", "IP adresa");
        TxtWorkplaceDriverLabel.Text = T("MES00.Workplaces.Driver", "Ovladač");
        TxtWorkplacePortLabel.Text = T("MES00.Workplaces.Port", "Port");
        TxtWorkplaceStopTimeoutLabel.Text = T("MES00.Workplaces.StopTimeout", "Stop po [s]");
        TxtWorkplaceUnitIdLabel.Text = T("MES00.Workplaces.UnitId", "Unit ID");
        TxtWorkplacePollIntervalLabel.Text = T("MES00.Workplaces.PollInterval", "Čtení [ms]");
        TxtWorkplaceTimeoutLabel.Text = T("MES00.Workplaces.Timeout", "Timeout [ms]");
        TxtWorkplaceStaleAfterLabel.Text = T("MES00.Workplaces.StaleAfter", "Stará data [s]");
        TxtModbusAddressHint.Text = T(
            "MES00.Workplaces.ModbusAddressHint",
            "Adresa je nula-založená: první Input/Holding register = 0. Bit vyplň jen u Bool hodnoty uložené v registru.");
        TxtWorkplaceControllerLabel.Text = T("MES00.Workplaces.Controller", "Řadič");
        ChkWorkplaceEnabled.Content = T("MES00.Workplaces.Enabled", "Aktivní");
        BtnReloadWorkplaces.Content = T("MES00.Workplaces.Reload", "Načíst pracoviště");
        BtnApplyBrTemplate.Content = T("MES00.Workplaces.ApplyTemplate", "Použít B&R šablonu");
        BtnSwapCounters.Content = T("MES00.Workplaces.SwapCounters", "Prohodit čítače");
        BtnTestModbus.Content = T("MES00.Workplaces.TestModbus", "Testovat Modbus");
        BtnSaveMapping.Content = T("MES00.Workplaces.Save", "Uložit mapování");
        ColMappingPoint.Header = T("MES00.Workplaces.Column.Point", "Bod");
        ColMappingModule.Header = T("MES00.Workplaces.Column.Module", "Modul");
        ColMappingSlot.Header = T("MES00.Workplaces.Column.Slot", "Slot");
        ColMappingChannel.Header = T("MES00.Workplaces.Column.Channel", "Kanál");
        ColMappingLogicalSignal.Header = T("MES00.Workplaces.Column.LogicalSignal", "Význam signálu");
        ColMappingDisplayName.Header = T("MES00.Workplaces.Column.DisplayName", "Zobrazený název");
        ColMappingEnabled.Header = T("MES00.Workplaces.Column.Enabled", "Aktivní");
        ColMappingMes03.Header = T("MES00.Workplaces.Column.Mes03", "MES03");
        ColMappingInverted.Header = T("MES00.Workplaces.Column.Inverted", "Invertovat");
        ColMappingArea.Header = T("MES00.Workplaces.Column.Area", "Oblast");
        ColMappingAddress.Header = T("MES00.Workplaces.Column.Address", "Adresa (0-based)");
        ColMappingDataType.Header = T("MES00.Workplaces.Column.DataType", "Datový typ");
        ColMappingBitIndex.Header = T("MES00.Workplaces.Column.BitIndex", "Bit 0-15");
        ColMappingWordOrder.Header = T("MES00.Workplaces.Column.WordOrder", "Pořadí slov");

        TxtModbusExplorerSection.Text = T("MES00.ModbusExplorer.Title", "Modbus TCP průzkumník");
        TxtModbusExplorerHint.Text = T(
            "MES00.ModbusExplorer.Hint",
            "Vestavěná náhrada B&R ModbusTCP Toolboxu pro bezpečné čtení. Umí ověřit TCP port, vyhledat čitelné adresy, uložit výchozí snímek a ukázat změny po sepnutí signálu. Neobsahuje žádné Modbus zápisy ani změnu konfigurace řadiče.");
        TxtExplorerEndpointLabel.Text = T("MES00.ModbusExplorer.Endpoint", "Koncový bod");
        TxtExplorerUnitIdLabel.Text = T("MES00.ModbusExplorer.UnitId", "Unit ID");
        TxtExplorerTimeoutLabel.Text = T("MES00.ModbusExplorer.Timeout", "Timeout");
        BtnExplorerTestConnection.Content = T("MES00.ModbusExplorer.TestPort", "Ověřit port");
        TxtExplorerAreaLabel.Text = T("MES00.ModbusExplorer.Area", "Oblast");
        TxtExplorerStartLabel.Text = T("MES00.ModbusExplorer.Start", "Od adresy");
        TxtExplorerCountLabel.Text = T("MES00.ModbusExplorer.Count", "Počet");
        TxtExplorerBlockLabel.Text = T("MES00.ModbusExplorer.Block", "Blok");
        BtnExplorerScan.Content = T("MES00.ModbusExplorer.Scan", "Prohledat oblast");
        BtnExplorerQuickScan.Content = T("MES00.ModbusExplorer.QuickScan", "B&R rychlý průzkum");
        BtnExplorerCancel.Content = T("MES00.ModbusExplorer.Cancel", "Zrušit");
        TxtExplorerFilterLabel.Text = T("MES00.ModbusExplorer.Filter", "Filtr");
        ChkExplorerOnlyReadable.Content = T("MES00.ModbusExplorer.OnlyReadable", "Jen čitelné");
        ChkExplorerOnlyNonZero.Content = T("MES00.ModbusExplorer.OnlyNonZero", "Jen nenulové");
        ChkExplorerOnlyChanged.Content = T("MES00.ModbusExplorer.OnlyChanged", "Jen změněné");
        BtnExplorerCaptureBaseline.Content = T("MES00.ModbusExplorer.Baseline", "Uložit výchozí snímek");
        TxtExplorerBitLabel.Text = T("MES00.ModbusExplorer.Bit", "Bit");
        BtnExplorerAssign.Content = T("MES00.ModbusExplorer.Assign", "Přiřadit vybranému bodu");
        TxtExplorerAssignmentHint.Text = T(
            "MES00.ModbusExplorer.AssignHint",
            "Vyber řádek v průzkumníku a současně datový bod v mapování výše. U Bool hodnoty v registru zvol bit, nebo ponech Auto po porovnání změn.");
        ColExplorerArea.Header = T("MES00.ModbusExplorer.Column.Area", "Oblast");
        ColExplorerAddress.Header = T("MES00.ModbusExplorer.Column.Address", "Adresa");
        ColExplorerValue.Header = T("MES00.ModbusExplorer.Column.Value", "Hodnota");
        ColExplorerHex.Header = T("MES00.ModbusExplorer.Column.Hex", "Hex");
        ColExplorerBinary.Header = T("MES00.ModbusExplorer.Column.Binary", "Binárně");
        ColExplorerSigned.Header = T("MES00.ModbusExplorer.Column.Signed", "Int16");
        ColExplorerPrevious.Header = T("MES00.ModbusExplorer.Column.Previous", "Předchozí");
        ColExplorerDelta.Header = T("MES00.ModbusExplorer.Column.Delta", "Rozdíl");
        ColExplorerChangedBits.Header = T("MES00.ModbusExplorer.Column.ChangedBits", "Změněné bity");
        ColExplorerStatus.Header = T("MES00.ModbusExplorer.Column.Status", "Stav");
        ColExplorerError.Header = T("MES00.ModbusExplorer.Column.Error", "Chyba");
        ColExplorerReadAt.Header = T("MES00.ModbusExplorer.Column.ReadAt", "Čtení");

        TxtUnlockSection.Text = T("MES00.Section.Unlock", "Budoucí signál pro stroj");
        TxtUnlockWarning.Text = T("MES00.Unlock.Warning", "DMS nebude bezpečnostní systém stroje. DMS může pouze dodat ověřený signál nastavení, vlastní blokaci a bezpečnostní rozhodnutí musí řešit PLC / B&R logika.");
        ChkEnableUnlockSignal.Content = T("MES00.Field.EnableUnlock", "Povolit signál DMS_SETUP_OK");
        TxtUnlockProviderLabel.Text = T("MES00.Field.UnlockProvider", "Typ komunikace");
        TxtGatewayHostLabel.Text = T("MES00.Field.GatewayHost", "Gateway host");
        TxtGatewayPortLabel.Text = T("MES00.Field.GatewayPort", "Gateway port");
        TxtHandshakeFolderLabel.Text = T("MES00.Field.HandshakeFolder", "Sdílená složka handshake");
        TxtValidityLabel.Text = T("MES00.Field.Validity", "Platnost povolení [min]");
        ChkRequireOperatorConfirmation.Content = T("MES00.Field.RequireOperator", "Vyžadovat potvrzení operátora");
        BtnReload.Content = T("MES00.Button.Reload", "Načíst");
        BtnSave.Content = T("MES00.Button.Save", "Uložit");
    }

    private void LoadSettings()
    {
        _settings = _settingsService.Load(_settingsPath);
        FillUi(_settings);
        TxtSettingsPath.Text = TF("MES00.SettingsPath", "Soubor nastavení: {0}", _settingsPath);
        TxtStatusLine.Text = T("MES00.Status.Loaded", "Nastavení bylo načteno.");
        _logger?.AdminAction(
            "MES00",
            "LoadMesCommunicationSettings",
            _currentUserDisplayName,
            $"File={_settingsPath}");
    }

    private void SaveSettings()
    {
        try
        {
            var settings = ReadUi();
            settings.Normalize();
            _settingsService.Save(_settingsPath, settings);
            _settings = settings;
            FillUi(_settings);
            TxtStatusLine.Text = T("MES00.Status.Saved", "Nastavení bylo uloženo.");
            _logger?.AdminAction(
                "MES00",
                "SaveMesCommunicationSettings",
                _currentUserDisplayName,
                $"File={_settingsPath}; Monitoring={settings.IsMonitoringEnabled}; TimeoutMs={settings.PingTimeoutMs}; MaxParallelism={settings.MaxParallelism}; AutoRefreshSeconds={settings.AutoRefreshSeconds}; DevicesFilePath={settings.DevicesFilePath}; StationsFilePath={settings.StationsFilePath}; StationPollTimeoutMs={settings.StationPollTimeoutMs}; StationAutoRefreshSeconds={settings.StationAutoRefreshSeconds}; UnlockEnabled={settings.EnableMachineUnlockSignal}; Provider={settings.UnlockProvider}");

            LoadWorkplaces(GetSelectedStationCode());
        }
        catch (Exception ex)
        {
            TxtStatusLine.Text = TF("MES00.Status.SaveFailed", "Uložení selhalo: {0}", ex.Message);
            _logger?.AdminAction(
                "MES00",
                "SaveMesCommunicationSettingsFailed",
                _currentUserDisplayName,
                $"File={_settingsPath}; Error={ex.Message}");
        }
    }

    private void FillUi(MesCommunicationSettings settings)
    {
        ChkMonitoringEnabled.IsChecked = settings.IsMonitoringEnabled;
        TxtPingTimeout.Text = settings.PingTimeoutMs.ToString();
        TxtMaxParallelism.Text = settings.MaxParallelism.ToString();
        TxtAutoRefresh.Text = settings.AutoRefreshSeconds.ToString();
        TxtDevicesFilePath.Text = settings.DevicesFilePath;
        TxtStationsFilePath.Text = settings.StationsFilePath;
        TxtStationSnapshotsFolder.Text = settings.StationSnapshotsFolder;
        TxtStationPollTimeout.Text = settings.StationPollTimeoutMs.ToString();
        TxtStationAutoRefresh.Text = settings.StationAutoRefreshSeconds.ToString();
        ChkEnableUnlockSignal.IsChecked = settings.EnableMachineUnlockSignal;
        SelectComboValue(CmbUnlockProvider, settings.UnlockProvider);
        TxtGatewayHost.Text = settings.GatewayHost;
        TxtGatewayPort.Text = settings.GatewayPort.ToString();
        TxtHandshakeFolder.Text = settings.SharedHandshakeFolder;
        TxtValidityMinutes.Text = settings.SetupOkValidMinutes.ToString();
        ChkRequireOperatorConfirmation.IsChecked = settings.RequireOperatorConfirmation;
    }

    private MesCommunicationSettings ReadUi()
    {
        return new MesCommunicationSettings
        {
            IsMonitoringEnabled = ChkMonitoringEnabled.IsChecked == true,
            PingTimeoutMs = ReadInt(TxtPingTimeout.Text, 1200),
            MaxParallelism = ReadInt(TxtMaxParallelism.Text, 16),
            AutoRefreshSeconds = ReadInt(TxtAutoRefresh.Text, 60),
            DevicesFilePath = TxtDevicesFilePath.Text?.Trim() ?? string.Empty,
            StationsFilePath = TxtStationsFilePath.Text?.Trim() ?? string.Empty,
            StationSnapshotsFolder = TxtStationSnapshotsFolder.Text?.Trim() ?? string.Empty,
            StationPollTimeoutMs = ReadInt(TxtStationPollTimeout.Text, 1500),
            StationAutoRefreshSeconds = ReadInt(TxtStationAutoRefresh.Text, 10),
            EnableMachineUnlockSignal = ChkEnableUnlockSignal.IsChecked == true,
            UnlockProvider = GetComboValue(CmbUnlockProvider),
            GatewayHost = TxtGatewayHost.Text?.Trim() ?? string.Empty,
            GatewayPort = ReadInt(TxtGatewayPort.Text, 0),
            SharedHandshakeFolder = TxtHandshakeFolder.Text?.Trim() ?? string.Empty,
            SetupOkValidMinutes = ReadInt(TxtValidityMinutes.Text, 480),
            RequireOperatorConfirmation = ChkRequireOperatorConfirmation.IsChecked == true
        };
    }

    private void LoadWorkplaces(string? stationToKeep = null)
    {
        _isLoadingWorkplace = true;

        try
        {
            _bindingSet = _plcBindingService.Load();
            var devicesPath = ResolveConfiguredPath(
                _settings.DevicesFilePath,
                Path.Combine(_configurationRootPath, "devices.txt"),
                "devices.txt");
            var inventory = _deviceInventoryParser.Load(devicesPath);

            var workplaces = new List<MesWorkplaceBindingItem>();
            var knownStations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var device in inventory.Devices.Where(device => device.IsMachine))
            {
                var stationCode = device.StationCode;
                var hasBinding = FindBinding(stationCode) is not null;

                workplaces.Add(new MesWorkplaceBindingItem
                {
                    Device = device,
                    StationCode = stationCode,
                    IpAddress = device.IpAddress,
                    DeviceName = device.Name,
                    SuggestedDriver = device.SuggestedDriver,
                    HasBinding = hasBinding
                });
                knownStations.Add(stationCode);
            }

            foreach (var binding in _bindingSet.Devices.Where(binding =>
                         !string.IsNullOrWhiteSpace(binding.StationCode) &&
                         !knownStations.Contains(binding.StationCode)))
            {
                workplaces.Add(new MesWorkplaceBindingItem
                {
                    StationCode = binding.StationCode,
                    IpAddress = binding.IpAddressOverride ?? string.Empty,
                    DeviceName = T(
                        "MES00.Workplaces.OrphanBinding",
                        "Mapování bez záznamu v devices.txt"),
                    SuggestedDriver = binding.Driver,
                    HasBinding = true
                });
            }

            workplaces = workplaces
                .OrderBy(item => item.StationCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            CmbWorkplace.ItemsSource = workplaces;
            TxtMappingPath.Text = TF(
                "MES00.Workplaces.MappingPath",
                "Mapování PLC: {0} | devices.txt: {1}",
                _plcBindingsPath,
                devicesPath);

            var selected = workplaces.FirstOrDefault(item =>
                               string.Equals(
                                   item.StationCode,
                                   stationToKeep,
                                   StringComparison.OrdinalIgnoreCase))
                           ?? workplaces.FirstOrDefault(item =>
                               string.Equals(
                                   item.StationCode,
                                   "X5-1",
                                   StringComparison.OrdinalIgnoreCase))
                           ?? workplaces.FirstOrDefault();

            CmbWorkplace.SelectedItem = selected;

            if (!inventory.Success)
            {
                TxtMappingStatus.Text = string.Join(" ", inventory.Errors);
            }
            else if (inventory.Errors.Count > 0)
            {
                TxtMappingStatus.Text = TF(
                    "MES00.Workplaces.InventoryWarnings",
                    "Pracoviště byla načtena, ale devices.txt obsahuje {0} upozornění.",
                    inventory.Errors.Count);
            }
            else
            {
                TxtMappingStatus.Text = TF(
                    "MES00.Workplaces.Loaded",
                    "Načteno {0} pracovišť typu STROJ.",
                    workplaces.Count);
            }
        }
        catch (Exception ex)
        {
            CmbWorkplace.ItemsSource = Array.Empty<MesWorkplaceBindingItem>();
            _mappingRows.Clear();
            TxtMappingStatus.Text = TF(
                "MES00.Workplaces.LoadFailed",
                "Pracoviště nebo mapování se nepodařilo načíst: {0}",
                ex.Message);
            _logger?.AdminAction(
                "MES00",
                "LoadPlcSignalMappingsFailed",
                _currentUserDisplayName,
                $"Bindings={_plcBindingsPath}; Error={ex.Message}");
        }
        finally
        {
            _isLoadingWorkplace = false;
            DisplaySelectedWorkplace();
        }
    }

    private void DisplaySelectedWorkplace()
    {
        if (_isLoadingWorkplace || CmbWorkplace.SelectedItem is not MesWorkplaceBindingItem item)
        {
            return;
        }

        _originalBinding = CloneBinding(FindBinding(item.StationCode));
        _editingBinding = CloneBinding(_originalBinding) ?? new MesPlcBinding
        {
            StationCode = item.StationCode,
            Driver = item.SuggestedDriver,
            Enabled = true,
            Port = string.Equals(
                item.SuggestedDriver,
                MesDriverKeys.BrX20ModbusTcp,
                StringComparison.OrdinalIgnoreCase)
                ? 502
                : 102,
            StopTimeoutSeconds = 30,
            Controller = string.Equals(
                item.SuggestedDriver,
                MesDriverKeys.BrX20ModbusTcp,
                StringComparison.OrdinalIgnoreCase)
                ? "B&R X20BC0087"
                : string.Empty
        };

        TxtWorkplaceName.Text = item.DeviceName;
        TxtWorkplaceIp.Text = item.IpAddress;
        CmbWorkplaceDriver.SelectedItem = _editingBinding.Driver;
        if (CmbWorkplaceDriver.SelectedItem is null)
        {
            CmbWorkplaceDriver.SelectedItem = MesDriverKeys.Unconfigured;
        }

        TxtWorkplacePort.Text = _editingBinding.Port.ToString();
        TxtWorkplaceStopTimeout.Text = _editingBinding.StopTimeoutSeconds.ToString();
        TxtWorkplaceUnitId.Text = _editingBinding.UnitId.ToString();
        TxtWorkplacePollInterval.Text = _editingBinding.PollIntervalMs.ToString();
        TxtWorkplaceTimeout.Text = _editingBinding.TimeoutMs.ToString();
        TxtWorkplaceStaleAfter.Text = _editingBinding.StaleAfterSeconds.ToString();
        TxtWorkplaceController.Text = _editingBinding.Controller;
        ChkWorkplaceEnabled.IsChecked = _editingBinding.Enabled;

        FillMappingRows(_editingBinding.DataPoints);

        TxtMappingStatus.Text = _originalBinding is null
            ? T("MES00.Workplaces.NoBinding", "Pracoviště zatím nemá uložené PLC mapování. Pro běžnou B&R stanici použij standardní šablonu.")
            : TF(
                "MES00.Workplaces.BindingLoaded",
                "Mapování pracoviště {0} bylo načteno. Datové body: {1}.",
                item.StationCode,
                _mappingRows.Count);

        ResetModbusExplorerForWorkplace(item);
    }

    private void FillMappingRows(IEnumerable<MesDataPointDefinition> definitions)
    {
        _mappingRows.Clear();
        foreach (var definition in definitions
                     .OrderBy(point => point.Slot)
                     .ThenBy(point => point.Channel)
                     .ThenBy(point => point.Code, StringComparer.OrdinalIgnoreCase))
        {
            _mappingRows.Add(MesSignalMappingEditRow.FromDefinition(definition));
        }
    }

    private void ApplyStandardBrTemplate()
    {
        if (CmbWorkplace.SelectedItem is not MesWorkplaceBindingItem item)
        {
            TxtMappingStatus.Text = T("MES00.Workplaces.SelectFirst", "Nejdřív vyber pracoviště.");
            return;
        }

        CaptureWorkplaceHeader();
        _editingBinding = MesPlcBindingTemplateFactory.CreateStandardBrX20(
            item.StationCode,
            _editingBinding);
        _editingBinding.IpAddressOverride = _originalBinding?.IpAddressOverride;

        CmbWorkplaceDriver.SelectedItem = _editingBinding.Driver;
        TxtWorkplacePort.Text = _editingBinding.Port.ToString();
        TxtWorkplaceStopTimeout.Text = _editingBinding.StopTimeoutSeconds.ToString();
        TxtWorkplaceUnitId.Text = _editingBinding.UnitId.ToString();
        TxtWorkplacePollInterval.Text = _editingBinding.PollIntervalMs.ToString();
        TxtWorkplaceTimeout.Text = _editingBinding.TimeoutMs.ToString();
        TxtWorkplaceStaleAfter.Text = _editingBinding.StaleAfterSeconds.ToString();
        TxtWorkplaceController.Text = _editingBinding.Controller;
        ChkWorkplaceEnabled.IsChecked = _editingBinding.Enabled;
        FillMappingRows(_editingBinding.DataPoints);

        TxtMappingStatus.Text = T(
            "MES00.Workplaces.TemplateApplied",
            "Standardní B&R šablona byla použita v editoru. Potvrď ji tlačítkem Uložit mapování.");
    }

    private void SwapCounters()
    {
        GridSignalMappings.CommitEdit(DataGridEditingUnit.Cell, true);
        GridSignalMappings.CommitEdit(DataGridEditingUnit.Row, true);

        var counter1 = _mappingRows.FirstOrDefault(row =>
            string.Equals(row.Code, "Counter1", StringComparison.OrdinalIgnoreCase));
        var counter2 = _mappingRows.FirstOrDefault(row =>
            string.Equals(row.Code, "Counter2", StringComparison.OrdinalIgnoreCase));

        if (counter1 is null || counter2 is null)
        {
            TxtMappingStatus.Text = T(
                "MES00.Workplaces.CountersMissing",
                "V mapování nejsou současně dostupné body Counter1 a Counter2.");
            return;
        }

        (counter1.LogicalSignal, counter2.LogicalSignal) =
            (counter2.LogicalSignal, counter1.LogicalSignal);
        (counter1.DisplayName, counter2.DisplayName) =
            (counter2.DisplayName, counter1.DisplayName);
        (counter1.Enabled, counter2.Enabled) =
            (counter2.Enabled, counter1.Enabled);
        (counter1.VisibleInMes03, counter2.VisibleInMes03) =
            (counter2.VisibleInMes03, counter1.VisibleInMes03);

        GridSignalMappings.Items.Refresh();
        TxtMappingStatus.Text = T(
            "MES00.Workplaces.CountersSwapped",
            "Význam Counter1 a Counter2 byl prohozen v editoru. Změnu ještě ulož.");
    }

    private void SaveSelectedMapping()
    {
        if (CmbWorkplace.SelectedItem is not MesWorkplaceBindingItem item)
        {
            TxtMappingStatus.Text = T("MES00.Workplaces.SelectFirst", "Nejdřív vyber pracoviště.");
            return;
        }

        try
        {
            GridSignalMappings.CommitEdit(DataGridEditingUnit.Cell, true);
            GridSignalMappings.CommitEdit(DataGridEditingUnit.Row, true);
            CaptureWorkplaceHeader();

            if (_editingBinding is null)
            {
                throw new InvalidOperationException("PLC binding editor is not initialized.");
            }

            _editingBinding.StationCode = item.StationCode;
            _editingBinding.DataPoints = _mappingRows
                .Select(row => row.ToDefinition())
                .ToList();

            if (string.Equals(
                    _editingBinding.Driver,
                    MesDriverKeys.BrX20ModbusTcp,
                    StringComparison.OrdinalIgnoreCase) &&
                _editingBinding.Modules.Count == 0)
            {
                _editingBinding.Modules = MesPlcBindingTemplateFactory.CreateStandardModules();
            }

            // Reload immediately before writing so editing one workplace never
            // overwrites mappings saved for other workplaces by another client.
            var latestBindingSet = _plcBindingService.Load();
            if (File.Exists(_plcBindingsPath) &&
                !string.IsNullOrWhiteSpace(_plcBindingService.LastError))
            {
                throw new InvalidOperationException(
                    "Existing PLC mapping could not be loaded safely. " +
                    _plcBindingService.LastError);
            }

            var previousBinding = CloneBinding(latestBindingSet.Devices.FirstOrDefault(binding =>
                string.Equals(
                    binding.StationCode,
                    item.StationCode,
                    StringComparison.OrdinalIgnoreCase)));
            var existingIndex = latestBindingSet.Devices.FindIndex(binding =>
                string.Equals(
                    binding.StationCode,
                    item.StationCode,
                    StringComparison.OrdinalIgnoreCase));

            var savedBinding = CloneBinding(_editingBinding)!;
            if (existingIndex >= 0)
            {
                latestBindingSet.Devices[existingIndex] = savedBinding;
            }
            else
            {
                latestBindingSet.Devices.Add(savedBinding);
            }

            _plcBindingService.Save(latestBindingSet);
            _bindingSet = latestBindingSet;
            LogBindingAudit(previousBinding, savedBinding);
            _logger?.AdminAction(
                "MES00",
                "SavePlcSignalMapping",
                _currentUserDisplayName,
                $"Station={savedBinding.StationCode}; Driver={savedBinding.Driver}; Points={savedBinding.DataPoints.Count}; File={_plcBindingsPath}");

            TxtMappingStatus.Text = TF(
                "MES00.Workplaces.MappingSaved",
                "Mapování pracoviště {0} bylo uloženo. Předchozí soubor byl automaticky zazálohován.",
                savedBinding.StationCode);

            LoadWorkplaces(savedBinding.StationCode);
        }
        catch (Exception ex)
        {
            TxtMappingStatus.Text = TF(
                "MES00.Workplaces.SaveFailed",
                "Mapování se nepodařilo uložit: {0}",
                ex.Message);
            _logger?.AdminAction(
                "MES00",
                "SavePlcSignalMappingFailed",
                _currentUserDisplayName,
                $"Station={item.StationCode}; File={_plcBindingsPath}; Error={ex.Message}");
        }
    }

    private async Task TestSelectedModbusAsync()
    {
        if (CmbWorkplace.SelectedItem is not MesWorkplaceBindingItem item)
        {
            TxtMappingStatus.Text = T(
                "MES00.Workplaces.SelectFirst",
                "Nejdřív vyber pracoviště.");
            return;
        }

        try
        {
            GridSignalMappings.CommitEdit(DataGridEditingUnit.Cell, true);
            GridSignalMappings.CommitEdit(DataGridEditingUnit.Row, true);
            CaptureWorkplaceHeader();

            if (_editingBinding is null)
            {
                throw new InvalidOperationException(
                    "PLC binding editor is not initialized.");
            }

            var testBinding = CloneBinding(_editingBinding)!;
            testBinding.StationCode = item.StationCode;
            testBinding.DataPoints = _mappingRows
                .Select(row => row.ToDefinition())
                .ToList();

            var mappedPoints = testBinding.DataPoints
                .Where(point => point.Enabled && point.Source?.Address is not null)
                .ToList();

            if (mappedPoints.Count == 0)
            {
                TxtMappingStatus.Text = T(
                    "MES00.Workplaces.Test.NoMappedPoints",
                    "Spojení má IP a port, ale není co číst. Doplň alespoň jednu skutečnou Modbus adresu a test zopakuj.");
                return;
            }

            var device = item.Device ?? new MesDeviceEntry
            {
                IpAddress = item.IpAddress,
                DeviceType = "STROJ",
                Name = item.DeviceName
            };

            BtnTestModbus.IsEnabled = false;
            TxtMappingStatus.Text = TF(
                "MES00.Workplaces.Test.Running",
                "Testuji Modbus {0}:{1}, Unit ID {2}...",
                device.IpAddress,
                testBinding.Port,
                testBinding.UnitId);

            var readService = new MesDataPointReadService();

            try
            {
                using var cancellation = new CancellationTokenSource(
                    TimeSpan.FromMilliseconds(
                        Math.Clamp(testBinding.TimeoutMs + 1500, 1000, 35_000)));

                var snapshot = await readService.ReadSnapshotAsync(
                    device,
                    testBinding,
                    _integrationSettings,
                    cancellation.Token);

                var validCount = snapshot.DataPoints.Count(point =>
                    point.Quality == MesDataPointQuality.Valid);
                var invalidCount = snapshot.DataPoints.Count(point =>
                    point.Quality is MesDataPointQuality.Invalid
                        or MesDataPointQuality.Offline
                        or MesDataPointQuality.ConfigurationError);

                TxtMappingStatus.Text = snapshot.IsOnline
                    ? TF(
                        "MES00.Workplaces.Test.Success",
                        "Modbus čtení je funkční. Platné body: {0}/{1}; chyby bodů: {2}. Hodnoty zkontroluj v MESDPM.",
                        validCount,
                        mappedPoints.Count,
                        invalidCount)
                    : TF(
                        "MES00.Workplaces.Test.Failed",
                        "Modbus test selhal: {0}",
                        snapshot.StatusMessage);

                _logger?.AdminAction(
                    "MES00",
                    snapshot.IsOnline ? "TestModbusOk" : "TestModbusFailed",
                    _currentUserDisplayName,
                    $"Station={item.StationCode}; IP={device.IpAddress}; Port={testBinding.Port}; UnitId={testBinding.UnitId}; Valid={validCount}; Mapped={mappedPoints.Count}; Status={snapshot.StatusMessage}");
            }
            finally
            {
                await readService.DisposeAsync();
            }
        }
        catch (OperationCanceledException)
        {
            TxtMappingStatus.Text = T(
                "MES00.Workplaces.Test.Timeout",
                "Modbus test překročil nastavený časový limit.");
        }
        catch (Exception ex)
        {
            TxtMappingStatus.Text = TF(
                "MES00.Workplaces.Test.Exception",
                "Modbus test se nepodařil: {0}",
                ex.Message);

            _logger?.AdminAction(
                "MES00",
                "TestModbusException",
                _currentUserDisplayName,
                $"Station={item.StationCode}; IP={item.IpAddress}; Error={ex.Message}");
        }
        finally
        {
            BtnTestModbus.IsEnabled = true;
        }
    }

    private void ResetModbusExplorerForWorkplace(MesWorkplaceBindingItem item)
    {
        _modbusExplorerCancellation?.Cancel();
        _allModbusExplorerRows.Clear();
        _modbusExplorerRows.Clear();
        _modbusExplorerBaseline.Clear();

        var port = _editingBinding?.Port > 0 ? _editingBinding.Port : 502;
        var unitId = _editingBinding?.UnitId ?? 0;
        var timeout = _editingBinding?.TimeoutMs > 0 ? _editingBinding.TimeoutMs : 3000;
        var ipAddress = string.IsNullOrWhiteSpace(_editingBinding?.IpAddressOverride)
            ? item.IpAddress
            : _editingBinding!.IpAddressOverride!.Trim();

        TxtExplorerEndpoint.Text = $"{ipAddress}:{port}";
        TxtExplorerUnitId.Text = unitId.ToString();
        TxtExplorerTimeout.Text = timeout.ToString();
        TxtExplorerConnectionStatus.Text = T(
            "MES00.ModbusExplorer.NotTested",
            "Spojení zatím nebylo ověřeno.");
        TxtExplorerProgress.Text = string.Empty;
        TxtExplorerStatus.Text = T(
            "MES00.ModbusExplorer.Ready",
            "Začni ověřením portu. Potom prohledej jednu oblast nebo použij rychlý B&R průzkum adres 0 až 63 ve všech čtyřech Modbus oblastech.");
    }

    private async Task TestExplorerConnectionAsync()
    {
        if (!TryGetExplorerConnection(out var connection, out var error))
        {
            TxtExplorerStatus.Text = error;
            return;
        }

        SetExplorerBusy(true);
        TxtExplorerConnectionStatus.Text = T(
            "MES00.ModbusExplorer.Testing",
            "Ověřuji TCP port...");

        try
        {
            using var cancellation = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(Math.Clamp(connection.TimeoutMs + 500, 750, 31_000)));

            var result = await _modbusExplorerService.TestTcpConnectionAsync(
                connection.IpAddress,
                connection.Port,
                connection.TimeoutMs,
                cancellation.Token);

            TxtExplorerConnectionStatus.Text = result.Success
                ? TF(
                    "MES00.ModbusExplorer.PortOnline",
                    "TCP port je dostupný ({0:0} ms). Modbus protokol ověř prohledáním oblasti.",
                    result.Elapsed.TotalMilliseconds)
                : TF(
                    "MES00.ModbusExplorer.PortOffline",
                    "TCP port není dostupný: {0}",
                    result.Message);

            _logger?.AdminAction(
                "MES00",
                result.Success ? "ModbusExplorerTcpOk" : "ModbusExplorerTcpFailed",
                _currentUserDisplayName,
                $"IP={connection.IpAddress}; Port={connection.Port}; ElapsedMs={result.Elapsed.TotalMilliseconds:0}; Error={result.Message}");
        }
        catch (Exception ex)
        {
            TxtExplorerConnectionStatus.Text = TF(
                "MES00.ModbusExplorer.PortException",
                "Ověření portu selhalo: {0}",
                ex.Message);
        }
        finally
        {
            SetExplorerBusy(false);
        }
    }

    private async Task ScanExplorerAreaAsync()
    {
        if (CmbExplorerArea.SelectedItem is not MesModbusArea area)
        {
            TxtExplorerStatus.Text = T(
                "MES00.ModbusExplorer.SelectArea",
                "Vyber Modbus oblast.");
            return;
        }

        var start = ReadInt(TxtExplorerStart.Text, 0);
        var count = Math.Clamp(ReadInt(TxtExplorerCount.Text, 64), 1, 2048);
        var block = Math.Clamp(ReadInt(TxtExplorerBlock.Text, 16), 1, 2000);

        await RunExplorerScansAsync(
            new[] { new ExplorerScanRequest(area, start, count, block) },
            clearPrevious: true);
    }

    private async Task QuickScanExplorerAsync()
    {
        // Conservative first pass. It is intentionally small to avoid flooding a live controller.
        // The user can expand the range after the readable process image becomes visible.
        var requests = new[]
        {
            new ExplorerScanRequest(MesModbusArea.InputRegister, 0, 64, 16),
            new ExplorerScanRequest(MesModbusArea.HoldingRegister, 0, 64, 16),
            new ExplorerScanRequest(MesModbusArea.DiscreteInput, 0, 64, 16),
            new ExplorerScanRequest(MesModbusArea.Coil, 0, 64, 16)
        };

        await RunExplorerScansAsync(requests, clearPrevious: true);
    }

    private async Task RunExplorerScansAsync(
        IReadOnlyList<ExplorerScanRequest> requests,
        bool clearPrevious)
    {
        if (!TryGetExplorerConnection(out var connection, out var error))
        {
            TxtExplorerStatus.Text = error;
            return;
        }

        _modbusExplorerCancellation?.Cancel();
        _modbusExplorerCancellation?.Dispose();
        _modbusExplorerCancellation = new CancellationTokenSource();

        if (clearPrevious)
        {
            _allModbusExplorerRows.Clear();
            _modbusExplorerRows.Clear();
        }

        SetExplorerBusy(true);
        TxtExplorerStatus.Text = TF(
            "MES00.ModbusExplorer.ScanRunning",
            "Čtu {0}:{1}, Unit ID {2}. Průzkumník je pouze pro čtení.",
            connection.IpAddress,
            connection.Port,
            connection.UnitId);

        try
        {
            var collected = new List<MesModbusExplorerValue>();

            foreach (var request in requests)
            {
                _modbusExplorerCancellation.Token.ThrowIfCancellationRequested();

                var progress = new Progress<MesModbusScanProgress>(value =>
                {
                    TxtExplorerProgress.Text = TF(
                        "MES00.ModbusExplorer.Progress",
                        "{0}: {1}/{2}",
                        value.Area,
                        value.Completed,
                        value.Total);
                });

                var values = await _modbusExplorerService.ScanAsync(
                    connection.IpAddress,
                    connection.Port,
                    connection.UnitId,
                    connection.TimeoutMs,
                    request.Area,
                    request.StartAddress,
                    request.Count,
                    request.BlockSize,
                    progress,
                    _modbusExplorerCancellation.Token);

                collected.AddRange(values);
            }

            _allModbusExplorerRows.Clear();
            _allModbusExplorerRows.AddRange(collected
                .GroupBy(value => new MesModbusExplorerAddress(value.Area, value.Address))
                .Select(group => group.Last())
                .Select(value => MesModbusExplorerRow.FromValue(
                    value,
                    _modbusExplorerBaseline))
                .OrderBy(row => row.Area)
                .ThenBy(row => row.Address));

            RefreshExplorerFilter();

            var readableCount = _allModbusExplorerRows.Count(row => row.IsReadable);
            var changedCount = _allModbusExplorerRows.Count(row => row.HasChanged);
            var nonZeroCount = _allModbusExplorerRows.Count(row => row.IsReadable && row.IsNonZero);

            TxtExplorerConnectionStatus.Text = readableCount > 0
                ? T(
                    "MES00.ModbusExplorer.ProtocolOk",
                    "Modbus odpovídá. Byla nalezena čitelná data.")
                : T(
                    "MES00.ModbusExplorer.NoReadable",
                    "TCP port odpovídá, ale v prohledaném rozsahu nebyla nalezena čitelná Modbus adresa.");

            TxtExplorerStatus.Text = TF(
                "MES00.ModbusExplorer.ScanFinished",
                "Průzkum dokončen. Čitelné adresy: {0}; nenulové: {1}; změněné proti výchozímu snímku: {2}. Chybné bloky lze zpřesnit menší velikostí bloku.",
                readableCount,
                nonZeroCount,
                changedCount);

            _logger?.AdminAction(
                "MES00",
                "ModbusExplorerScan",
                _currentUserDisplayName,
                $"IP={connection.IpAddress}; Port={connection.Port}; UnitId={connection.UnitId}; Requests={requests.Count}; Readable={readableCount}; NonZero={nonZeroCount}; Changed={changedCount}");
        }
        catch (OperationCanceledException)
        {
            TxtExplorerStatus.Text = T(
                "MES00.ModbusExplorer.Cancelled",
                "Průzkum byl zrušen.");
        }
        catch (Exception ex)
        {
            TxtExplorerStatus.Text = TF(
                "MES00.ModbusExplorer.ScanFailed",
                "Průzkum Modbus selhal: {0}",
                ex.Message);
            TxtExplorerConnectionStatus.Text = T(
                "MES00.ModbusExplorer.ProtocolFailed",
                "Modbus čtení selhalo.");

            _logger?.AdminAction(
                "MES00",
                "ModbusExplorerScanFailed",
                _currentUserDisplayName,
                $"IP={connection.IpAddress}; Port={connection.Port}; UnitId={connection.UnitId}; Error={ex.Message}");
        }
        finally
        {
            TxtExplorerProgress.Text = string.Empty;
            SetExplorerBusy(false);
        }
    }

    private void CaptureExplorerBaseline()
    {
        var readableRows = _allModbusExplorerRows
            .Where(row => row.IsReadable)
            .ToList();

        if (readableRows.Count == 0)
        {
            TxtExplorerStatus.Text = T(
                "MES00.ModbusExplorer.BaselineEmpty",
                "Nejdřív načti alespoň jednu čitelnou Modbus adresu.");
            return;
        }

        _modbusExplorerBaseline.Clear();
        foreach (var row in readableRows)
        {
            _modbusExplorerBaseline[row.Key] = row.NumericValue;
        }

        TxtExplorerStatus.Text = TF(
            "MES00.ModbusExplorer.BaselineSaved",
            "Výchozí snímek byl uložen pro {0} adres. Nyní sepni nebo vypni hledaný signál a spusť stejný průzkum znovu; použij filtr Jen změněné.",
            readableRows.Count);
    }

    private void AssignExplorerAddressToMapping()
    {
        if (GridModbusExplorer.SelectedItem is not MesModbusExplorerRow explorerRow ||
            !explorerRow.IsReadable)
        {
            TxtExplorerStatus.Text = T(
                "MES00.ModbusExplorer.SelectReadable",
                "Vyber čitelný řádek v Modbus průzkumníku.");
            return;
        }

        if (GridSignalMappings.SelectedItem is not MesSignalMappingEditRow mappingRow)
        {
            TxtExplorerStatus.Text = T(
                "MES00.ModbusExplorer.SelectMapping",
                "Vyber také datový bod v tabulce mapování výše, například Counter1 nebo Input1.");
            return;
        }

        mappingRow.Area = explorerRow.Area;
        mappingRow.AddressText = explorerRow.Address.ToString();

        if (explorerRow.Area is MesModbusArea.Coil or MesModbusArea.DiscreteInput)
        {
            mappingRow.DataType = MesDataType.Bool;
            mappingRow.BitIndexText = string.Empty;
        }
        else if (mappingRow.Code.StartsWith("Counter", StringComparison.OrdinalIgnoreCase))
        {
            mappingRow.DataType = MesDataType.UInt16;
            mappingRow.BitIndexText = string.Empty;
        }
        else
        {
            mappingRow.DataType = MesDataType.Bool;

            var selectedBitText = CmbExplorerAssignBit.SelectedItem?.ToString() ?? "Auto";
            int? bitIndex = int.TryParse(selectedBitText, out var explicitBit)
                ? explicitBit
                : explorerRow.SuggestedBitIndex;

            if (!bitIndex.HasValue)
            {
                TxtExplorerStatus.Text = T(
                    "MES00.ModbusExplorer.BitRequired",
                    "U Bool signálu uloženého v registru zvol konkrétní bit 0 až 15. Režim Auto funguje, když se proti výchozímu snímku změnil právě jeden bit.");
                return;
            }

            mappingRow.BitIndexText = bitIndex.Value.ToString();
        }

        GridSignalMappings.Items.Refresh();
        GridSignalMappings.ScrollIntoView(mappingRow);

        TxtExplorerStatus.Text = TF(
            "MES00.ModbusExplorer.Assigned",
            "{0} byl přiřazen na {1}[{2}]{3}. Změna je zatím jen v editoru; potvrď ji tlačítkem Uložit mapování.",
            mappingRow.Code,
            explorerRow.Area,
            explorerRow.Address,
            string.IsNullOrWhiteSpace(mappingRow.BitIndexText)
                ? string.Empty
                : $" / bit {mappingRow.BitIndexText}");
    }

    private void RefreshExplorerFilter()
    {
        if (!_isViewInitialized ||
            TxtExplorerFilter is null ||
            ChkExplorerOnlyReadable is null ||
            ChkExplorerOnlyNonZero is null ||
            ChkExplorerOnlyChanged is null ||
            GridModbusExplorer is null)
        {
            return;
        }

        var filter = TxtExplorerFilter.Text?.Trim() ?? string.Empty;
        var onlyReadable = ChkExplorerOnlyReadable.IsChecked == true;
        var onlyNonZero = ChkExplorerOnlyNonZero.IsChecked == true;
        var onlyChanged = ChkExplorerOnlyChanged.IsChecked == true;

        var selectedKey = (GridModbusExplorer.SelectedItem as MesModbusExplorerRow)?.Key;

        var filtered = _allModbusExplorerRows.Where(row =>
            (!onlyReadable || row.IsReadable) &&
            (!onlyNonZero || row.IsNonZero) &&
            (!onlyChanged || row.HasChanged) &&
            (string.IsNullOrWhiteSpace(filter) ||
             row.SearchText.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        _modbusExplorerRows.Clear();
        foreach (var row in filtered)
        {
            _modbusExplorerRows.Add(row);
        }

        if (!string.IsNullOrWhiteSpace(selectedKey))
        {
            GridModbusExplorer.SelectedItem = _modbusExplorerRows.FirstOrDefault(row =>
                string.Equals(row.Key, selectedKey, StringComparison.OrdinalIgnoreCase));
        }
    }

    private bool TryGetExplorerConnection(
        out ExplorerConnection connection,
        out string error)
    {
        connection = default;
        error = string.Empty;

        if (CmbWorkplace.SelectedItem is not MesWorkplaceBindingItem item)
        {
            error = T(
                "MES00.ModbusExplorer.SelectWorkplace",
                "Nejdřív vyber pracoviště.");
            return false;
        }

        CaptureWorkplaceHeader();

        var ipAddress = string.IsNullOrWhiteSpace(_editingBinding?.IpAddressOverride)
            ? item.IpAddress
            : _editingBinding!.IpAddressOverride!.Trim();
        var port = Math.Clamp(ReadInt(TxtWorkplacePort.Text, 502), 1, 65535);
        var unitId = (byte)Math.Clamp(ReadInt(TxtExplorerUnitId.Text, _editingBinding?.UnitId ?? 0), 0, 255);
        var timeoutMs = Math.Clamp(ReadInt(TxtExplorerTimeout.Text, _editingBinding?.TimeoutMs ?? 3000), 250, 30_000);

        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            error = T(
                "MES00.ModbusExplorer.MissingIp",
                "Vybrané pracoviště nemá IP adresu.");
            return false;
        }

        TxtExplorerEndpoint.Text = $"{ipAddress}:{port}";
        connection = new ExplorerConnection(ipAddress, port, unitId, timeoutMs);
        return true;
    }

    private void SetExplorerBusy(bool isBusy)
    {
        BtnExplorerTestConnection.IsEnabled = !isBusy;
        BtnExplorerScan.IsEnabled = !isBusy;
        BtnExplorerQuickScan.IsEnabled = !isBusy;
        BtnExplorerCancel.IsEnabled = isBusy;
        BtnExplorerAssign.IsEnabled = !isBusy;
        BtnExplorerCaptureBaseline.IsEnabled = !isBusy;
    }

    private async void BtnExplorerTestConnection_Click(object sender, RoutedEventArgs e) =>
        await TestExplorerConnectionAsync();

    private async void BtnExplorerScan_Click(object sender, RoutedEventArgs e) =>
        await ScanExplorerAreaAsync();

    private async void BtnExplorerQuickScan_Click(object sender, RoutedEventArgs e) =>
        await QuickScanExplorerAsync();

    private void BtnExplorerCancel_Click(object sender, RoutedEventArgs e) =>
        _modbusExplorerCancellation?.Cancel();

    private void BtnExplorerCaptureBaseline_Click(object sender, RoutedEventArgs e) =>
        CaptureExplorerBaseline();

    private void BtnExplorerAssign_Click(object sender, RoutedEventArgs e) =>
        AssignExplorerAddressToMapping();

    private void ExplorerTextFilter_Changed(object sender, TextChangedEventArgs e)
    {
        if (_isViewInitialized)
        {
            RefreshExplorerFilter();
        }
    }

    private void ExplorerCheckFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_isViewInitialized)
        {
            RefreshExplorerFilter();
        }
    }

    private readonly record struct ExplorerConnection(
        string IpAddress,
        int Port,
        byte UnitId,
        int TimeoutMs);

    private readonly record struct ExplorerScanRequest(
        MesModbusArea Area,
        int StartAddress,
        int Count,
        int BlockSize);

    private void CaptureWorkplaceHeader()
    {
        if (_editingBinding is null)
        {
            return;
        }

        _editingBinding.Driver = CmbWorkplaceDriver.SelectedItem?.ToString()
                                 ?? CmbWorkplaceDriver.Text?.Trim()
                                 ?? MesDriverKeys.Unconfigured;
        if (string.IsNullOrWhiteSpace(_editingBinding.Driver))
        {
            _editingBinding.Driver = MesDriverKeys.Unconfigured;
        }
        _editingBinding.Port = Math.Clamp(
            ReadInt(TxtWorkplacePort.Text, _editingBinding.Port),
            0,
            65535);
        _editingBinding.UnitId = (byte)Math.Clamp(
            ReadInt(TxtWorkplaceUnitId.Text, _editingBinding.UnitId),
            byte.MinValue,
            byte.MaxValue);
        _editingBinding.PollIntervalMs = Math.Clamp(
            ReadInt(TxtWorkplacePollInterval.Text, 500),
            250,
            60_000);
        _editingBinding.TimeoutMs = Math.Clamp(
            ReadInt(TxtWorkplaceTimeout.Text, 3000),
            250,
            30_000);
        _editingBinding.StaleAfterSeconds = Math.Clamp(
            ReadInt(TxtWorkplaceStaleAfter.Text, 3),
            1,
            3600);
        _editingBinding.StopTimeoutSeconds = Math.Clamp(
            ReadInt(TxtWorkplaceStopTimeout.Text, 30),
            1,
            3600);
        _editingBinding.Controller = TxtWorkplaceController.Text?.Trim() ?? string.Empty;
        _editingBinding.Enabled = ChkWorkplaceEnabled.IsChecked == true;
    }

    private void LogBindingAudit(MesPlcBinding? oldBinding, MesPlcBinding newBinding)
    {
        var station = newBinding.StationCode;

        if (oldBinding is null)
        {
            _logger?.AuditCreated(
                "MES00",
                "PlcBinding",
                station,
                _currentUserDisplayName,
                $"Driver={newBinding.Driver}; Controller={newBinding.Controller}; Port={newBinding.Port}; UnitId={newBinding.UnitId}; PollIntervalMs={newBinding.PollIntervalMs}; TimeoutMs={newBinding.TimeoutMs}; StaleAfterSeconds={newBinding.StaleAfterSeconds}; Points={newBinding.DataPoints.Count}");

            foreach (var point in newBinding.DataPoints)
            {
                _logger?.AuditCreated(
                    "MES00",
                    "PlcSignalMapping",
                    $"{station}/{point.Code}",
                    _currentUserDisplayName,
                    $"LogicalSignal={point.LogicalSignal}; DisplayName={point.DisplayName}; Module={point.ModuleType}; Slot={point.Slot}; Channel={point.Channel}; Enabled={point.Enabled}; VisibleInMes03={point.VisibleInMes03}; Inverted={point.Inverted}; Source={DescribeSource(point.Source)}");
            }

            return;
        }

        AuditField(station, "Driver", oldBinding.Driver, newBinding.Driver);
        AuditField(station, "Controller", oldBinding.Controller, newBinding.Controller);
        AuditField(station, "Enabled", oldBinding.Enabled.ToString(), newBinding.Enabled.ToString());
        AuditField(station, "Port", oldBinding.Port.ToString(), newBinding.Port.ToString());
        AuditField(station, "UnitId", oldBinding.UnitId.ToString(), newBinding.UnitId.ToString());
        AuditField(station, "PollIntervalMs", oldBinding.PollIntervalMs.ToString(), newBinding.PollIntervalMs.ToString());
        AuditField(station, "TimeoutMs", oldBinding.TimeoutMs.ToString(), newBinding.TimeoutMs.ToString());
        AuditField(station, "StaleAfterSeconds", oldBinding.StaleAfterSeconds.ToString(), newBinding.StaleAfterSeconds.ToString());
        AuditField(station, "StopTimeoutSeconds", oldBinding.StopTimeoutSeconds.ToString(), newBinding.StopTimeoutSeconds.ToString());

        var oldPoints = oldBinding.DataPoints.ToDictionary(
            point => point.Code,
            StringComparer.OrdinalIgnoreCase);

        foreach (var point in newBinding.DataPoints)
        {
            var entityId = $"{station}/{point.Code}";
            if (!oldPoints.TryGetValue(point.Code, out var oldPoint))
            {
                _logger?.AuditCreated(
                    "MES00",
                    "PlcSignalMapping",
                    entityId,
                    _currentUserDisplayName,
                    $"LogicalSignal={point.LogicalSignal}; DisplayName={point.DisplayName}; Enabled={point.Enabled}; VisibleInMes03={point.VisibleInMes03}; Inverted={point.Inverted}; Source={DescribeSource(point.Source)}");
                continue;
            }

            AuditPointField(entityId, "LogicalSignal", oldPoint.LogicalSignal, point.LogicalSignal);
            AuditPointField(entityId, "DisplayName", oldPoint.DisplayName, point.DisplayName);
            AuditPointField(entityId, "Enabled", oldPoint.Enabled.ToString(), point.Enabled.ToString());
            AuditPointField(entityId, "VisibleInMes03", oldPoint.VisibleInMes03.ToString(), point.VisibleInMes03.ToString());
            AuditPointField(entityId, "Inverted", oldPoint.Inverted.ToString(), point.Inverted.ToString());
            AuditPointField(entityId, "Source", DescribeSource(oldPoint.Source), DescribeSource(point.Source));
        }
    }

    private static string DescribeSource(MesModbusSource? source)
    {
        if (source?.Address is not int address)
        {
            return "UNMAPPED";
        }

        return $"{source.Area}:{address}:{source.DataType}:Bit={source.BitIndex?.ToString() ?? "-"}:WordOrder={source.WordOrder}";
    }

    private void AuditField(string station, string field, string? oldValue, string? newValue)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        _logger?.AuditChange(
            "MES00",
            "PlcBinding",
            station,
            field,
            oldValue,
            newValue,
            _currentUserDisplayName);
    }

    private void AuditPointField(string entityId, string field, string? oldValue, string? newValue)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        _logger?.AuditChange(
            "MES00",
            "PlcSignalMapping",
            entityId,
            field,
            oldValue,
            newValue,
            _currentUserDisplayName);
    }

    private MesPlcBinding? FindBinding(string stationCode)
    {
        return _bindingSet.Devices.FirstOrDefault(binding =>
            string.Equals(
                binding.StationCode,
                stationCode,
                StringComparison.OrdinalIgnoreCase));
    }

    private string GetSelectedStationCode()
    {
        return CmbWorkplace.SelectedItem is MesWorkplaceBindingItem item
            ? item.StationCode
            : string.Empty;
    }

    private string ResolveConfiguredPath(
        string? configuredPath,
        string fallback,
        string defaultFileName)
    {
        var value = configuredPath?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var resolved = Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(_configurationRootPath, value));

        return Directory.Exists(resolved)
            ? Path.Combine(resolved, defaultFileName)
            : resolved;
    }

    private static MesPlcBinding? CloneBinding(MesPlcBinding? source)
    {
        if (source is null)
        {
            return null;
        }

        return new MesPlcBinding
        {
            StationCode = source.StationCode,
            IpAddressOverride = source.IpAddressOverride,
            Driver = source.Driver,
            Enabled = source.Enabled,
            Port = source.Port,
            UnitId = source.UnitId,
            PollIntervalMs = source.PollIntervalMs,
            TimeoutMs = source.TimeoutMs,
            StaleAfterSeconds = source.StaleAfterSeconds,
            StopTimeoutSeconds = source.StopTimeoutSeconds,
            Controller = source.Controller,
            Modules = source.Modules.Select(module => new MesModuleDefinition
            {
                Slot = module.Slot,
                Type = module.Type,
                Description = module.Description
            }).ToList(),
            DataPoints = source.DataPoints.Select(point =>
                MesSignalMappingEditRow.FromDefinition(point).ToDefinition()).ToList()
        };
    }

    private static int ReadInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static string GetComboValue(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item)
        {
            return item.Content?.ToString() ?? "None";
        }

        return comboBox.Text;
    }

    private static void SelectComboValue(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        LoadSettings();
        LoadWorkplaces(GetSelectedStationCode());
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e) => SaveSettings();

    private void BtnReloadWorkplaces_Click(object sender, RoutedEventArgs e) =>
        LoadWorkplaces(GetSelectedStationCode());

    private void CmbWorkplace_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        DisplaySelectedWorkplace();

    private void BtnApplyBrTemplate_Click(object sender, RoutedEventArgs e) =>
        ApplyStandardBrTemplate();

    private void BtnSwapCounters_Click(object sender, RoutedEventArgs e) =>
        SwapCounters();

    private async void BtnTestModbus_Click(object sender, RoutedEventArgs e) =>
        await TestSelectedModbusAsync();

    private void BtnSaveMapping_Click(object sender, RoutedEventArgs e) =>
        SaveSelectedMapping();

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
