using DMS.Desktop.Logging;
using DMS.Desktop.Views.Dialogs;
using DMS.Desktop.WorkLog;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.WorkLog;

public partial class WorkLogConfigView : UserControl
{
    private readonly WorkLogSettingsService _settingsService;
    private readonly DmsLogger? _logger;
    private readonly string _windowsLogin;
    private readonly string _currentUserName;
    private readonly bool _isDmsAdmin;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    private WorkLogSettings _settings = new();
    private WorkLogAccessPolicy? _access;

    public WorkLogConfigView(
        string configurationRootPath,
        string windowsLogin,
        string currentUserName,
        bool isDmsAdmin,
        DmsLogger? logger = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _settingsService =
            new WorkLogSettingsService(configurationRootPath);
        _windowsLogin = windowsLogin ?? string.Empty;
        _currentUserName =
            string.IsNullOrWhiteSpace(currentUserName)
                ? "UNKNOWN"
                : currentUserName;
        _isDmsAdmin = isDmsAdmin;
        _logger = logger;
        _translate = translate;
        _translateFormat = translateFormat;

        ApplyLocalization();
        LoadSettingsAndInfo();
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("WLCONFIG.Title");
        TxtSubtitle.Text = T("WLCONFIG.Subtitle");
        LblDatabase.Text = T("WLCONFIG.Database");
        BtnBrowse.Content = T("WLCONFIG.Browse");
        BtnTest.Content = T("WLCONFIG.Test");
        BtnSave.Content = T("WLCONFIG.Save");
        BtnReload.Content = T("WLCONFIG.Reload");
        TxtDatabaseInfoTitle.Text = T("WLCONFIG.DatabaseInfo");
        TxtServerTitle.Text = T("WLCONFIG.ServerTitle");
        ChkServerEnabled.Content = T("WLCONFIG.ServerEnabled");
        LblInterval.Text = T("WLCONFIG.Interval");
        TxtServerHint.Text = T("WLCONFIG.ServerHint");
        TxtSchemaTitle.Text = T("WLCONFIG.SchemaTitle");
        TxtSchemaHint.Text = T("WLCONFIG.SchemaHint");
    }

    private void LoadSettingsAndInfo()
    {
        try
        {
            _settings =
                _settingsService.Load();

            TxtDatabasePath.Text =
                _settings.DatabasePath;
            ChkServerEnabled.IsChecked =
                _settings.ServerClientEnabled;
            TxtInterval.Text =
                _settings.ServerTaskIntervalMinutes
                    .ToString();

            var repository =
                new WorkLogRepository(
                    _settings.DatabasePath);

            repository.TestConnection();

            var current =
                repository.FindUserByWindowsUsername(
                    _windowsLogin);

            _access =
                new WorkLogAccessPolicy(
                    current,
                    _isDmsAdmin);

            var admin = _access.IsAdministrator;
            SetAdminControlsEnabled(admin);

            ShowDatabaseInfo(
                repository.GetDatabaseInfo());

            TxtStatus.Text =
                admin
                    ? T("WLCONFIG.Status.Loaded")
                    : T("WLCONFIG.Status.ReadOnly");
        }
        catch (Exception ex)
        {
            _access =
                new WorkLogAccessPolicy(
                    null,
                    _isDmsAdmin);

            SetAdminControlsEnabled(
                _isDmsAdmin);

            TxtDatabaseInfo.Text =
                TF(
                    "WLCONFIG.Info.Error",
                    ex.Message);
            TxtStatus.Text =
                TF(
                    "WLCONFIG.Status.LoadFailed",
                    ex.Message);

            _logger?.Error(
                "WLCONFIG: load failed.",
                ex);
        }
    }

    private void SetAdminControlsEnabled(
        bool enabled)
    {
        TxtDatabasePath.IsEnabled = enabled;
        BtnBrowse.IsEnabled = enabled;
        BtnSave.IsEnabled = enabled;
        ChkServerEnabled.IsEnabled = enabled;
        TxtInterval.IsEnabled = enabled;

        // Testing and reloading are safe in read-only mode.
        BtnTest.IsEnabled = true;
        BtnReload.IsEnabled = true;
    }

    private void ShowDatabaseInfo(
        WorkLogDatabaseInfo info)
    {
        TxtDatabaseInfo.Text =
            string.Join(
                Environment.NewLine,
                TF(
                    "WLCONFIG.Info.Path",
                    info.DatabasePath),
                TF(
                    "WLCONFIG.Info.Size",
                    info.FileSizeText),
                TF(
                    "WLCONFIG.Info.Sqlite",
                    info.SqliteVersion),
                TF(
                    "WLCONFIG.Info.Users",
                    info.ActiveUsers,
                    info.Users),
                TF(
                    "WLCONFIG.Info.Projects",
                    info.Projects),
                TF(
                    "WLCONFIG.Info.Entries",
                    info.TimeEntries),
                TF(
                    "WLCONFIG.Info.Attendance",
                    info.ArrivalsDepartures),
                TF(
                    "WLCONFIG.Info.LockedDays",
                    info.LockedSpecialDays));
    }

