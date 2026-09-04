namespace DMS.Core.Quality;

public sealed class QualityPrintVersionListRow
{
    public bool TasksCompleted { get; init; }

    public string TaskStatusIcon => TasksCompleted ? "✓" : "ϟ";

    public string SapMaterialNumber { get; init; } = string.Empty;

    public bool MissingSapMaterialNumber => string.IsNullOrWhiteSpace(SapMaterialNumber);

    public string FullPrintVersionNumber { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Decoration { get; init; } = string.Empty;

    public string Customer { get; init; } = string.Empty;

    public string ColorType { get; init; } = string.Empty;

    public string SortKey { get; init; } = string.Empty;

}