using DMS.Core.Sap;
using DMS.Desktop.Logging;
using DMS.Desktop.Settings;
using DMS.Integration.Mes.Database;
using DMS.Integration.Mes.Screens;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DMS.Desktop.Views.Screens
{
    public partial class Scr10PreparationQueueView : UserControl
    {
        private readonly DmsLogger _logger;
        private readonly string _userName;
        private readonly DmsUserSettings _userSettings;
        private readonly DmsUserSettingsService _settingsService;
        private readonly Scr10PreparationQueueService? _service;
        private readonly DispatcherTimer _refreshTimer;

        private CancellationTokenSource? _cts;
        private bool _loadingGroups;
        private bool _refreshRunning;
        private Scr10QueueMode _mode = Scr10QueueMode.Current;
        private Scr10QueueSnapshot? _lastSnapshot;
        private string? _lastFingerprint;

        private bool _autoRefreshEnabled;
        private int _autoRefreshIntervalMs;

        public Scr10PreparationQueueView(
            string configurationRootPath,
            DmsLogger logger,
            string userName,
            DmsUserSettings userSettings,
            DmsUserSettingsService settingsService)
        {
            InitializeComponent();

            // Event is attached here intentionally.
            // XAML does not contain SelectionChanged=... to avoid duplicate invocation.
            GroupFilterCombo.SelectionChanged += GroupFilterCombo_SelectionChanged;

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _userName = userName ?? string.Empty;
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

            _autoRefreshEnabled = _userSettings.Scr10AutoRefreshEnabled;
            _autoRefreshIntervalMs = _userSettings.Scr10AutoRefreshIntervalMs;

            _service = CreateService(configurationRootPath);

            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Tick += RefreshTimer_Tick;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            ApplyRefreshSettings();
        }

        private Scr10PreparationQueueService? CreateService(
            string configurationRootPath)
        {
            try
            {
                var settingsPath =
                    FindMesDatabaseSettingsPath(configurationRootPath);

                if (string.IsNullOrWhiteSpace(settingsPath))
                {
                    _logger.Warning(
                        "SCR10: MES database settings file was not found.");
                    return null;
                }

                var mesSettings =
                    new MesDatabaseSettingsService().Load(settingsPath);

                if (!mesSettings.IsEnabled)
                {
                    _logger.Warning(
                        "SCR10: MES SQL connection is disabled.");
                    return null;
                }

                // ConfigurationRootPath normally points to ...\Config,
                // while SAP mirror files live under the DMS environment/data root.
                var dmsDataRoot =
                    ResolveDmsDataRoot(configurationRootPath);

                _logger.Info(
                    $"SCR10 initialized. ConfigRoot={configurationRootPath}; DataRoot={dmsDataRoot}");

                return new Scr10PreparationQueueService(
                    new Scr10QueueDataService(mesSettings),
                    new SapScreenBomResolver(dmsDataRoot));
            }
            catch (Exception ex)
            {
                _logger.Error(
                    "SCR10 initialization failed.",
                    ex);
                return null;
            }
        }

        private async void OnLoaded(
            object sender,
            RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();

            if (_service == null)
            {
                StatusText.Text =
                    "Připojení k MES není nakonfigurováno.";
                return;
            }

            // Load group metadata before the first snapshot.
            await LoadGroupsAsync(_cts.Token);

            if (_autoRefreshEnabled)
            {
                _refreshTimer.Start();
            }

            await RefreshAsync(false, _cts.Token);
        }

        private async Task LoadGroupsAsync(
            CancellationToken cancellationToken)
        {
            if (_service == null)
            {
                return;
            }

            try
            {
                _loadingGroups = true;

                var groups =
                    await _service.GetWorkcenterGroupsAsync(
                        cancellationToken);

                // Always provide a usable fallback.
                if (groups.Count == 0)
                {
                    groups = new[]
                    {
                        new Scr10WorkcenterGroup
                        {
                            Code = string.Empty,
                            WorkcenterCodes = Array.Empty<string>()
                        }
                    };
                }

                GroupFilterCombo.ItemsSource = groups;

                var savedCode =
                    _userSettings.Scr10SelectedWorkcenterGroup
                    ?? string.Empty;

                GroupFilterCombo.SelectedItem =
                    groups.FirstOrDefault(x =>
                        string.Equals(
                            x.Code,
                            savedCode,
                            StringComparison.OrdinalIgnoreCase))
                    ?? groups.First();

                _logger.Info(
                    $"SCR10 workcenter groups loaded. Count={Math.Max(0, groups.Count - 1)}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(
                    "SCR10 workcenter groups load failed.",
                    ex);

                var fallback = new[]
                {
                    new Scr10WorkcenterGroup
                    {
                        Code = string.Empty,
                        WorkcenterCodes = Array.Empty<string>()
                    }
                };

                GroupFilterCombo.ItemsSource = fallback;
                GroupFilterCombo.SelectedIndex = 0;
            }
            finally
            {
                _loadingGroups = false;
            }
        }

        private void OnUnloaded(
            object sender,
            RoutedEventArgs e)
        {
            _refreshTimer.Stop();

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private async void RefreshTimer_Tick(
            object? sender,
            EventArgs e)
        {
            if (_cts == null || _service == null)
            {
                return;
            }

            await RefreshAsync(false, _cts.Token);
        }

        private async Task RefreshAsync(
            bool manual,
            CancellationToken cancellationToken)
        {
            if (_service == null)
            {
                StatusText.Text =
                    "Připojení k MES není nakonfigurováno.";
                return;
            }

            if (_refreshRunning)
            {
                if (manual)
                {
                    StatusText.Text =
                        "Předchozí načítání ještě probíhá.";
                }

                return;
            }

            try
            {
                _refreshRunning = true;

                if (manual)
                {
                    StatusText.Text = "Načítám data…";
                }

                var snapshot =
                    await _service.GetAsync(
                        _mode,
                        cancellationToken);

                _lastSnapshot = snapshot;
                ApplySnapshotIfChanged(snapshot);

                LastRefreshText.Text =
                    $"Poslední obnovení: {snapshot.LoadedAt:dd.MM.yyyy HH:mm:ss}";

                StatusText.Text = "Načteno";
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Načítání zrušeno";
            }
            catch (Exception ex)
            {
                _logger.Error(
                    $"SCR10 data load failed. User={_userName}; Mode={_mode}.",
                    ex);

                StatusText.Text =
                    $"Chyba při načítání dat: {ex.Message}";
            }
            finally
            {
                _refreshRunning = false;
            }
        }

        private void ApplySnapshotIfChanged(
            Scr10QueueSnapshot snapshot)
        {
            var selectedGroup =
                GroupFilterCombo.SelectedItem
                as Scr10WorkcenterGroup;

            var groupCode =
                selectedGroup?.Code ?? string.Empty;

            var filterFingerprint =
                $"{snapshot.Fingerprint}|GROUP={groupCode}";

            if (string.Equals(
                    _lastFingerprint,
                    filterFingerprint,
                    StringComparison.Ordinal))
            {
                return;
            }

            _lastFingerprint = filterFingerprint;

            var rows = snapshot.Rows.AsEnumerable();

            if (selectedGroup != null &&
                !string.IsNullOrWhiteSpace(selectedGroup.Code))
            {
                var allowed =
                    selectedGroup.WorkcenterCodes
                        .ToHashSet(
                            StringComparer.OrdinalIgnoreCase);

                rows = rows.Where(x =>
                    allowed.Contains(x.WorkcenterCode));
            }

            var filteredRows = rows.ToArray();

            QueueGrid.ItemsSource = filteredRows;

            RowsText.Text =
                $"Řádků: {filteredRows.Length}";

            WorkcentersText.Text =
                $"Strojů: {filteredRows.Select(x => x.WorkcenterCode).Distinct(StringComparer.OrdinalIgnoreCase).Count()}";
        }

        private void GroupFilterCombo_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_loadingGroups)
            {
                return;
            }

            var selected =
                GroupFilterCombo.SelectedItem
                as Scr10WorkcenterGroup;

            _userSettings.Scr10SelectedWorkcenterGroup =
                selected?.Code ?? string.Empty;

            _settingsService.Save(_userSettings);

            _lastFingerprint = null;

            if (_lastSnapshot != null)
            {
                ApplySnapshotIfChanged(_lastSnapshot);
            }
        }

        private async void CurrentModeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_mode == Scr10QueueMode.Current)
            {
                CurrentModeButton.IsChecked = true;
                return;
            }

            _mode = Scr10QueueMode.Current;
            CurrentModeButton.IsChecked = true;
            PlannedModeButton.IsChecked = false;
            _lastFingerprint = null;

            if (_cts != null)
            {
                await RefreshAsync(true, _cts.Token);
            }
        }

        private async void PlannedModeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_mode == Scr10QueueMode.Planned)
            {
                PlannedModeButton.IsChecked = true;
                return;
            }

            _mode = Scr10QueueMode.Planned;
            CurrentModeButton.IsChecked = false;
            PlannedModeButton.IsChecked = true;
            _lastFingerprint = null;

            if (_cts != null)
            {
                await RefreshAsync(true, _cts.Token);
            }
        }

        private async void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_cts != null)
            {
                await RefreshAsync(true, _cts.Token);
            }
        }

        private void SettingsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _autoRefreshEnabled =
                !_autoRefreshEnabled;

            _userSettings.Scr10AutoRefreshEnabled =
                _autoRefreshEnabled;

            _userSettings.Scr10AutoRefreshIntervalMs =
                _autoRefreshIntervalMs;

            _settingsService.Save(_userSettings);

            ApplyRefreshSettings();

            StatusText.Text =
                _autoRefreshEnabled
                    ? $"Auto-refresh zapnut ({_autoRefreshIntervalMs} ms)"
                    : "Auto-refresh vypnut";
        }

        private void ApplyRefreshSettings()
        {
            _autoRefreshIntervalMs =
                Math.Clamp(
                    _autoRefreshIntervalMs,
                    500,
                    60000);

            _refreshTimer.Interval =
                TimeSpan.FromMilliseconds(
                    _autoRefreshIntervalMs);

            AutoRefreshText.Text =
                _autoRefreshEnabled
                    ? $"⟳ {_autoRefreshIntervalMs / 1000.0:0.#} s"
                    : "⟳ vypnuto";

            if (!IsLoaded)
            {
                return;
            }

            if (_autoRefreshEnabled && _service != null)
            {
                _refreshTimer.Start();
            }
            else
            {
                _refreshTimer.Stop();
            }
        }

        private static string ResolveDmsDataRoot(
            string configurationRootPath)
        {
            if (string.IsNullOrWhiteSpace(configurationRootPath))
            {
                return configurationRootPath;
            }

            var fullPath =
                Path.GetFullPath(configurationRootPath);

            // ConfigurationRootPath is normally <environment>\Config.
            if (string.Equals(
                    Path.GetFileName(
                        fullPath.TrimEnd(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar)),
                    "Config",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Directory.GetParent(fullPath)?.FullName
                    ?? fullPath;
            }

            // Compatibility: if the caller already passed the environment root.
            if (Directory.Exists(
                    Path.Combine(fullPath, "SapMirror")))
            {
                return fullPath;
            }

            var parent =
                Directory.GetParent(fullPath)?.FullName;

            if (!string.IsNullOrWhiteSpace(parent) &&
                Directory.Exists(
                    Path.Combine(parent, "SapMirror")))
            {
                return parent;
            }

            return fullPath;
        }

        private static string? FindMesDatabaseSettingsPath(
            string configurationRootPath)
        {
            if (string.IsNullOrWhiteSpace(configurationRootPath) ||
                !Directory.Exists(configurationRootPath))
            {
                return null;
            }

            var names = new[]
            {
                "mes-database-settings.json",
                "mes-reporting-settings.json",
                "mes-sql-settings.json"
            };

            foreach (var fileName in names)
            {
                var candidate =
                    Path.Combine(
                        configurationRootPath,
                        fileName);

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            foreach (var candidate in
                     Directory.EnumerateFiles(
                         configurationRootPath,
                         "*.json",
                         SearchOption.TopDirectoryOnly))
            {
                if (LooksLikeMesDatabaseSettings(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool LooksLikeMesDatabaseSettings(
            string filePath)
        {
            try
            {
                using var document =
                    JsonDocument.Parse(
                        File.ReadAllText(filePath));

                if (document.RootElement.ValueKind !=
                    JsonValueKind.Object)
                {
                    return false;
                }

                var names =
                    document.RootElement
                        .EnumerateObject()
                        .Select(x => x.Name)
                        .ToHashSet(
                            StringComparer.OrdinalIgnoreCase);

                return names.Contains("Server") &&
                       names.Contains("Database") &&
                       (names.Contains("ReportingSchema") ||
                        names.Contains("Schema"));
            }
            catch
            {
                return false;
            }
        }
    }
}