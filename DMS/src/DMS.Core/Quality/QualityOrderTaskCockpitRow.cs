namespace DMS.Core.Quality;

public sealed class QualityOrderTaskCockpitRow
{
    public string OrderNumber { get; init; } = string.Empty;
    public string SapMaterialNumber { get; init; } = string.Empty;
    public string PrintVersionNumber { get; init; } = string.Empty;
    public int TaskNumber { get; init; }
    public string TaskText { get; init; } = string.Empty;
    public DateTime? CreatedAt { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    public DateTime? DueDate { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string CompletedBy { get; init; } = string.Empty;

    public bool IsCompleted => CompletedAt.HasValue;
    public string CreatedAtText => CreatedAt?.ToString("dd.MM.yyyy") ?? string.Empty;
    public string DueDateText => DueDate?.ToString("dd.MM.yyyy") ?? string.Empty;
    public string CompletedAtText => CompletedAt?.ToString("dd.MM.yyyy") ?? string.Empty;

    public int DelayDays
    {
        get
        {
            if (IsCompleted || DueDate is null)
            {
                return 0;
            }

            return Math.Max(0, (DateTime.Today - DueDate.Value.Date).Days);
        }
    }

    public string DelayText => DelayDays <= 0 ? string.Empty : $"{DelayDays} dní";

    public string DelaySeverity => DelayDays >= 6
        ? "Red"
        : DelayDays > 0
            ? "Yellow"
            : "None";
}
