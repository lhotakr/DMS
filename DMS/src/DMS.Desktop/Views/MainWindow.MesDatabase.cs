using DMS.Integration.Mes.Database;
using System.Windows.Threading;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private readonly MesDatabaseSettingsService _mesDatabaseSettingsService = new();
    private readonly MesConnectionHealthService _mesConnectionHealthService = new();

    private DispatcherTimer? _mesDatabaseHealthTimer;
    private MesConnectionHealthResult? _mesDatabaseHealth;
    private bool _mesDatabaseHealthCheckRunning;
    private string _lastMesDatabaseHealthSignature = string.Empty;

    private string GetMesDatabaseSettingsFilePath()
    {
        return GetConfigPath(
            "mes-database-settings.json");
    }

    private string GetMesReportDefinitionsFilePath()
    {
        return GetConfigPath(
            "mes-report-definitions.json");
    }

    private void InitializeMesDatabaseHealthMonitoring()
    {
        _mesDatabaseHealthTimer =
            new DispatcherTimer(
                DispatcherPriority.Background)
            {
                Interval =
                    TimeSpan.FromSeconds(60)
            };

        _mesDatabaseHealthTimer.Tick +=
            async (_, _) =>
                await RefreshMesDatabaseStatusAsync();

        Loaded +=
            async (_, _) =>
            {
                ConfigureMesDatabaseHealthTimer();
                await RefreshMesDatabaseStatusAsync();
            };

        Closed +=
            (_, _) =>
                _mesDatabaseHealthTimer?.Stop();
    }

    private void ConfigureMesDatabaseHealthTimer()
    {
        var settings =
            _mesDatabaseSettingsService.Load(
                GetMesDatabaseSettingsFilePath());

        if (_mesDatabaseHealthTimer is null)
        {
            return;
        }

        _mesDatabaseHealthTimer.Interval =
            TimeSpan.FromSeconds(
                settings.HealthCheckIntervalSeconds);

        _mesDatabaseHealthTimer.Start();
    }

    internal async Task RefreshMesDatabaseStatusAsync()
    {
        if (_mesDatabaseHealthCheckRunning)
        {
            return;
        }

        _mesDatabaseHealthCheckRunning = true;

        try
        {
            var settings =
                _mesDatabaseSettingsService.Load(
                    GetMesDatabaseSettingsFilePath());

            _mesDatabaseHealth =
                await _mesConnectionHealthService
                    .CheckAsync(settings);

            ApplyMesDatabaseStatusText();
            LogMesDatabaseStatusChange(
                settings,
                _mesDatabaseHealth);
        }
        catch (Exception ex)
        {
            _mesDatabaseHealth =
                new MesConnectionHealthResult
                {
                    IsEnabled = true,
                    IsConnected = false,
                    CheckedAt = DateTimeOffset.Now,
                    Error = ex.Message
                };

            ApplyMesDatabaseStatusText();

            _logger.Error(
                "MES SQL health check failed.",
                ex);
        }
        finally
        {
            _mesDatabaseHealthCheckRunning = false;
        }
    }


    private void LogMesDatabaseStatusChange(
        MesDatabaseConnectionSettings settings,
        MesConnectionHealthResult health)
    {
        var signature =
            $"{health.IsEnabled}|{health.IsConnected}|{health.CanSelect}|{health.CanViewDefinition}|{health.Server}|{health.Database}|{health.LoginName}|{health.Error}";

        if (string.Equals(
                signature,
                _lastMesDatabaseHealthSignature,
                StringComparison.Ordinal))
        {
            return;
        }

        _lastMesDatabaseHealthSignature =
            signature;

        if (!health.IsEnabled)
        {
            _logger.Info(
                "MES SQL status changed: disabled.");

            return;
        }

        if (health.IsConnected &&
            health.CanSelect)
        {
            _logger.Info(
                $"MES SQL status changed: connected; Server={health.Server}; Database={health.Database}; Login={health.LoginName}; ViewDefinition={health.CanViewDefinition}");

            return;
        }

        _logger.Warning(
            $"MES SQL status changed: unavailable; Server={settings.Server}; Database={settings.Database}; Error={health.Error}");
    }


    private string MesStatusText(
        string key,
        string fallback)
    {
        var translated =
            T(key);

        return string.IsNullOrWhiteSpace(translated)
               || string.Equals(
                   translated,
                   key,
                   StringComparison.Ordinal)
            ? fallback
            : translated;
    }

    private void ApplyMesDatabaseStatusText()
    {
        if (StatusMes is null)
        {
            return;
        }

        if (_mesDatabaseHealth is null)
        {
            StatusMes.Content =
                MesStatusText(
                    "Status.MesConnecting",
                    "MES: connecting...");

            SetMesDatabaseStatusBrush(
                "DmsWarningBrush");

            return;
        }

        if (!_mesDatabaseHealth.IsEnabled)
        {
            StatusMes.Content =
                MesStatusText(
                    "Status.MesDisabled",
                    "MES: disabled");

            StatusMes.ToolTip =
                MesStatusText(
                    "Status.MesDisabled.Tooltip",
                    "MES database connection is disabled in MESSET.");

            SetMesDatabaseStatusBrush(
                "DmsMutedForegroundBrush");

            return;
        }

        if (_mesDatabaseHealth.IsConnected &&
            _mesDatabaseHealth.CanSelect)
        {
            StatusMes.Content =
                MesStatusText(
                    "Status.MesConnected",
                    "MES: connection established");

            StatusMes.ToolTip =
                $"{_mesDatabaseHealth.Server} / {_mesDatabaseHealth.Database} | {_mesDatabaseHealth.LoginName} | {_mesDatabaseHealth.LatencyMilliseconds} ms";

            SetMesDatabaseStatusBrush(
                "DmsAccentBrush");

            return;
        }

        StatusMes.Content =
            MesStatusText(
                "Status.MesDisconnected",
                "MES: disconnected");

        StatusMes.ToolTip =
            _mesDatabaseHealth.Error;

        SetMesDatabaseStatusBrush(
            "DmsErrorBrush");
    }

    private void SetMesDatabaseStatusBrush(
        string resourceKey)
    {
        StatusMes.SetResourceReference(
            System.Windows.Controls.Control.ForegroundProperty,
            resourceKey);
    }
}
