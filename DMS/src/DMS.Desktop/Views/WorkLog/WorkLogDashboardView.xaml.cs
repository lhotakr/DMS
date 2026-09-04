using DMS.Desktop.Logging;
using DMS.Desktop.Views.Dialogs;
using DMS.Desktop.WorkLog;
using Microsoft.Win32;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DMS.Desktop.Views.WorkLog;

public partial class WorkLogDashboardView : UserControl
{
    private const int SnapMinutes = 15;
    private const int DefaultEntryMinutes = 30;
    private const double HourHeight = 72.0;
    private const double TimeLabelWidth = 64.0;
    private const double TimelineRightPadding = 12.0;

    private readonly WorkLogSettingsService _settingsService;
    private readonly DmsLogger? _logger;
    private readonly string _windowsLogin;
    private readonly string _currentUserName;
    private readonly bool _isDmsAdmin;
    private readonly Func<string, string>? _translate;
    private readonly Func<string, object[], string>? _translateFormat;

    private WorkLogSettings _settings = new();
    private WorkLogRepository? _repository;
    private WorkLogAccessPolicy? _access;
    private IReadOnlyList<WorkLogProject> _projects = Array.Empty<WorkLogProject>();
    private IReadOnlyList<WorkLogEntryType> _entryTypes = Array.Empty<WorkLogEntryType>();
    private IReadOnlyList<WorkLogTimeEntry> _entries = Array.Empty<WorkLogTimeEntry>();
    private WorkLogArrivalDeparture? _attendance;
    private WorkLogTimeEntry? _selectedEntry;
    private readonly HashSet<int> _selectedEntryIds = new();
    private readonly Dictionary<int, Border> _entryVisuals = new();

    private DateTime _selectedDate = DateTime.Today;
    private DateTime? _draftStart;
    private int _draftMinutes = DefaultEntryMinutes;
    private int _timelineStartHour = 5;
    private int _timelineEndHour = 18;
    private bool _loading;
    private bool _syncingCalendar;

    private bool _dragCandidate;
    private bool _dragging;
    private Point _dragStartPoint;
    private int _dragPreviewDeltaMinutes;

    public WorkLogDashboardView(
        string configurationRootPath,
        string windowsLogin,
        string currentUserName,
        bool isDmsAdmin,
        DmsLogger? logger = null,
        Func<string, string>? translate = null,
        Func<string, object[], string>? translateFormat = null)
    {
        InitializeComponent();

        _settingsService = new WorkLogSettingsService(configurationRootPath);
        _windowsLogin = windowsLogin ?? string.Empty;
        _currentUserName = string.IsNullOrWhiteSpace(currentUserName)
            ? "UNKNOWN"
            : currentUserName;
        _isDmsAdmin = isDmsAdmin;
        _logger = logger;
        _translate = translate;
        _translateFormat = translateFormat;

        ApplyLocalization();
        SetCalendarDate(_selectedDate, updateDisplayMonth: true);

        LoadDatabase(
            keepSelectedUserId: null,
            showDialogOnFailure: false);
    }

    private void ApplyLocalization()
    {
        TxtTitle.Text = T("WORKLOG.Title");
        TxtSubtitle.Text = T("WORKLOG.Subtitle");
        LblDatabase.Text = T("WORKLOG.Database");
        BtnBrowseDatabase.Content = T("WORKLOG.Browse");
        BtnSaveDatabasePath.Content = T("WORKLOG.SavePath");
        BtnReload.Content = T("WORKLOG.Reload");
        LblUser.Text = T("WORKLOG.User");
        LblSelectedDay.Text = T("WORKLOG.SelectedDay");
        TxtCalendarTitle.Text = T("WORKLOG.Calendar");
        BtnToday.Content = LocalizedOr("WORKLOG.Today", "Dnes");
        TxtAttendanceTitle.Text = T("WORKLOG.Attendance");
        TxtArrivalLegend.Text = LocalizedOr("WORKLOG.Agenda.Arrival", "Příchod");
        TxtDepartureLegend.Text = LocalizedOr("WORKLOG.Agenda.Departure", "Odchod");

        TxtAgendaTitle.Text = LocalizedOr("WORKLOG.Agenda.Title", "Denní agenda");
        TxtAgendaHint.Text = LocalizedOr(
            "WORKLOG.Agenda.Hint",
            "Kliknutím do volného prostoru vytvoříš nový 30min blok. Ctrl/Shift + klik označí více bloků; tažením je přesuneš po 15 minutách.");
        BtnMoveEarlier.Content = LocalizedOr("WORKLOG.Agenda.MoveEarlier", "−15 min");
        BtnMoveLater.Content = LocalizedOr("WORKLOG.Agenda.MoveLater", "+15 min");
        BtnClearSelection.Content = LocalizedOr("WORKLOG.Agenda.ClearSelection", "Zrušit výběr");

        TxtEditorTitle.Text = T("WORKLOG.Editor");
        LblProject.Text = T("WORKLOG.Project");
        LblTime.Text = T("WORKLOG.Time");
        LblEntryType.Text = T("WORKLOG.EntryType");
        LblMinutes.Text = T("WORKLOG.Minutes");
        LblDescription.Text = T("WORKLOG.Description");
        LblNote.Text = T("WORKLOG.Note");
        BtnNewEntry.Content = T("WORKLOG.New");
        BtnSaveEntry.Content = T("WORKLOG.Save");
        BtnDeleteEntry.Content = T("WORKLOG.Delete");

        TxtDescription.ToolTip = T("WORKLOG.Description");
        TxtNote.ToolTip = T("WORKLOG.Note");

        UpdateSelectionInfo();
    }

