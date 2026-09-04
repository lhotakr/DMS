using DMS.Desktop.Logging;
using DMS.Desktop.UI;
using DMS.Integration.Mes.Database;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Mes;

public partial class MesDatabaseSettingsView
    : UserControl
{
    private readonly string _settingsPath;
    private readonly DmsLogger _logger;
    private readonly string _user;
    private readonly bool _canEdit;
    private readonly Func<string, string> _translate;
    private readonly Func<Task>? _connectionChanged;
    private readonly MesDatabaseSettingsService _settingsService = new();
    private readonly MesConnectionHealthService _healthService = new();

    private MesDatabaseConnectionSettings _settings = new();

    public MesDatabaseSettingsView(
        string settingsPath,
        DmsLogger logger,
        string user,
        bool canEdit,
        Func<string, string>? translate = null,
        Func<Task>? connectionChanged = null)
    {
        InitializeComponent();

        _settingsPath =
            settingsPath
            ?? throw new ArgumentNullException(nameof(settingsPath));

        _logger =
            logger
            ?? throw new ArgumentNullException(nameof(logger));

        _user =
            user ?? string.Empty;

        _canEdit = canEdit;

        _translate =
            translate ?? (key => key);

        _connectionChanged =
            connectionChanged;

        ApplyLocalization();
        LoadSettings();
    }

    private string T(
        string key,
        string fallback)
    {
        var translated =
            _translate(key);

        return string.IsNullOrWhiteSpace(translated)
               || string.Equals(
                   translated,
                   key,
                   StringComparison.Ordinal)
            ? fallback
            : translated;
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text =
            T(
                "MESSET.Title",
                "MESSET - MES Database Settings");

        TxtSubtitle.Text =
            T(
                "MESSET.Subtitle",
                "Read-only connection to the FASTEC reporting database used by DMS MES reporting.");

        ChkEnabled.Content =
            T(
                "MESSET.Field.Enabled",
                "Enable MES SQL reporting connection");

        LblServer.Text =
            T(
                "MESSET.Field.Server",
                "SQL server");

        LblDatabase.Text =
            T(
                "MESSET.Field.Database",
                "Database");

        LblSchema.Text =
            T(
                "MESSET.Field.Schema",
                "Reporting schema");

        LblAuthentication.Text =
            T(
                "MESSET.Field.Authentication",
                "Authentication");

        TxtAuthentication.Text =
            T(
                "MESSET.Authentication.Windows",
                "Windows Authentication");

        ChkEncrypt.Content =
            T(
                "MESSET.Field.Encrypt",
                "Encrypt SQL connection");

        ChkTrustServerCertificate.Content =
            T(
                "MESSET.Field.TrustServerCertificate",
                "Trust server certificate");

        LblConnectTimeout.Text =
            T(
                "MESSET.Field.ConnectTimeout",
                "Connection timeout [s]");

        LblCommandTimeout.Text =
            T(
                "MESSET.Field.CommandTimeout",
                "Query timeout [s]");

        LblHealthInterval.Text =
            T(
                "MESSET.Field.HealthInterval",
                "Status check interval [s]");

        TxtReadOnlyHint.Text =
            T(
                "MESSET.ReadOnlyHint",
                "DMS uses this integration only for parameterized SELECT queries. No INSERT, UPDATE or DELETE API is exposed.");

        TxtHealthTitle.Text =
            T(
                "MESSET.Health.Title",
                "Connection test");

        LblHealthStatus.Text =
            T(
                "MESSET.Health.Status",
                "Status");

        LblHealthLogin.Text =
            T(
                "MESSET.Health.Login",
                "Login");

        LblHealthLatency.Text =
            T(
                "MESSET.Health.Latency",
                "Latency");

        LblHealthSelect.Text =
            T(
                "MESSET.Health.Select",
                "SELECT");

        LblHealthViewDefinition.Text =
            T(
                "MESSET.Health.ViewDefinition",
                "VIEW DEFINITION");

        BtnReload.Content =
            T(
                "MESSET.Button.Reload",
                "Reload");

        BtnTest.Content =
            T(
                "MESSET.Button.Test",
                "Test connection");

        BtnSave.Content =
            T(
                "MESSET.Button.Save",
                "Save");
    }

    private void LoadSettings()
    {
        _settings =
            _settingsService.Load(
                _settingsPath);

        FillUi(
            _settings);

        SetEditState();

        TxtStatus.Text =
            T(
                "MESSET.Status.Loaded",
                "MES database settings loaded.");
    }

    private void FillUi(
        MesDatabaseConnectionSettings settings)
    {
        ChkEnabled.IsChecked =
            settings.IsEnabled;

        TxtServer.Text =
            settings.Server;

        TxtDatabase.Text =
            settings.Database;

        TxtSchema.Text =
            settings.ReportingSchema;

        ChkEncrypt.IsChecked =
            settings.Encrypt;

        ChkTrustServerCertificate.IsChecked =
            settings.TrustServerCertificate;

        TxtConnectTimeout.Text =
            settings.ConnectTimeoutSeconds.ToString();

        TxtCommandTimeout.Text =
            settings.CommandTimeoutSeconds.ToString();

        TxtHealthInterval.Text =
            settings.HealthCheckIntervalSeconds.ToString();
    }

    private MesDatabaseConnectionSettings ReadUi()
    {
        var result =
            new MesDatabaseConnectionSettings
            {
                IsEnabled =
                    ChkEnabled.IsChecked == true,

                Server =
                    TxtServer.Text?.Trim()
                    ?? string.Empty,

                Database =
                    TxtDatabase.Text?.Trim()
                    ?? string.Empty,

                ReportingSchema =
                    TxtSchema.Text?.Trim()
                    ?? string.Empty,

                IntegratedSecurity = true,

                Encrypt =
                    ChkEncrypt.IsChecked == true,

                TrustServerCertificate =
                    ChkTrustServerCertificate.IsChecked == true,

                ConnectTimeoutSeconds =
                    ReadInt(
                        TxtConnectTimeout.Text,
                        5),

                CommandTimeoutSeconds =
                    ReadInt(
                        TxtCommandTimeout.Text,
                        30),

                HealthCheckIntervalSeconds =
                    ReadInt(
                        TxtHealthInterval.Text,
                        60)
            };

        result.Normalize();
        return result;
    }

    private void SetEditState()
    {
        ChkEnabled.IsEnabled = _canEdit;
        TxtServer.IsReadOnly = !_canEdit;
        TxtDatabase.IsReadOnly = !_canEdit;
        TxtSchema.IsReadOnly = !_canEdit;
        ChkEncrypt.IsEnabled = _canEdit;
        ChkTrustServerCertificate.IsEnabled = _canEdit;
        TxtConnectTimeout.IsReadOnly = !_canEdit;
        TxtCommandTimeout.IsReadOnly = !_canEdit;
        TxtHealthInterval.IsReadOnly = !_canEdit;
        BtnSave.IsEnabled = _canEdit;
    }

    private void BtnReload_Click(
        object sender,
        RoutedEventArgs e)
    {
        LoadSettings();

        _logger.AdminAction(
            "MESSET",
            "ReloadMesDatabaseSettings",
            _user,
            $"File={_settingsPath}");
    }

    private async void BtnTest_Click(
        object sender,
        RoutedEventArgs e)
    {
        BtnTest.IsEnabled = false;

        try
        {
            var settings =
                ReadUi();

            TxtStatus.Text =
                T(
                    "MESSET.Status.Testing",
                    "Testing MES database connection...");

            var health =
                await _healthService
                    .CheckAsync(settings);

            ShowHealth(
                health);

            _logger.AdminAction(
                "MESSET",
                "TestMesDatabaseConnection",
                _user,
                $"Server={settings.Server}; Database={settings.Database}; Connected={health.IsConnected}; CanSelect={health.CanSelect}; CanViewDefinition={health.CanViewDefinition}; LatencyMs={health.LatencyMilliseconds}; Error={health.Error}");
        }
        finally
        {
            BtnTest.IsEnabled = true;
        }
    }

    private async void BtnSave_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_canEdit)
        {
            return;
        }

        try
        {
            var newSettings =
                ReadUi();

            _settingsService.Save(
                _settingsPath,
                newSettings);

            AuditSettingsChanges(
                _settings,
                newSettings);

            _settings =
                newSettings;

            TxtStatus.Text =
                T(
                    "MESSET.Status.Saved",
                    "MES database settings saved.");

            if (_connectionChanged is not null)
            {
                await _connectionChanged();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"MESSET settings save failed. File={_settingsPath}",
                ex);

            DmsMessage.Show(
                ex.Message,
                "MESSET",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            TxtStatus.Text =
                T(
                    "MESSET.Status.SaveFailed",
                    "MES database settings could not be saved.");
        }
    }


    private void AuditSettingsChanges(
        MesDatabaseConnectionSettings oldSettings,
        MesDatabaseConnectionSettings newSettings)
    {
        AuditSetting(
            "IsEnabled",
            oldSettings.IsEnabled,
            newSettings.IsEnabled);

        AuditSetting(
            "Server",
            oldSettings.Server,
            newSettings.Server);

        AuditSetting(
            "Database",
            oldSettings.Database,
            newSettings.Database);

        AuditSetting(
            "ReportingSchema",
            oldSettings.ReportingSchema,
            newSettings.ReportingSchema);

        AuditSetting(
            "Encrypt",
            oldSettings.Encrypt,
            newSettings.Encrypt);

        AuditSetting(
            "TrustServerCertificate",
            oldSettings.TrustServerCertificate,
            newSettings.TrustServerCertificate);

        AuditSetting(
            "ConnectTimeoutSeconds",
            oldSettings.ConnectTimeoutSeconds,
            newSettings.ConnectTimeoutSeconds);

        AuditSetting(
            "CommandTimeoutSeconds",
            oldSettings.CommandTimeoutSeconds,
            newSettings.CommandTimeoutSeconds);

        AuditSetting(
            "HealthCheckIntervalSeconds",
            oldSettings.HealthCheckIntervalSeconds,
            newSettings.HealthCheckIntervalSeconds);
    }

    private void AuditSetting(
        string field,
        object? oldValue,
        object? newValue)
    {
        var oldText =
            Convert.ToString(oldValue)
            ?? string.Empty;

        var newText =
            Convert.ToString(newValue)
            ?? string.Empty;

        if (string.Equals(
                oldText,
                newText,
                StringComparison.Ordinal))
        {
            return;
        }

        _logger.AuditChange(
            "MESSET",
            "MesDatabaseConnectionSettings",
            "FASTEC",
            field,
            oldText,
            newText,
            _user);
    }

    private void ShowHealth(
        MesConnectionHealthResult health)
    {
        TxtHealthStatus.Text =
            health.IsConnected && health.CanSelect
                ? T(
                    "MESSET.Health.Connected",
                    "Connected")
                : health.IsConnected
                    ? T(
                        "MESSET.Health.NoSelect",
                        "Connected, SELECT not permitted")
                    : T(
                        "MESSET.Health.Disconnected",
                        "Disconnected");

        TxtHealthLogin.Text =
            string.IsNullOrWhiteSpace(
                health.LoginName)
                ? "—"
                : health.LoginName;

        TxtHealthLatency.Text =
            $"{health.LatencyMilliseconds} ms";

        TxtHealthSelect.Text =
            health.CanSelect
                ? "✓"
                : "✕";

        TxtHealthViewDefinition.Text =
            health.CanViewDefinition
                ? "✓"
                : "✕";

        TxtHealthError.Text =
            health.Error;

        TxtStatus.Text =
            health.IsConnected && health.CanSelect
                ? T(
                    "MESSET.Status.TestOk",
                    "MES SQL connection is ready for read-only reporting.")
                : T(
                    "MESSET.Status.TestFailed",
                    "MES SQL connection test failed or SELECT is unavailable.");
    }

    private static int ReadInt(
        string? text,
        int fallback)
    {
        return int.TryParse(
                text,
                out var value)
            ? value
            : fallback;
    }
}
