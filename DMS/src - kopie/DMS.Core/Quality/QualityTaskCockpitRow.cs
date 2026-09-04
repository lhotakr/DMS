namespace DMS.Core.Quality;

public sealed class QualityTaskCockpitRow
{
    public string SapMaterialNumber { get; init; } = string.Empty;

    public string MaterialStatus { get; init; } = string.Empty;

    public string OldMaterialNumber { get; init; } = string.Empty;

    public int TaskNumber { get; init; }

    public string TaskText { get; init; } = string.Empty;

    public DateTime? CreatedAt { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public DateTime? DueDate { get; init; }

    public DateTime? CompletedAt { get; init; }

    public string CompletedBy { get; init; } = string.Empty;

    public string FullPrintVersionNumber { get; init; } = string.Empty;

    public bool IsCompleted => CompletedAt.HasValue;

    public string CreatedAtText =>
        CreatedAt?.ToString("dd.MM.yyyy") ?? string.Empty;

    public string DueDateText =>
        DueDate?.ToString("dd.MM.yyyy") ?? string.Empty;

    public string CompletedAtText =>
        CompletedAt?.ToString("dd.MM.yyyy") ?? string.Empty;

    public int DelayDays
    {
        get
        {
            if (IsCompleted || DueDate is null)
            {
                return 0;
            }

            var days =
                (DateTime.Today - DueDate.Value.Date).Days;

            return Math.Max(0, days);
        }
    }

    public string DelayText =>
        DelayDays <= 0
            ? string.Empty
            : $"{DelayDays} dní";

    public string DelaySeverity
    {
        get
        {
            if (DelayDays >= 6)
            {
                return "Red";
            }

            if (DelayDays > 0)
            {
                return "Yellow";
            }

            return "None";
        }
    }
}