    private void LoadDatabase(
        int? keepSelectedUserId,
        bool showDialogOnFailure)
    {
        _loading = true;

        try
        {
            _settings = _settingsService.Load();
            TxtDatabasePath.Text = _settings.DatabasePath;

            _repository = new WorkLogRepository(_settings.DatabasePath);
            _repository.TestConnection();

            var currentWorkLogUser = _repository.FindUserByWindowsUsername(_windowsLogin);
            _access = new WorkLogAccessPolicy(currentWorkLogUser, _isDmsAdmin);

            if (!_access.CanUseDashboard)
            {
                CmbUser.ItemsSource = null;
                _entries = Array.Empty<WorkLogTimeEntry>();
                _attendance = null;
                ClearTimeline();
                SetEditorEnabled(false);

                TxtStatus.Text = currentWorkLogUser is null
                    ? TF(
                        "WORKLOG.Status.UserNotFound",
                        WorkLogRepository.NormalizeWindowsLogin(_windowsLogin))
                    : T("WORKLOG.Status.AccessDenied");

                return;
            }

            var allUsers = _repository.GetUsers(includeArchived: false);
            var accessible = _access.FilterAccessibleUsers(allUsers);
            CmbUser.ItemsSource = accessible;

            WorkLogUser? selected = null;

            if (keepSelectedUserId.HasValue)
            {
                selected = accessible.FirstOrDefault(user => user.Id == keepSelectedUserId.Value);
            }

            selected ??= accessible.FirstOrDefault(user => user.Id == currentWorkLogUser?.Id);
            selected ??= accessible.FirstOrDefault();
            CmbUser.SelectedItem = selected;

            _projects = _repository.GetProjects(includeArchived: false);
            _entryTypes = _repository.GetEntryTypes();
            CmbProject.ItemsSource = _projects;
            RefreshEntryTypesForProject();

            var info = _repository.GetDatabaseInfo();
            TxtStatus.Text = TF(
                "WORKLOG.Status.Loaded",
                info.ActiveUsers,
                info.Projects,
                info.TimeEntries);
        }
        catch (Exception ex)
        {
            _repository = null;
            _access = null;
            CmbUser.ItemsSource = null;
            _entries = Array.Empty<WorkLogTimeEntry>();
            _attendance = null;
            ClearTimeline();
            SetEditorEnabled(false);

            TxtStatus.Text = TF("WORKLOG.Status.LoadFailed", ex.Message);
            _logger?.Error("WORKLOG: database load failed.", ex);

            if (showDialogOnFailure)
            {
                DmsConfirmDialog.ShowInfo(
                    Window.GetWindow(this),
                    T("WORKLOG.Dialog.ErrorTitle"),
                    TF("WORKLOG.Dialog.LoadFailed", ex.Message));
            }
        }
        finally
        {
            _loading = false;
        }

        if (_repository is not null && _access?.CanUseDashboard == true)
        {
            RefreshSelectedDay(scrollToWorkStart: true);
        }
    }

    private void RefreshSelectedDay(bool scrollToWorkStart = false)
    {
        if (_loading)
        {
            return;
        }

        var repository = _repository;
        var access = _access;
        var user = CmbUser.SelectedItem as WorkLogUser;
        var date = _selectedDate.Date;

        TxtSelectedDay.Text = date.ToString("dddd d. M. yyyy", CultureInfo.CurrentCulture);
        SetCalendarDate(date, updateDisplayMonth: false);

        if (repository is null || access is null || user is null)
        {
            _entries = Array.Empty<WorkLogTimeEntry>();
            _attendance = null;
            ClearTimeline();
            TxtAttendance.Text = T("WORKLOG.Attendance.None");
            TxtLockState.Text = string.Empty;
            TxtDailyTotal.Text = string.Empty;
            SetEditorEnabled(false);
            return;
        }

        try
        {
            var keepSelection = _selectedEntryIds.ToHashSet();
            var keepPrimaryId = _selectedEntry?.Id;

            _entries = repository.GetTimeEntries(user.Id, date);
            _attendance = repository.GetArrivalDeparture(user.Id, date);

            _selectedEntryIds.Clear();
            foreach (var id in keepSelection)
            {
                if (_entries.Any(entry => entry.Id == id))
                {
                    _selectedEntryIds.Add(id);
                }
            }

            _selectedEntry = keepPrimaryId.HasValue
                ? _entries.FirstOrDefault(entry => entry.Id == keepPrimaryId.Value)
                : null;

            if (_selectedEntry is null && _selectedEntryIds.Count > 0)
            {
                _selectedEntry = _entries.FirstOrDefault(entry => _selectedEntryIds.Contains(entry.Id));
            }

            TxtAttendance.Text = _attendance is null
                ? T("WORKLOG.Attendance.None")
                : TF(
                    "WORKLOG.Attendance.Format",
                    FormatTime(_attendance.ArrivalTimestamp),
                    FormatTime(_attendance.DepartureTimestamp),
                    _attendance.HoursWorked,
                    _attendance.HoursOvertime);

            var locked = repository.IsDayLocked(date);
            TxtLockState.Text = locked ? T("WORKLOG.Locked") : T("WORKLOG.Unlocked");

            var totalMinutes = _entries.Sum(entry => entry.EntryMinutes);
            TxtDailyTotal.Text = TF(
                "WORKLOG.Total",
                totalMinutes / 60,
                totalMinutes % 60);

            CalculateTimelineRange();
            RenderTimeline(scrollToWorkStart);

            if (_selectedEntry is not null)
            {
                LoadEntryIntoEditor(_selectedEntry);
            }
            else if (_draftStart.HasValue)
            {
                LoadDraftIntoEditor();
            }
            else
            {
                ClearEditor(resetTime: true, rerenderTimeline: false);
            }

            SetEditorEnabled(access.CanEditUser(user) && !locked);
            UpdateSelectionInfo();
        }
        catch (Exception ex)
        {
            TxtStatus.Text = TF("WORKLOG.Status.DayLoadFailed", ex.Message);
            _logger?.Error("WORKLOG: day load failed.", ex);
        }
    }

