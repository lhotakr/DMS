using DMS.Desktop.Logging;
using DMS.Desktop.Views.Dialogs;
using DMS.Desktop.WorkLog;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.WorkLog;

public partial class WorkLogLockView : UserControl
{
    private readonly WorkLogSettingsService _settingsService;
    private readonly DmsLogger? _logger;
    private readonly string _windowsLogin;
    private readonly string _currentUserName;
    private readonly bool _isDmsAdmin;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    private WorkLogRepository? _repository;
    private WorkLogAccessPolicy? _access;
    private bool _loading;

    public WorkLogLockView(
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

        var today = DateTime.Today;
        DateFrom.SelectedDate =
            new DateTime(today.Year, today.Month, 1);
        DateTo.SelectedDate =
            new DateTime(
                today.Year,
                today.Month,
                DateTime.DaysInMonth(
                    today.Year,
                    today.Month));

        LoadData();
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("WLLOCK.Title");
        TxtSubtitle.Text = T("WLLOCK.Subtitle");
        LblFrom.Text = T("WLLOCK.From");
        LblTo.Text = T("WLLOCK.To");
        BtnLock.Content = T("WLLOCK.Lock");
        BtnUnlock.Content = T("WLLOCK.Unlock");
        BtnReload.Content = T("WLLOCK.Reload");
        TxtDaysTitle.Text = T("WLLOCK.Days");
        TxtHint.Text = T("WLLOCK.Hint");

        ColDate.Header = T("WLLOCK.Col.Date");
        ColTitle.Header = T("WLLOCK.Col.Title");
        ColLocked.Header = T("WLLOCK.Col.Locked");
        ColColor.Header = T("WLLOCK.Col.Color");
    }

    private void LoadData()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;

        try
        {
            var settings = _settingsService.Load();
            _repository =
                new WorkLogRepository(settings.DatabasePath);
            _repository.TestConnection();

            var current =
                _repository.FindUserByWindowsUsername(
                    _windowsLogin);
            _access =
                new WorkLogAccessPolicy(
                    current,
                    _isDmsAdmin);

            var admin = _access.IsAdministrator;

            BtnLock.IsEnabled = admin;
            BtnUnlock.IsEnabled = admin;

            if (!admin)
            {
                GridDays.ItemsSource = null;
                TxtStatus.Text =
                    T("WLLOCK.Status.AccessDenied");
                return;
            }

            var (from, to) = GetRange();

            var days =
                _repository.GetSpecialDays(
                    from,
                    to);

            GridDays.ItemsSource = days;

            TxtStatus.Text = TF(
                "WLLOCK.Status.Loaded",
                days.Count,
                days.Count(day => day.Locked));
        }
        catch (Exception ex)
        {
            BtnLock.IsEnabled = false;
            BtnUnlock.IsEnabled = false;

            TxtStatus.Text = TF(
                "WLLOCK.Status.LoadFailed",
                ex.Message);

            _logger?.Error(
                "WLLOCK: load failed.",
                ex);
        }
        finally
        {
            _loading = false;
        }
    }

    private (DateTime From, DateTime To) GetRange()
    {
        var from =
            DateFrom.SelectedDate?.Date
            ?? DateTime.Today;
        var to =
            DateTo.SelectedDate?.Date
            ?? from;

        return from <= to
            ? (from, to)
            : (to, from);
    }

    private void DateRange_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_loading &&
            IsLoaded)
        {
            LoadData();
        }
    }

    private void BtnLock_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetRangeLocked(true);
    }

    private void BtnUnlock_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetRangeLocked(false);
    }

    private void SetRangeLocked(bool locked)
    {
        var repository = _repository;

        if (repository is null ||
            _access?.IsAdministrator != true)
        {
            return;
        }

        var (from, to) = GetRange();

        var questionKey =
            locked
                ? "WLLOCK.Dialog.LockQuestion"
                : "WLLOCK.Dialog.UnlockQuestion";

        if (!DmsConfirmDialog.ShowQuestion(
                Window.GetWindow(this),
                T("WLLOCK.Dialog.Title"),
                TF(
                    questionKey,
                    from,
                    to)))
        {
            return;
        }

        try
        {
            repository.SetLockedRange(
                from,
                to,
                locked);

            _logger?.AdminAction(
                "WLLOCK",
                locked
                    ? "LockRange"
                    : "UnlockRange",
                _currentUserName,
                $"From={from:yyyy-MM-dd}; To={to:yyyy-MM-dd}");

            // This is a user-visible data change. Log each day so LOG03 can
            // reconstruct the exact closure/reopening range.
            for (var date = from.Date;
                 date <= to.Date;
                 date = date.AddDays(1))
            {
                _logger?.AuditChange(
                    "WORKLOG",
                    "DayLock",
                    date.ToString("yyyy-MM-dd"),
                    "Locked",
                    locked ? "false" : "true",
                    locked ? "true" : "false",
                    _currentUserName);
            }

            LoadData();

            TxtStatus.Text =
                locked
                    ? T("WLLOCK.Status.Locked")
                    : T("WLLOCK.Status.Unlocked");
        }
        catch (Exception ex)
        {
            _logger?.Error(
                "WLLOCK: lock state change failed.",
                ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WLLOCK.Dialog.ErrorTitle"),
                TF(
                    "WLLOCK.Dialog.OperationFailed",
                    ex.Message));
        }
    }

    private void BtnReload_Click(
        object sender,
        RoutedEventArgs e)
    {
        LoadData();
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