    private void BtnBrowse_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title =
                T("WLCONFIG.FileDialog.Title"),
            Filter =
                T("WLCONFIG.FileDialog.Filter"),
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            TxtDatabasePath.Text =
                dialog.FileName;
        }
    }

    private void BtnTest_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var repository =
                new WorkLogRepository(
                    TxtDatabasePath.Text.Trim());

            repository.TestConnection();
            ShowDatabaseInfo(
                repository.GetDatabaseInfo());

            TxtStatus.Text =
                T("WLCONFIG.Status.TestOk");
        }
        catch (Exception ex)
        {
            TxtStatus.Text =
                TF(
                    "WLCONFIG.Status.TestFailed",
                    ex.Message);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WLCONFIG.Dialog.ErrorTitle"),
                TF(
                    "WLCONFIG.Dialog.TestFailed",
                    ex.Message));
        }
    }

    private void BtnSave_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_access?.IsAdministrator != true &&
            !_isDmsAdmin)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WLCONFIG.Dialog.ValidationTitle"),
                T("WLCONFIG.Dialog.AdminOnly"));
            return;
        }

        var path =
            TxtDatabasePath.Text.Trim();

        if (string.IsNullOrWhiteSpace(path))
        {
            path =
                WorkLogSettings.DefaultDatabasePath;
        }

        if (!int.TryParse(
                TxtInterval.Text.Trim(),
                out var interval) ||
            interval <= 0)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WLCONFIG.Dialog.ValidationTitle"),
                T("WLCONFIG.Dialog.InvalidInterval"));
            return;
        }

        try
        {
            var repository =
                new WorkLogRepository(path);
            repository.TestConnection();

            var oldPath =
                _settings.DatabasePath;
            var oldEnabled =
                _settings.ServerClientEnabled;
            var oldInterval =
                _settings.ServerTaskIntervalMinutes;

            _settings.DatabasePath = path;
            _settings.ServerClientEnabled =
                ChkServerEnabled.IsChecked == true;
            _settings.ServerTaskIntervalMinutes =
                interval;

            _settingsService.Save(_settings);

            LogSettingChange(
                "DatabasePath",
                oldPath,
                _settings.DatabasePath);
            LogSettingChange(
                "ServerClientEnabled",
                oldEnabled.ToString(),
                _settings.ServerClientEnabled
                    .ToString());
            LogSettingChange(
                "ServerTaskIntervalMinutes",
                oldInterval.ToString(),
                interval.ToString());

            _logger?.AdminAction(
                "WLCONFIG",
                "SaveSettings",
                _currentUserName,
                $"Path={path}; ServerClientEnabled={_settings.ServerClientEnabled}; IntervalMinutes={interval}");

            ShowDatabaseInfo(
                repository.GetDatabaseInfo());

            TxtStatus.Text =
                T("WLCONFIG.Status.Saved");
        }
        catch (Exception ex)
        {
            _logger?.Error(
                "WLCONFIG: save failed.",
                ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WLCONFIG.Dialog.ErrorTitle"),
                TF(
                    "WLCONFIG.Dialog.SaveFailed",
                    ex.Message));
        }
    }

    private void BtnReload_Click(
        object sender,
        RoutedEventArgs e)
    {
        LoadSettingsAndInfo();
    }

    private void LogSettingChange(
        string field,
        string? oldValue,
        string? newValue)
    {
        if (string.Equals(
                oldValue,
                newValue,
                StringComparison.Ordinal))
        {
            return;
        }

        _logger?.AuditChange(
            "WORKLOG",
            "Configuration",
            "worklog-settings.json",
            field,
            oldValue,
            newValue,
            _currentUserName);
    }

    private string T(string key)
    {
        if (_translate is null)
        {
            return key;
        }

        var value = _translate(key);

        return string.IsNullOrWhiteSpace(value) ||
               string.Equals(
                   value,
                   $"[[{key}]]",
                   StringComparison.OrdinalIgnoreCase)
            ? key
            : value;
    }

    private string TF(
        string key,
        params object[] args)
    {
        if (_translateFormat is not null)
        {
            return _translateFormat(key, args);
        }

        try
        {
            return string.Format(
                T(key),
                args);
        }
        catch
        {
            return T(key);
        }
    }
}
