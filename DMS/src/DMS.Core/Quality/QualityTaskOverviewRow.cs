namespace DMS.Core.Quality;

public sealed class QualityTaskOverviewRow
{
    public string PrintVersionNumber { get; init; } = string.Empty;

    public int Number { get; init; }

    public string Text { get; init; } = string.Empty;

    public DateTime? CreatedAt { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public DateTime? DueDate { get; init; }

    public DateTime? CompletedAt { get; init; }

    public string CompletedBy { get; init; } = string.Empty;

    public bool IsCompleted => CompletedAt.HasValue;

    public string DueDateText =>
        DueDate?.ToString("dd.MM.yyyy") ?? string.Empty;

    public string CompletedText =>
        IsCompleted
            ? "Splněno"
            : "Nesplněno";
}