    private void CalculateTimelineRange()
    {
        var minMinute = 5 * 60;
        var maxMinute = 18 * 60;

        foreach (var entry in _entries)
        {
            var start = (int)Math.Floor(entry.Timestamp.TimeOfDay.TotalMinutes);
            var end = start + Math.Max(1, entry.EntryMinutes);
            minMinute = Math.Min(minMinute, start);
            maxMinute = Math.Max(maxMinute, end);
        }

        if (_attendance?.ArrivalTimestamp is DateTime arrival)
        {
            minMinute = Math.Min(minMinute, (int)arrival.TimeOfDay.TotalMinutes);
        }

        if (_attendance?.DepartureTimestamp is DateTime departure)
        {
            maxMinute = Math.Max(maxMinute, (int)departure.TimeOfDay.TotalMinutes);
        }

        _timelineStartHour = Math.Clamp((minMinute / 60) - 1, 0, 22);
        _timelineStartHour = Math.Min(_timelineStartHour, 5);

        _timelineEndHour = Math.Clamp(((maxMinute + 59) / 60) + 1, 2, 24);
        _timelineEndHour = Math.Max(_timelineEndHour, 18);

        if (_timelineEndHour <= _timelineStartHour)
        {
            _timelineEndHour = Math.Min(24, _timelineStartHour + 8);
        }
    }

    private void RenderTimeline(bool scrollToWorkStart = false)
    {
        if (TimelineCanvas is null)
        {
            return;
        }

        var canvasWidth = Math.Max(
            620.0,
            TimelineScrollViewer.ActualWidth > 40
                ? TimelineScrollViewer.ActualWidth - 22
                : 620.0);

        var canvasHeight = (_timelineEndHour - _timelineStartHour) * HourHeight;
        TimelineCanvas.Width = canvasWidth;
        TimelineCanvas.Height = Math.Max(360.0, canvasHeight);
        TimelineCanvas.Children.Clear();
        _entryVisuals.Clear();

        DrawTimeGrid(canvasWidth);
        DrawAttendanceLine(_attendance?.ArrivalTimestamp, Brushes.LimeGreen, LocalizedOr("WORKLOG.Agenda.Arrival", "Příchod"), canvasWidth);
        DrawAttendanceLine(_attendance?.DepartureTimestamp, Brushes.IndianRed, LocalizedOr("WORKLOG.Agenda.Departure", "Odchod"), canvasWidth);
        DrawEntryBlocks(canvasWidth);
        DrawDraftBlock(canvasWidth);

        UpdateSelectionInfo();
        UpdateMoveButtons();

        if (scrollToWorkStart)
        {
            var firstMinute = _attendance?.ArrivalTimestamp is DateTime arrival
                ? (int)arrival.TimeOfDay.TotalMinutes
                : _entries.Count > 0
                    ? (int)_entries.Min(entry => entry.Timestamp.TimeOfDay.TotalMinutes)
                    : _timelineStartHour * 60;

            var offset = Math.Max(0, MinuteOfDayToY(firstMinute) - 28);

            Dispatcher.BeginInvoke(
                () => TimelineScrollViewer.ScrollToVerticalOffset(offset),
                DispatcherPriority.Loaded);
        }
    }

