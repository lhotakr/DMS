namespace DMS.Core.Quality;

public sealed class QualityTask
{
    public int Number { get; init; }

    public string Text { get; init; } = string.Empty;

    public DateTime? DueDate { get; init; }

    public DateTime? CreatedAt { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public DateTime? CompletedAt { get; init; }

    public string CompletedBy { get; init; } = string.Empty;
}