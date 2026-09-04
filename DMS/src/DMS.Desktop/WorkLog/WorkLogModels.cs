namespace DMS.Desktop.WorkLog;

public sealed class WorkLogUser
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public int PersonalNumber { get; set; }
    public string WindowsUsername { get; set; } = string.Empty;
    public int LevelOfAccess { get; set; }
    public int? UserGroupId { get; set; }
    public string UserGroupTitle { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int? MasterUserId { get; set; }
    public bool IsArchived { get; set; }

    public string FullName =>
        string.Join(
            " ",
            new[] { FirstName, Surname }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

    public bool IsExternal =>
        string.Equals(
            UserGroupTitle,
            "EXTERNISTÉ",
            StringComparison.OrdinalIgnoreCase);

    public string DisplayText =>
        $"{Surname}, {FirstName} [{PersonalNumber}]";

    public override string ToString() => DisplayText;
}

public sealed class WorkLogUserGroup
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public override string ToString() => Title;
}

public sealed class WorkLogProject
{
    public int Id { get; set; }
    public int ProjectType { get; set; }
    public string ProjectTitle { get; set; } = string.Empty;
    public string ProjectDescription { get; set; } = string.Empty;
    public int CreatedBy { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public string DateFullFilled { get; set; } = string.Empty;

    public string DisplayText =>
        string.IsNullOrWhiteSpace(ProjectDescription)
            ? ProjectTitle
            : $"{ProjectTitle} — {ProjectDescription}";

    public override string ToString() => DisplayText;
}

public sealed class WorkLogEntryType
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Color { get; set; } = "#ADD8E6";
    public int? ForProjectType { get; set; }

    public string DisplayText => Title;

    public override string ToString() => DisplayText;
}

public sealed class WorkLogTimeEntry
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? ProjectId { get; set; }
    public string ProjectTitle { get; set; } = string.Empty;
    public int? EntryTypeId { get; set; }
    public string EntryTypeTitle { get; set; } = string.Empty;
    public string EntryTypeColor { get; set; } = "#315A7D";
    public DateTime Timestamp { get; set; }
    public string Description { get; set; } = string.Empty;
    public int EntryMinutes { get; set; } = 30;
    public bool AfterCare { get; set; }
    public string Note { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public bool IsValid { get; set; }

    public string TimeText => Timestamp.ToString("HH:mm");
    public string DurationText =>
        EntryMinutes % 60 == 0
            ? $"{EntryMinutes / 60} h"
            : $"{EntryMinutes / 60} h {EntryMinutes % 60} min";
}

public sealed class WorkLogArrivalDeparture
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime WorkDate { get; set; }
    public DateTime? ArrivalTimestamp { get; set; }
    public DateTime? DepartureTimestamp { get; set; }
    public string DepartureReason { get; set; } = string.Empty;
    public double HoursWorked { get; set; }
    public double HoursOvertime { get; set; }
}

public sealed class WorkLogSpecialDay
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool Locked { get; set; }
    public string Color { get; set; } = string.Empty;
}


public sealed class WorkLogDayOverviewRow
{
    public DateTime Date { get; set; }
    public DateTime? ArrivalTimestamp { get; set; }
    public DateTime? DepartureTimestamp { get; set; }
    public double HoursWorked { get; set; }
    public double HoursOvertime { get; set; }
    public int EntryMinutes { get; set; }
    public int EntryCount { get; set; }
    public bool IsLocked { get; set; }
    public string SpecialTitle { get; set; } = string.Empty;

    public bool IsToday => Date.Date == DateTime.Today;
    public bool IsWeekend =>
        Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public string DateText => Date.ToString("dd.MM.yyyy");
    public string DayText => Date.ToString("dddd");
    public string ArrivalText => ArrivalTimestamp?.ToString("HH:mm") ?? "—";
    public string DepartureText => DepartureTimestamp?.ToString("HH:mm") ?? "—";

    public string WorkedText
    {
        get
        {
            if (HoursWorked > 0)
            {
                return FormatHours(HoursWorked);
            }

            if (ArrivalTimestamp.HasValue && DepartureTimestamp.HasValue)
            {
                var duration =
                    DepartureTimestamp.Value -
                    ArrivalTimestamp.Value;

                if (duration.TotalMinutes > 0)
                {
                    return FormatMinutes(
                        (int)Math.Round(duration.TotalMinutes));
                }
            }

            return "—";
        }
    }

    public string OvertimeText =>
        HoursOvertime == 0
            ? "—"
            : FormatHours(HoursOvertime);

    public string LoggedText =>
        EntryMinutes <= 0
            ? "—"
            : FormatMinutes(EntryMinutes);

    public string EntryCountText =>
        EntryCount <= 0
            ? "—"
            : EntryCount.ToString();

    public string StatusText =>
        IsLocked
            ? string.IsNullOrWhiteSpace(SpecialTitle)
                ? "🔒"
                : $"🔒 {SpecialTitle}"
            : SpecialTitle;

    private static string FormatHours(double hours)
    {
        var minutes = (int)Math.Round(hours * 60.0);
        return FormatMinutes(minutes);
    }

    private static string FormatMinutes(int minutes)
    {
        var sign = minutes < 0 ? "-" : string.Empty;
        var absolute = Math.Abs(minutes);

        return $"{sign}{absolute / 60}:{absolute % 60:00}";
    }
}

public sealed class WorkLogDatabaseInfo
{
    public string DatabasePath { get; set; } = string.Empty;
    public string SqliteVersion { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public int Users { get; set; }
    public int ActiveUsers { get; set; }
    public int Projects { get; set; }
    public int TimeEntries { get; set; }
    public int ArrivalsDepartures { get; set; }
    public int LockedSpecialDays { get; set; }

    public string FileSizeText =>
        FileSizeBytes < 1024 * 1024
            ? $"{FileSizeBytes / 1024.0:0.0} kB"
            : $"{FileSizeBytes / (1024.0 * 1024.0):0.0} MB";
}