    private void DrawTimeGrid(double canvasWidth)
    {
        for (var hour = _timelineStartHour; hour <= _timelineEndHour; hour++)
        {
            var y = (hour - _timelineStartHour) * HourHeight;

            var label = new TextBlock
            {
                Text = $"{hour:00}:00",
                Width = TimeLabelWidth - 10,
                TextAlignment = TextAlignment.Right,
                FontSize = 12,
                IsHitTestVisible = false
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "DmsMutedForegroundBrush");
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, Math.Max(0, y - 9));
            TimelineCanvas.Children.Add(label);

            var line = new Border
            {
                Width = Math.Max(0, canvasWidth - TimeLabelWidth - TimelineRightPadding),
                Height = 1,
                Opacity = hour == _timelineEndHour ? 0.75 : 0.55,
                IsHitTestVisible = false
            };
            line.SetResourceReference(Border.BackgroundProperty, "DmsBorderBrush");
            Canvas.SetLeft(line, TimeLabelWidth);
            Canvas.SetTop(line, y);
            TimelineCanvas.Children.Add(line);

            if (hour < _timelineEndHour)
            {
                var halfLine = new Border
                {
                    Width = Math.Max(0, canvasWidth - TimeLabelWidth - TimelineRightPadding),
                    Height = 1,
                    Opacity = 0.22,
                    IsHitTestVisible = false
                };
                halfLine.SetResourceReference(Border.BackgroundProperty, "DmsBorderBrush");
                Canvas.SetLeft(halfLine, TimeLabelWidth);
                Canvas.SetTop(halfLine, y + HourHeight / 2.0);
                TimelineCanvas.Children.Add(halfLine);
            }
        }
    }

    private void DrawAttendanceLine(
        DateTime? timestamp,
        Brush brush,
        string caption,
        double canvasWidth)
    {
        if (!timestamp.HasValue || timestamp.Value.Date != _selectedDate.Date)
        {
            return;
        }

        var minute = (int)Math.Round(timestamp.Value.TimeOfDay.TotalMinutes);
        if (minute < _timelineStartHour * 60 || minute > _timelineEndHour * 60)
        {
            return;
        }

        var y = MinuteOfDayToY(minute);
        var line = new Border
        {
            Width = Math.Max(0, canvasWidth - TimeLabelWidth - TimelineRightPadding),
            Height = 3,
            Background = brush,
            Opacity = 0.95,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(line, TimeLabelWidth);
        Canvas.SetTop(line, y - 1.5);
        TimelineCanvas.Children.Add(line);

        var badge = new Border
        {
            Background = brush,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = $"{caption} {timestamp.Value:HH:mm}",
                Foreground = Brushes.Black,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            }
        };
        Canvas.SetLeft(badge, Math.Max(TimeLabelWidth + 4, canvasWidth - 128));
        Canvas.SetTop(badge, Math.Max(0, y - 22));
        TimelineCanvas.Children.Add(badge);
    }

    private void DrawEntryBlocks(double canvasWidth)
    {
        var laneMap = CalculateEntryLanes(_entries);
        var laneCount = laneMap.Count == 0
            ? 1
            : Math.Max(1, laneMap.Values.Max() + 1);
        var availableWidth = Math.Max(240.0, canvasWidth - TimeLabelWidth - TimelineRightPadding - 8);
        var laneWidth = availableWidth / laneCount;

        foreach (var entry in _entries.OrderBy(item => item.Timestamp).ThenBy(item => item.Id))
        {
            var lane = laneMap.TryGetValue(entry.Id, out var value) ? value : 0;
            var startMinute = (int)Math.Round(entry.Timestamp.TimeOfDay.TotalMinutes);
            var top = MinuteOfDayToY(startMinute) + 2;
            var height = Math.Max(28.0, entry.EntryMinutes * PixelsPerMinute - 4);
            var left = TimeLabelWidth + 4 + lane * laneWidth;
            var width = Math.Max(110.0, laneWidth - 7);
            var selected = _selectedEntryIds.Contains(entry.Id);

            var background = ParseColorBrush(entry.EntryTypeColor, 205);
            var borderBrush = ParseColorBrush(entry.EntryTypeColor, 255);
            var textBrush = GetContrastingBrush(background.Color);

            var titleText = string.IsNullOrWhiteSpace(entry.Description)
                ? entry.ProjectTitle
                : entry.Description;
            var subtitleText = string.Join(
                "  •  ",
                new[]
                {
                    entry.ProjectTitle,
                    entry.EntryTypeTitle,
                    $"{entry.Timestamp:HH:mm}–{entry.Timestamp.AddMinutes(entry.EntryMinutes):HH:mm}"
                }.Where(text => !string.IsNullOrWhiteSpace(text)));

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = titleText,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = textBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.Wrap
            });

            if (height >= 42)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = subtitleText,
                    Margin = new Thickness(0, 2, 0, 0),
                    FontSize = 10,
                    Foreground = textBrush,
                    Opacity = 0.88,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            var border = new Border
            {
                Tag = entry,
                Width = width,
                Height = height,
                Background = background,
                BorderBrush = borderBrush,
                BorderThickness = selected ? new Thickness(3) : new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 5, 8, 4),
                Cursor = Cursors.Hand,
                ToolTip = BuildEntryToolTip(entry),
                Child = stack
            };

            if (selected)
            {
                border.SetResourceReference(Border.BorderBrushProperty, "DmsAccentBrush");
            }

            border.MouseLeftButtonDown += EntryBlock_MouseLeftButtonDown;
            Canvas.SetLeft(border, left);
            Canvas.SetTop(border, top);
            Panel.SetZIndex(border, selected ? 30 : 10);
            TimelineCanvas.Children.Add(border);
            _entryVisuals[entry.Id] = border;
        }
    }

    private Dictionary<int, int> CalculateEntryLanes(IEnumerable<WorkLogTimeEntry> source)
    {
        var laneEnds = new List<DateTime>();
        var result = new Dictionary<int, int>();

        foreach (var entry in source.OrderBy(item => item.Timestamp).ThenBy(item => item.Id))
        {
            var lane = -1;
            for (var index = 0; index < laneEnds.Count; index++)
            {
                if (laneEnds[index] <= entry.Timestamp)
                {
                    lane = index;
                    break;
                }
            }

            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(entry.Timestamp.AddMinutes(entry.EntryMinutes));
            }
            else
            {
                laneEnds[lane] = entry.Timestamp.AddMinutes(entry.EntryMinutes);
            }

            result[entry.Id] = lane;
        }

        return result;
    }

    private void DrawDraftBlock(double canvasWidth)
    {
        if (!_draftStart.HasValue || _draftStart.Value.Date != _selectedDate.Date)
        {
            return;
        }

        var minute = (int)Math.Round(_draftStart.Value.TimeOfDay.TotalMinutes);
        var top = MinuteOfDayToY(minute) + 2;
        var height = Math.Max(28.0, _draftMinutes * PixelsPerMinute - 4);

        var border = new Border
        {
            Width = Math.Max(160, canvasWidth - TimeLabelWidth - TimelineRightPadding - 16),
            Height = height,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(8, 5, 8, 4),
            Opacity = 0.75,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = $"{LocalizedOr("WORKLOG.Agenda.NewBlock", "Nový blok")}  {_draftStart.Value:HH:mm}–{_draftStart.Value.AddMinutes(_draftMinutes):HH:mm}",
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            }
        };
        border.SetResourceReference(Border.BackgroundProperty, "DmsPanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "DmsAccentBrush");
        ((TextBlock)border.Child).SetResourceReference(TextBlock.ForegroundProperty, "DmsForegroundBrush");

        Canvas.SetLeft(border, TimeLabelWidth + 8);
        Canvas.SetTop(border, top);
        Panel.SetZIndex(border, 25);
        TimelineCanvas.Children.Add(border);
    }

    private string BuildEntryToolTip(WorkLogTimeEntry entry)
    {
        return $"{entry.Timestamp:HH:mm}–{entry.Timestamp.AddMinutes(entry.EntryMinutes):HH:mm}\n" +
               $"{entry.ProjectTitle}\n" +
               $"{entry.EntryTypeTitle}\n" +
               (string.IsNullOrWhiteSpace(entry.Description) ? string.Empty : entry.Description);
    }

    private double PixelsPerMinute => HourHeight / 60.0;

    private double MinuteOfDayToY(int minuteOfDay)
    {
        return (minuteOfDay - _timelineStartHour * 60) * PixelsPerMinute;
    }

    private int YToMinuteOfDay(double y)
    {
        var raw = _timelineStartHour * 60 + y / PixelsPerMinute;
        var snapped = (int)Math.Round(raw / SnapMinutes) * SnapMinutes;
        return Math.Clamp(snapped, 0, 24 * 60 - SnapMinutes);
    }

    private void ClearTimeline()
    {
        if (TimelineCanvas is null)
        {
            return;
        }

        TimelineCanvas.Children.Clear();
        _entryVisuals.Clear();
        _selectedEntryIds.Clear();
        _selectedEntry = null;
        _draftStart = null;
        UpdateSelectionInfo();
        UpdateMoveButtons();
    }

    private void EntryBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not WorkLogTimeEntry entry)
        {
            return;
        }

        TimelineCanvas.Focus();
        _draftStart = null;

        var additive = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None;

        if (additive)
        {
            if (!_selectedEntryIds.Add(entry.Id))
            {
                _selectedEntryIds.Remove(entry.Id);
            }
        }
        else if (!_selectedEntryIds.Contains(entry.Id))
        {
            _selectedEntryIds.Clear();
            _selectedEntryIds.Add(entry.Id);
        }

        if (_selectedEntryIds.Contains(entry.Id))
        {
            _selectedEntry = entry;
            LoadEntryIntoEditor(entry);
        }
        else
        {
            SyncPrimarySelectionFromIds();
        }

        RenderTimeline(scrollToWorkStart: false);

        if (CanMoveSelection())
        {
            _dragCandidate = true;
            _dragging = false;
            _dragStartPoint = e.GetPosition(TimelineCanvas);
            _dragPreviewDeltaMinutes = 0;
        }

        e.Handled = true;
    }

    private void TimelineCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        TimelineCanvas.Focus();
        var point = e.GetPosition(TimelineCanvas);

        if (point.X < TimeLabelWidth)
        {
            ClearSelection(rerender: true);
            return;
        }

        var user = CmbUser.SelectedItem as WorkLogUser;
        if (user is null || _access?.CanEditUser(user) != true || _repository?.IsDayLocked(_selectedDate.Date) != false)
        {
            ClearSelection(rerender: true);
            return;
        }

        var minute = YToMinuteOfDay(point.Y);
        _selectedEntryIds.Clear();
        _selectedEntry = null;
        _draftStart = _selectedDate.Date.AddMinutes(minute);
        _draftMinutes = DefaultEntryMinutes;
        LoadDraftIntoEditor();
        RenderTimeline(scrollToWorkStart: false);
        TxtDescription.Focus();
        e.Handled = true;
    }

    private void TimelineCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragCandidate || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(TimelineCanvas);
        var deltaY = current.Y - _dragStartPoint.Y;

        if (!_dragging && Math.Abs(deltaY) < 5)
        {
            return;
        }

        if (!_dragging)
        {
            _dragging = true;
            TimelineCanvas.CaptureMouse();
        }

        var rawMinutes = deltaY / PixelsPerMinute;
        var snapped = (int)Math.Round(rawMinutes / SnapMinutes) * SnapMinutes;
        var clamped = ClampMoveDelta(snapped);
        _dragPreviewDeltaMinutes = clamped;

        foreach (var entry in GetSelectedEntries())
        {
            if (!_entryVisuals.TryGetValue(entry.Id, out var visual))
            {
                continue;
            }

            var minute = (int)Math.Round(entry.Timestamp.TimeOfDay.TotalMinutes) + clamped;
            Canvas.SetTop(visual, MinuteOfDayToY(minute) + 2);
        }

        TxtStatus.Text = FormatLocalizedOr(
            "WORKLOG.Agenda.MovePreview",
            "Přesun vybraných bloků: {0:+#;-#;0} min",
            clamped);

        e.Handled = true;
    }

    private void TimelineCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragCandidate)
        {
            return;
        }

        var wasDragging = _dragging;
        var delta = _dragPreviewDeltaMinutes;

        _dragCandidate = false;
        _dragging = false;
        _dragPreviewDeltaMinutes = 0;

        if (TimelineCanvas.IsMouseCaptured)
        {
            TimelineCanvas.ReleaseMouseCapture();
        }

        if (wasDragging && delta != 0)
        {
            MoveSelectedEntriesBy(delta);
        }
        else if (wasDragging)
        {
            RenderTimeline(scrollToWorkStart: false);
        }

        e.Handled = wasDragging;
    }

    private void TimelineCanvas_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClearSelection(rerender: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _selectedEntryIds.Clear();
            foreach (var entry in _entries.Where(entry => !entry.IsLocked))
            {
                _selectedEntryIds.Add(entry.Id);
            }

            SyncPrimarySelectionFromIds();
            RenderTimeline(scrollToWorkStart: false);
            e.Handled = true;
        }
    }

    private void TimelineCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_loading && e.WidthChanged)
        {
            RenderTimeline(scrollToWorkStart: false);
        }
    }

    private void MoveSelectedEntriesBy(int deltaMinutes)
    {
        if (deltaMinutes == 0 || _repository is null)
        {
            return;
        }

        var user = CmbUser.SelectedItem as WorkLogUser;
        var selected = GetSelectedEntries();

        if (user is null || selected.Count == 0 || !CanMoveSelection())
        {
            return;
        }

        var delta = ClampMoveDelta(deltaMinutes);
        if (delta == 0)
        {
            RenderTimeline(scrollToWorkStart: false);
            return;
        }

        try
        {
            var before = selected.ToDictionary(entry => entry.Id, entry => entry.Timestamp);
            _repository.MoveTimeEntries(
                selected.Select(entry => entry.Id).ToArray(),
                user.Id,
                TimeSpan.FromMinutes(delta));

            foreach (var entry in selected)
            {
                LogChange(
                    before[entry.Id].ToString("yyyy-MM-dd HH:mm:ss"),
                    before[entry.Id].AddMinutes(delta).ToString("yyyy-MM-dd HH:mm:ss"),
                    "Timestamp",
                    entry.Id);
            }

            TxtStatus.Text = FormatLocalizedOr(
                "WORKLOG.Agenda.MoveDone",
                "Přesunuto {0} bloků o {1:+#;-#;0} min.",
                selected.Count,
                delta);

            RefreshSelectedDay(scrollToWorkStart: false);
        }
        catch (Exception ex)
        {
            _logger?.Error("WORKLOG: moving time entries failed.", ex);
            RenderTimeline(scrollToWorkStart: false);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WORKLOG.Dialog.ErrorTitle"),
                FormatLocalizedOr(
                    "WORKLOG.Agenda.MoveFailed",
                    "Přesun bloků se nepodařil: {0}",
                    ex.Message));
        }
    }

    private int ClampMoveDelta(int requestedDelta)
    {
        var selected = GetSelectedEntries();
        if (selected.Count == 0)
        {
            return 0;
        }

        var firstMinute = selected.Min(entry => (int)Math.Round(entry.Timestamp.TimeOfDay.TotalMinutes));
        var lastMinute = selected.Max(entry =>
            (int)Math.Round(entry.Timestamp.TimeOfDay.TotalMinutes) + Math.Max(1, entry.EntryMinutes));

        var minDelta = -firstMinute;
        var maxDelta = 24 * 60 - lastMinute;
        return Math.Clamp(requestedDelta, minDelta, maxDelta);
    }

    private bool CanMoveSelection()
    {
        var user = CmbUser.SelectedItem as WorkLogUser;
        if (user is null || _access?.CanEditUser(user) != true || _repository is null)
        {
            return false;
        }

        if (_repository.IsDayLocked(_selectedDate.Date))
        {
            return false;
        }

        var selected = GetSelectedEntries();
        return selected.Count > 0 && selected.All(entry => !entry.IsLocked);
    }

    private List<WorkLogTimeEntry> GetSelectedEntries()
    {
        return _entries
            .Where(entry => _selectedEntryIds.Contains(entry.Id))
            .OrderBy(entry => entry.Timestamp)
            .ThenBy(entry => entry.Id)
            .ToList();
    }

    private void ClearSelection(bool rerender)
    {
        _selectedEntryIds.Clear();
        _selectedEntry = null;
        _draftStart = null;
        ClearEditor(resetTime: true, rerenderTimeline: false);
        UpdateSelectionInfo();
        UpdateMoveButtons();

        if (rerender)
        {
            RenderTimeline(scrollToWorkStart: false);
        }
    }

    private void SyncPrimarySelectionFromIds()
    {
        _selectedEntry = _entries.FirstOrDefault(entry => _selectedEntryIds.Contains(entry.Id));
        if (_selectedEntry is not null)
        {
            LoadEntryIntoEditor(_selectedEntry);
        }
        else
        {
            ClearEditor(resetTime: true, rerenderTimeline: false);
        }
    }

    private void UpdateSelectionInfo()
    {
        TxtSelectionInfo.Text = _selectedEntryIds.Count switch
        {
            0 => LocalizedOr("WORKLOG.Agenda.NoSelection", "Bez výběru"),
            1 => LocalizedOr("WORKLOG.Agenda.OneSelected", "1 blok vybrán"),
            _ => FormatLocalizedOr("WORKLOG.Agenda.SelectedCount", "Vybráno bloků: {0}", _selectedEntryIds.Count)
        };
    }

    private void UpdateMoveButtons()
    {
        var enabled = CanMoveSelection();
        BtnMoveEarlier.IsEnabled = enabled;
        BtnMoveLater.IsEnabled = enabled;
        BtnClearSelection.IsEnabled = _selectedEntryIds.Count > 0 || _draftStart.HasValue;
    }

    private void SetEditorEnabled(bool enabled)
    {
        CmbProject.IsEnabled = enabled;
        CmbEntryType.IsEnabled = enabled;
        TxtTime.IsEnabled = enabled;
        TxtMinutes.IsEnabled = enabled;
        TxtDescription.IsEnabled = enabled;
        TxtNote.IsEnabled = enabled;
        BtnNewEntry.IsEnabled = enabled;
        BtnSaveEntry.IsEnabled = enabled;
        BtnDeleteEntry.IsEnabled =
            enabled &&
            _selectedEntry is not null &&
            !_selectedEntry.IsLocked;

        UpdateMoveButtons();
    }

    private void ClearEditor(bool resetTime, bool rerenderTimeline)
    {
        _selectedEntry = null;

        CmbProject.SelectedItem = _projects.FirstOrDefault();
        RefreshEntryTypesForProject();
        CmbEntryType.SelectedItem = (CmbEntryType.ItemsSource as IEnumerable<WorkLogEntryType>)?.FirstOrDefault();

        if (resetTime)
        {
            TxtTime.Text = "08:00";
            TxtMinutes.Text = DefaultEntryMinutes.ToString(CultureInfo.InvariantCulture);
        }

        TxtDescription.Text = string.Empty;
        TxtNote.Text = string.Empty;

        var user = CmbUser.SelectedItem as WorkLogUser;
        var locked = _repository is not null && _repository.IsDayLocked(_selectedDate.Date);
        SetEditorEnabled(user is not null && _access?.CanEditUser(user) == true && !locked);

        if (rerenderTimeline)
        {
            RenderTimeline(scrollToWorkStart: false);
        }
    }

    private void LoadDraftIntoEditor()
    {
        if (!_draftStart.HasValue)
        {
            return;
        }

        _selectedEntry = null;
        CmbProject.SelectedItem = _projects.FirstOrDefault();
        RefreshEntryTypesForProject();
        CmbEntryType.SelectedItem = (CmbEntryType.ItemsSource as IEnumerable<WorkLogEntryType>)?.FirstOrDefault();
        TxtTime.Text = _draftStart.Value.ToString("HH:mm");
        TxtMinutes.Text = _draftMinutes.ToString(CultureInfo.InvariantCulture);
        TxtDescription.Text = string.Empty;
        TxtNote.Text = string.Empty;
        BtnDeleteEntry.IsEnabled = false;
    }

    private void LoadEntryIntoEditor(WorkLogTimeEntry entry)
    {
        _draftStart = null;
        _selectedEntry = entry;

        CmbProject.SelectedItem = _projects.FirstOrDefault(project => project.Id == entry.ProjectId);
        RefreshEntryTypesForProject();
        CmbEntryType.SelectedItem = (CmbEntryType.ItemsSource as IEnumerable<WorkLogEntryType>)?
            .FirstOrDefault(type => type.Id == entry.EntryTypeId);

        TxtTime.Text = entry.Timestamp.ToString("HH:mm");
        TxtMinutes.Text = entry.EntryMinutes.ToString(CultureInfo.InvariantCulture);
        TxtDescription.Text = entry.Description;
        TxtNote.Text = entry.Note;

        var user = CmbUser.SelectedItem as WorkLogUser;
        var enabled =
            user is not null &&
            _access?.CanEditUser(user) == true &&
            !entry.IsLocked &&
            _repository?.IsDayLocked(entry.Timestamp.Date) == false;

        SetEditorEnabled(enabled);
        BtnDeleteEntry.IsEnabled = enabled;
    }

    private void RefreshEntryTypesForProject()
    {
        var selectedProject = CmbProject.SelectedItem as WorkLogProject;
        IEnumerable<WorkLogEntryType> filtered = _entryTypes;

        if (selectedProject is not null)
        {
            var exact = _entryTypes
                .Where(type => type.ForProjectType == selectedProject.ProjectType)
                .ToList();

            if (exact.Count > 0)
            {
                filtered = exact;
            }
        }

        var currentId = (CmbEntryType.SelectedItem as WorkLogEntryType)?.Id;
        var list = filtered.OrderBy(type => type.Title).ToList();
        CmbEntryType.ItemsSource = list;
        CmbEntryType.SelectedItem = list.FirstOrDefault(type => type.Id == currentId) ?? list.FirstOrDefault();
    }

    private void CmbUser_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _selectedEntryIds.Clear();
        _selectedEntry = null;
        _draftStart = null;
        RefreshSelectedDay(scrollToWorkStart: true);
    }

    private void MonthCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _syncingCalendar || !MonthCalendar.SelectedDate.HasValue)
        {
            return;
        }

        _selectedDate = MonthCalendar.SelectedDate.Value.Date;
        _selectedEntryIds.Clear();
        _selectedEntry = null;
        _draftStart = null;
        RefreshSelectedDay(scrollToWorkStart: true);
    }

    private void MonthCalendar_DisplayDateChanged(object sender, CalendarDateChangedEventArgs e)
    {
        // Displaying another month does not change the selected work day until the user clicks a date.
    }

    private void SetCalendarDate(DateTime date, bool updateDisplayMonth)
    {
        if (MonthCalendar is null)
        {
            return;
        }

        _syncingCalendar = true;
        try
        {
            MonthCalendar.SelectedDate = date.Date;
            if (updateDisplayMonth || MonthCalendar.DisplayDate.Month != date.Month || MonthCalendar.DisplayDate.Year != date.Year)
            {
                MonthCalendar.DisplayDate = date.Date;
            }
        }
        finally
        {
            _syncingCalendar = false;
        }
    }

    private void BtnToday_Click(object sender, RoutedEventArgs e)
    {
        _selectedDate = DateTime.Today;
        _selectedEntryIds.Clear();
        _selectedEntry = null;
        _draftStart = null;
        SetCalendarDate(_selectedDate, updateDisplayMonth: true);
        RefreshSelectedDay(scrollToWorkStart: true);
    }

    private void BtnMoveEarlier_Click(object sender, RoutedEventArgs e) => MoveSelectedEntriesBy(-SnapMinutes);

    private void BtnMoveLater_Click(object sender, RoutedEventArgs e) => MoveSelectedEntriesBy(SnapMinutes);

    private void BtnClearSelection_Click(object sender, RoutedEventArgs e) => ClearSelection(rerender: true);

    private void CmbProject_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            RefreshEntryTypesForProject();
        }
    }

    private void BtnNewEntry_Click(object sender, RoutedEventArgs e)
    {
        _selectedEntryIds.Clear();
        _selectedEntry = null;
        _draftStart = null;
        ClearEditor(resetTime: true, rerenderTimeline: true);
        TxtDescription.Focus();
    }

    private void BtnSaveEntry_Click(object sender, RoutedEventArgs e)
    {
        var repository = _repository;
        var access = _access;
        var user = CmbUser.SelectedItem as WorkLogUser;

        if (repository is null || access is null || user is null)
        {
            return;
        }

        if (!access.CanEditUser(user))
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WORKLOG.Dialog.ValidationTitle"),
                T("WORKLOG.Dialog.NotAllowed"));
            return;
        }

        var date = _selectedDate.Date;

        if (!TimeSpan.TryParse(TxtTime.Text.Trim(), out var time) || time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WORKLOG.Dialog.ValidationTitle"),
                T("WORKLOG.Dialog.InvalidTime"));
            return;
        }

        if (!int.TryParse(TxtMinutes.Text.Trim(), out var minutes) || minutes <= 0)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WORKLOG.Dialog.ValidationTitle"),
                T("WORKLOG.Dialog.InvalidMinutes"));
            return;
        }

        if (time.TotalMinutes + minutes > 24 * 60)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WORKLOG.Dialog.ValidationTitle"),
                LocalizedOr("WORKLOG.Agenda.EntryOutsideDay", "Záznam nesmí přesáhnout konec vybraného dne."));
            return;
        }

        var project = CmbProject.SelectedItem as WorkLogProject;
        var entryType = CmbEntryType.SelectedItem as WorkLogEntryType;

        if (project is null || entryType is null)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WORKLOG.Dialog.ValidationTitle"),
                T("WORKLOG.Dialog.ProjectAndTypeRequired"));
            return;
        }

        var original = _selectedEntry?.Id > 0
            ? repository.GetTimeEntry(_selectedEntry.Id)
            : null;

        var item = new WorkLogTimeEntry
        {
            Id = _selectedEntry?.Id ?? 0,
            UserId = user.Id,
            ProjectId = project.Id,
            EntryTypeId = entryType.Id,
            Timestamp = date.Add(time),
            Description = TxtDescription.Text.Trim(),
            EntryMinutes = minutes,
            Note = TxtNote.Text.Trim(),
            AfterCare = _selectedEntry?.AfterCare ?? false,
            IsValid = true
        };

        try
        {
            var id = repository.SaveTimeEntry(item);

            if (original is null)
            {
                _logger?.AuditCreated(
                    "WORKLOG",
                    "TimeEntry",
                    id.ToString(CultureInfo.InvariantCulture),
                    _currentUserName,
                    $"UserId={user.Id}; Date={item.Timestamp:yyyy-MM-dd}; Time={item.Timestamp:HH:mm}; ProjectId={project.Id}; Minutes={minutes}");
            }
            else
            {
                item.Id = id;
                LogEntryChanges(original, item);
            }

            _draftStart = null;
            _selectedEntryIds.Clear();
            _selectedEntryIds.Add(id);
            _selectedEntry = null;

            TxtStatus.Text = T("WORKLOG.Status.EntrySaved");
            RefreshSelectedDay(scrollToWorkStart: false);
        }
        catch (Exception ex)
        {
            _logger?.Error("WORKLOG: time entry save failed.", ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WORKLOG.Dialog.ErrorTitle"),
                TF("WORKLOG.Dialog.SaveFailed", ex.Message));
        }
    }

    private void BtnDeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedEntry is null)
        {
            return;
        }

        var user = CmbUser.SelectedItem as WorkLogUser;
        if (user is null || _access?.CanEditUser(user) != true)
        {
            return;
        }

        var confirm = DmsConfirmDialog.ShowQuestion(
            Window.GetWindow(this),
            T("WORKLOG.Dialog.DeleteTitle"),
            T("WORKLOG.Dialog.DeleteQuestion"));

        if (!confirm)
        {
            return;
        }

        try
        {
            var id = _selectedEntry.Id;
            _repository.DeleteTimeEntry(id);

            _logger?.AuditDeleted(
                "WORKLOG",
                "TimeEntry",
                id.ToString(CultureInfo.InvariantCulture),
                _currentUserName,
                $"UserId={user.Id}");

            _selectedEntryIds.Remove(id);
            _selectedEntry = null;
            _draftStart = null;
            TxtStatus.Text = T("WORKLOG.Status.EntryDeleted");
            RefreshSelectedDay(scrollToWorkStart: false);
        }
        catch (Exception ex)
        {
            _logger?.Error("WORKLOG: time entry delete failed.", ex);

            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WORKLOG.Dialog.ErrorTitle"),
                TF("WORKLOG.Dialog.DeleteFailed", ex.Message));
        }
    }

    private void BtnBrowseDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = T("WORKLOG.FileDialog.Title"),
            Filter = T("WORKLOG.FileDialog.Filter"),
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            TxtDatabasePath.Text = dialog.FileName;
        }
    }

    private void BtnSaveDatabasePath_Click(object sender, RoutedEventArgs e)
    {
        var path = TxtDatabasePath.Text.Trim();

        if (string.IsNullOrWhiteSpace(path))
        {
            path = WorkLogSettings.DefaultDatabasePath;
        }

        try
        {
            var probe = new WorkLogRepository(path);
            probe.TestConnection();

            _settings.DatabasePath = path;
            _settingsService.Save(_settings);

            _logger?.AdminAction(
                "WORKLOG",
                "SaveDatabasePath",
                _currentUserName,
                $"Path={path}");

            LoadDatabase(
                keepSelectedUserId: (CmbUser.SelectedItem as WorkLogUser)?.Id,
                showDialogOnFailure: true);

            TxtStatus.Text = T("WORKLOG.Status.PathSaved");
        }
        catch (Exception ex)
        {
            DmsConfirmDialog.ShowInfo(
                Window.GetWindow(this),
                T("WORKLOG.Dialog.ErrorTitle"),
                TF("WORKLOG.Dialog.DatabaseInvalid", ex.Message));
        }
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        var selectedUserId = (CmbUser.SelectedItem as WorkLogUser)?.Id;
        LoadDatabase(selectedUserId, showDialogOnFailure: true);
    }

    private void LogEntryChanges(WorkLogTimeEntry oldItem, WorkLogTimeEntry newItem)
    {
        LogChange(oldItem.ProjectId?.ToString(), newItem.ProjectId?.ToString(), "ProjectId", newItem.Id);
        LogChange(oldItem.EntryTypeId?.ToString(), newItem.EntryTypeId?.ToString(), "EntryTypeId", newItem.Id);
        LogChange(
            oldItem.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
            newItem.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
            "Timestamp",
            newItem.Id);
        LogChange(oldItem.Description, newItem.Description, "Description", newItem.Id);
        LogChange(oldItem.EntryMinutes.ToString(), newItem.EntryMinutes.ToString(), "EntryMinutes", newItem.Id);
        LogChange(oldItem.Note, newItem.Note, "Note", newItem.Id);
    }

    private void LogChange(string? oldValue, string? newValue, string field, int id)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        _logger?.AuditChange(
            "WORKLOG",
            "TimeEntry",
            id.ToString(CultureInfo.InvariantCulture),
            field,
            oldValue,
            newValue,
            _currentUserName);
    }

    private static SolidColorBrush ParseColorBrush(string? value, byte alpha)
    {
        try
        {
            var parsed = ColorConverter.ConvertFromString(
                string.IsNullOrWhiteSpace(value) ? "#315A7D" : value.Trim());

            if (parsed is Color color)
            {
                return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            }
        }
        catch
        {
            // Use fallback below.
        }

        return new SolidColorBrush(Color.FromArgb(alpha, 49, 90, 125));
    }

    private static Brush GetContrastingBrush(Color color)
    {
        var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
        return luminance > 0.62 ? Brushes.Black : Brushes.White;
    }

    private static string FormatTime(DateTime? value) => value?.ToString("HH:mm") ?? "—";

    private string LocalizedOr(string key, string fallback)
    {
        var value = T(key);
        return string.Equals(value, key, StringComparison.OrdinalIgnoreCase) ? fallback : value;
    }

    private string FormatLocalizedOr(string key, string fallback, params object[] args)
    {
        var format = LocalizedOr(key, fallback);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch
        {
            return format;
        }
    }

    private string T(string key)
    {
        if (_translate is null)
        {
            return key;
        }

        var value = _translate(key);
        return string.IsNullOrWhiteSpace(value) ||
               string.Equals(value, $"[[{key}]]", StringComparison.OrdinalIgnoreCase)
            ? key
            : value;
    }

    private string TF(string key, params object[] args)
    {
        if (_translateFormat is not null)
        {
            return _translateFormat(key, args);
        }

        var format = T(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch
        {
            return format;
        }
    }
}
